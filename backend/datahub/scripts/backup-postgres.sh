#!/usr/bin/env bash
# backup-postgres.sh - dump the DataHub database out of the postgres container.
#
#   ./backup-postgres.sh --env-file /opt/autojms-datahub/.env.production --output-dir /srv/datahub-backups
#   ./backup-postgres.sh --env-file /opt/autojms-datahub/.env.production --output-dir /srv/datahub-backups --critical-only
#
# The bash counterpart to backup-postgres.ps1, for a plain Ubuntu VPS that runs
# Docker and nothing else. It mirrors that script's -ComposeFile mode: pg_dump
# runs INSIDE the container, reusing the credentials the container already holds,
# so no password is ever placed on a command line where /proc would expose it.
# Only the finished dump crosses the container boundary.
#
# Host mode (the .ps1 script's -DatabaseUrl) is deliberately absent, for the same
# reason apply-migrations.sh gives: this host has no pg_dump, and an untested
# host path would be worse than no host path. Use the .ps1 script where a real
# client is installed.
#
# --critical-only keeps the full schema of every table but drops the rows of the
# observation tables listed in critical-backup-exclusions.txt, which both this
# script and the .ps1 script read so the two cannot drift. Read that file before
# using the flag: excluding dashboard_changes carries a mandatory post-restore
# step, and the script reprints it on every critical run.
set -euo pipefail

. "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/_datahub-common.sh"

OUTPUT_DIR=""
CRITICAL_ONLY=0

datahub::parse_common_args "$@"
set -- ${DATAHUB_REST+"${DATAHUB_REST[@]}"}
while [ "$#" -gt 0 ]; do
    case "$1" in
        --output-dir)   OUTPUT_DIR="${2-}"; shift 2 ;;
        --output-dir=*) OUTPUT_DIR="${1#*=}"; shift ;;
        --critical-only) CRITICAL_ONLY=1; shift ;;
        *) datahub::die "unexpected argument: $1" ;;
    esac
done

datahub::require_stack
[ -n "$OUTPUT_DIR" ] || datahub::die "an output directory is required: pass --output-dir PATH."
mkdir -p -- "$OUTPUT_DIR"

# --- which tables lose their data --------------------------------------------
EXCLUDED_TABLES=()
EXCLUDE_ARGS=()
if [ "$CRITICAL_ONLY" -eq 1 ]; then
    exclusion_file="$DATAHUB_SCRIPT_DIR/critical-backup-exclusions.txt"
    [ -r "$exclusion_file" ] || datahub::die "--critical-only requires $exclusion_file, which is missing."
    # `|| [ -n "$line" ]` so a final line without a trailing newline is not dropped.
    while IFS= read -r line || [ -n "$line" ]; do
        line="${line%%#*}"
        line="${line#"${line%%[![:space:]]*}"}"
        line="${line%"${line##*[![:space:]]}"}"
        [ -n "$line" ] || continue
        EXCLUDED_TABLES+=("$line")
        EXCLUDE_ARGS+=("--exclude-table-data=$line")
    done < "$exclusion_file"
    [ "${#EXCLUDED_TABLES[@]}" -gt 0 ] \
        || datahub::die "$exclusion_file names no tables, so --critical-only would silently produce a full backup."
fi

STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
TARGET="$OUTPUT_DIR/datahub-$STAMP.dump"
# Unique per run so two operators dumping at once cannot overwrite each other's
# work file inside the container.
CONTAINER_TARGET="/tmp/datahub-$STAMP-$$-${RANDOM}.dump"

# The work file is removed whether pg_dump succeeded, failed or was interrupted:
# a half-written dump left in the container is a disk leak nobody goes looking for.
cleanup() {
    datahub::compose exec -T "$DATAHUB_POSTGRES_SERVICE" \
        rm -f -- "$CONTAINER_TARGET" </dev/null >/dev/null 2>&1 || true
}
trap cleanup EXIT

printf 'DataHub backup\n'
printf '  service    %s\n' "$DATAHUB_POSTGRES_SERVICE"
printf '  target     %s\n' "$TARGET"
if [ "$CRITICAL_ONLY" -eq 1 ]; then
    printf '  mode       critical-only (full schema; no data for %s)\n' "$(IFS=', '; printf '%s' "${EXCLUDED_TABLES[*]}")"
else
    printf '  mode       full\n'
fi

# The table names arrive as positional arguments instead of being spliced into
# the command string: a table name is data, and building shell text out of data
# is how a quoting bug turns into command injection.
datahub::compose exec -T "$DATAHUB_POSTGRES_SERVICE" sh -ec \
    'file="$1"; shift; exec pg_dump --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --format=custom --compress=6 --file "$file" "$@"' \
    sh "$CONTAINER_TARGET" ${EXCLUDE_ARGS+"${EXCLUDE_ARGS[@]}"} </dev/null \
    || datahub::die "container pg_dump failed."

datahub::compose cp "$DATAHUB_POSTGRES_SERVICE:$CONTAINER_TARGET" "$TARGET" \
    || datahub::die "docker compose cp failed; the dump is still inside the container at $CONTAINER_TARGET."

printf 'Created %s (%s bytes). Encrypt and upload it outside this script.\n' \
    "$TARGET" "$(wc -c < "$TARGET" | tr -d '[:space:]')"

if [ "$CRITICAL_ONLY" -eq 1 ]; then
    printf '\nAfter restoring this dump, run:\n'
    printf '    UPDATE site_change_counters SET pruned_through_seq = change_seq;\n'
    printf 'Skipping that leaves a station that was offline during the migration believing\n'
    printf 'it is up to date. See scripts/critical-backup-exclusions.txt.\n'
fi
