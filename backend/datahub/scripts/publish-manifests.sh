#!/usr/bin/env bash
# publish-manifests.sh — publish the control-plane seed objects to a DataHub API.
#
#   ./publish-manifests.sh --env-file /path/to/.env.staging
#   ./publish-manifests.sh --env-file /path/to/.env.production --seed-dir ../seeds
#   ./publish-manifests.sh --api-url https://datahub.example.com --token-file /root/.datahub-admin-token
#   ./publish-manifests.sh --env-file /path/to/.env.staging --dry-run
#
# A fresh VPS serves 404 for every policy path, and that 404 is not neutral:
# VpsRuntimePolicyService falls through to SafeDefault("BASE"), so every ULTRA
# station silently runs as BASE with nothing failing anywhere. Running this after
# the migrations is what makes a new deployment usable. See ../seeds/README.md.
#
# Only curl is required. The admin token is never placed on a command line
# (argv is world-readable through /proc): it reaches curl through a --config
# document on stdin, written by a shell builtin so it never becomes a process
# argument either.
#
# Each object is published and then read back over the ANONYMOUS path, and the
# two ETags are compared. That second request is the point: a PUT accepted by the
# API while the public GET still 404s is exactly what a misconfigured reverse
# proxy looks like, and it is invisible if you only check the publish response.
set -euo pipefail

. "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/_datahub-common.sh"

SEED_DIR=""
API_URL=""
TOKEN_FILE=""
DRY_RUN=0

datahub::parse_common_args "$@"
set -- ${DATAHUB_REST+"${DATAHUB_REST[@]}"}
while [ "$#" -gt 0 ]; do
    case "$1" in
        --seed-dir)     SEED_DIR="${2-}"; shift 2 ;;
        --seed-dir=*)   SEED_DIR="${1#*=}"; shift ;;
        --api-url)      API_URL="${2-}"; shift 2 ;;
        --api-url=*)    API_URL="${1#*=}"; shift ;;
        --token-file)   TOKEN_FILE="${2-}"; shift 2 ;;
        --token-file=*) TOKEN_FILE="${1#*=}"; shift ;;
        --dry-run)      DRY_RUN=1; shift ;;
        *) datahub::die "unexpected argument: $1" ;;
    esac
done

datahub::need curl
SEED_DIR="${SEED_DIR:-$DATAHUB_DIR/seeds}"
[ -d "$SEED_DIR" ] || datahub::die "seed directory does not exist: $SEED_DIR"

if [ -n "$DATAHUB_ENV_FILE" ]; then
    [ -f "$DATAHUB_ENV_FILE" ] || datahub::die "env file not found: $DATAHUB_ENV_FILE"
fi

# --- where to publish --------------------------------------------------------
if [ -z "$API_URL" ]; then
    [ -n "$DATAHUB_ENV_FILE" ] || datahub::die \
        "pass --api-url, or --env-file so DATAHUB_PUBLIC_HOST can supply it."
    public_host="$(datahub::env_value DATAHUB_PUBLIC_HOST)"
    [ -n "$public_host" ] || datahub::die \
        "DATAHUB_PUBLIC_HOST is not set in $DATAHUB_ENV_FILE; pass --api-url instead."
    API_URL="https://$public_host"
fi
API_URL="${API_URL%/}"
# Plain http would put the admin token on the wire in clear text. Localhost is
# the one exception, for publishing from inside the VPS before DNS or TLS exists.
case "$API_URL" in
    https://*) ;;
    http://localhost*|http://127.0.0.1*) ;;
    *) datahub::die "refusing to send the admin token over $API_URL; use https, or http on localhost only." ;;
esac

# --- the admin token ---------------------------------------------------------
ADMIN_TOKEN=""
if [ -n "$TOKEN_FILE" ]; then
    [ -r "$TOKEN_FILE" ] || datahub::die "token file not readable: $TOKEN_FILE"
    # Only the first line, and trailing whitespace removed: a token with a stray
    # newline in the header produces a 401 that looks like a wrong secret.
    ADMIN_TOKEN="$(head -n 1 "$TOKEN_FILE" | tr -d '\r\n[:blank:]')"
elif [ -n "$DATAHUB_ENV_FILE" ]; then
    ADMIN_TOKEN="$(datahub::env_value DATAHUB_ADMIN_TOKEN | tr -d '\r[:blank:]')"
fi
[ -n "$ADMIN_TOKEN" ] || datahub::die \
    "no admin token: pass --token-file PATH, or --env-file containing DATAHUB_ADMIN_TOKEN."
case "$ADMIN_TOKEN" in
    REPLACE_WITH*) datahub::die "DATAHUB_ADMIN_TOKEN is still the template placeholder." ;;
esac

printf 'DataHub manifest publish\n'
printf '  api        %s\n' "$API_URL"
printf '  seeds      %s\n' "$SEED_DIR"
printf '  admin token %s\n' "$(datahub::mask "$ADMIN_TOKEN")"

# --- helpers -----------------------------------------------------------------
content_type_for() {
    case "$1" in
        *.json) printf 'application/json; charset=utf-8' ;;
        *)      printf 'application/octet-stream' ;;
    esac
}

# Header names are case-insensitive and both curl and any proxy in between may
# change the case, so match it without relying on awk's IGNORECASE (mawk, the
# default awk on Ubuntu, does not have it).
#
# Both helpers read the LAST match, not the first: a dumped header file can hold
# more than one block. curl adds Expect: 100-continue once a body passes 1 KiB,
# so the first status line would be "100 Continue" — a seed large enough to
# trigger it would otherwise be reported as an unrecognised failure.
etag_of() {
    tr -d '\r' < "$1" | sed -n 's/^[Ee][Tt][Aa][Gg]: *//p' | tail -n 1
}

status_of() {
    tr -d '\r' < "$1" | sed -n 's|^HTTP/[0-9.]* \([0-9][0-9][0-9]\).*|\1|p' | tail -n 1
}

published=0
failed=0
work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT

publish_one() {
    local object_path="$1" file="$2"
    local put_headers="$work_dir/put.headers" put_body="$work_dir/put.body"
    local get_headers="$work_dir/get.headers"

    # The token is interpolated by printf, a bash builtin: no fork, so it never
    # appears in any process's argv.
    if ! printf 'header = "Authorization: Bearer %s"\n' "$ADMIN_TOKEN" \
        | curl --config - \
               --silent --show-error \
               --header "Content-Type: $(content_type_for "$object_path")" \
               --upload-file "$file" \
               --dump-header "$put_headers" \
               --output "$put_body" \
               "$API_URL/api/v1/admin/manifests/$object_path"
    then
        printf 'FAIL  %s (curl could not reach the API)\n' "$object_path"
        failed=$((failed + 1))
        return
    fi

    local put_status put_etag
    put_status="$(status_of "$put_headers")"
    case "$put_status" in
        200|201) ;;
        *)
            printf 'FAIL  %s (HTTP %s) %s\n' "$object_path" "${put_status:-none}" "$(tr -d '\n' < "$put_body")"
            failed=$((failed + 1))
            return ;;
    esac
    put_etag="$(etag_of "$put_headers")"

    # No credentials on this request on purpose: this is the request a station
    # makes, and it is the only one that proves the object is actually reachable.
    if ! curl --silent --show-error --head \
              --dump-header "$get_headers" \
              --output /dev/null \
              "$API_URL/$object_path"
    then
        printf 'WARN  %s published, but the anonymous read-back could not be performed\n' "$object_path"
        published=$((published + 1))
        return
    fi

    local get_status get_etag
    get_status="$(status_of "$get_headers")"
    get_etag="$(etag_of "$get_headers")"
    if [ "$get_status" != "200" ]; then
        printf 'FAIL  %s published (HTTP %s) but GET /%s answered %s — check the reverse proxy\n' \
            "$object_path" "$put_status" "$object_path" "${get_status:-none}"
        failed=$((failed + 1))
        return
    fi
    if [ -n "$put_etag" ] && [ "$put_etag" != "$get_etag" ]; then
        printf 'FAIL  %s published but the served copy has a different ETag (%s vs %s) — something else is answering\n' \
            "$object_path" "$put_etag" "$get_etag"
        failed=$((failed + 1))
        return
    fi

    printf 'OK    %s (HTTP %s, etag %s)\n' "$object_path" "$put_status" "${get_etag:-unknown}"
    published=$((published + 1))
}

# --- walk the seed tree ------------------------------------------------------
# The directory layout is the object path: seeds/configs/runtime-policy.json is
# published as configs/runtime-policy.json. -print0 so a name with a space
# cannot be split into two arguments.
found_any=0
while IFS= read -r -d '' file; do
    object_path="${file#"$SEED_DIR"/}"
    found_any=1
    if [ "$DRY_RUN" -eq 1 ]; then
        printf 'DRY   %s -> PUT %s/api/v1/admin/manifests/%s\n' "$object_path" "$API_URL" "$object_path"
        continue
    fi
    publish_one "$object_path" "$file"
done < <(find "$SEED_DIR" -type f ! -name 'README.md' ! -name '.*' -print0 | sort -z)

[ "$found_any" -eq 1 ] || datahub::die "no publishable files found under $SEED_DIR"

if [ "$DRY_RUN" -eq 1 ]; then
    printf 'Dry run only; nothing was published.\n'
    exit 0
fi

printf 'DataHub manifest publish complete (%d published, %d failed).\n' "$published" "$failed"
[ "$failed" -eq 0 ] || exit 1
