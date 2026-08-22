#!/usr/bin/env bash
# smoke-test.sh — end-to-end staging smoke against a running DataHub stack.
#
#   ./smoke-test.sh --env-file /path/to/.env.staging
#   ./smoke-test.sh --env-file /path/to/.env.staging --base https://datahub.example.com
#
# Ten steps: provision a site, mint a staging license assertion, enroll a device,
# acquire the leader lease, ingest behind the fence, replay the same
# Idempotency-Key, read the change feed, read the snapshot, run five negative
# cases, release the lease. Exits non-zero if any check fails.
#
# Only ever run this against staging. It writes real rows, and it needs
# DATAHUB_ALLOW_STAGING_TEST_ISSUER=true to mint its own license assertion --
# which production must never have enabled.
#
# No secret is printed. The device token and the assertion are shown as
# first4...last4 only, which is enough to prove one was issued.
#
# `set -e` is deliberately NOT used: a failing check must be recorded and the run
# must continue, so that one run reports every broken contract rather than only
# the first.
set -uo pipefail

. "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/_datahub-common.sh"

BASE="${DATAHUB_SMOKE_BASE-}"
datahub::parse_common_args "$@"
set -- ${DATAHUB_REST+"${DATAHUB_REST[@]}"}
while [ "$#" -gt 0 ]; do
    case "$1" in
        --base)   BASE="${2-}"; shift 2 ;;
        --base=*) BASE="${1#*=}"; shift ;;
        *) datahub::die "unexpected argument: $1" ;;
    esac
done

datahub::require_stack
datahub::need curl
datahub::need jq
datahub::need python3

# Base URL resolution: --base, then DATAHUB_SMOKE_BASE, then DATAHUB_PUBLIC_HOST
# from the env file. DATAHUB_PUBLIC_HOST is the Caddy site address, so it decides
# the scheme: given an explicit http:// Caddy serves plain HTTP, otherwise it
# applies automatic HTTPS. A bare IP address must therefore carry http://,
# because automatic HTTPS cannot work for an IP literal -- no client sends SNI
# for one, so the certificate can never be selected and every handshake fails.
if [ -z "$BASE" ]; then
    public_host="$(datahub::env_value DATAHUB_PUBLIC_HOST)"
    [ -n "$public_host" ] || datahub::die \
        "cannot determine the base URL: pass --base URL or set DATAHUB_PUBLIC_HOST in the env file."
    case "$public_host" in
        http://*|https://*) BASE="$public_host" ;;
        *)                  BASE="https://$public_host" ;;
    esac
fi
BASE="${BASE%/}"

PASS=0
FAIL=0
ok()  { PASS=$((PASS + 1)); printf 'PASS  %s\n' "$1"; }
bad() { FAIL=$((FAIL + 1)); printf 'FAIL  %s\n' "$1"; }
chk() {
    if [ "$2" = "$3" ]; then ok "$1 (=$3)"; else bad "$1 (expected $3, got $2)"; fi
}

# req METHOD PATH [curl args...] -> sets HTTP and BODY
req() {
    local method="$1" path="$2"; shift 2
    local out
    out=$(curl -sS -X "$method" "$BASE$path" -w $'\n%{http_code}' "$@" 2>&1)
    HTTP="${out##*$'\n'}"
    BODY="${out%$'\n'*}"
}

printf 'DataHub smoke against %s\n\n' "$BASE"

# create_datahub_site is not idempotent (sites_site_code_key), and steps 7-8
# assert exact counts, so every run gets its own site rather than reusing one.
SITE_CODE="SMK$(date -u +%y%m%d%H%M%S)"
if [ -r /proc/sys/kernel/random/uuid ]; then
    SITE_ID="$(cat /proc/sys/kernel/random/uuid)"
else
    SITE_ID="$(python3 -c 'import uuid; print(uuid.uuid4())')"
fi
echo "== step 1: provision site $SITE_CODE ($SITE_ID) =="
# The SQL goes in on stdin because :'var' is only expanded for stdin or -f.
if printf '%s\n' "BEGIN; SELECT create_datahub_site(:'site_id'::uuid, :'site_code'); COMMIT;" \
     | datahub::psql_stdin --set ON_ERROR_STOP=1 \
           --variable "site_id=$SITE_ID" --variable "site_code=$SITE_CODE" >/dev/null 2>&1; then
    ok "create_datahub_site"
else
    bad "create_datahub_site"
    exit 1
fi

echo "== step 2: mint staging license assertion (HMAC-SHA256, key stays in env) =="
EXP=$(( $(date -u +%s) + 8 * 3600 ))
# The payload is compact PascalCase JSON: the API deserializes it with the
# default, case-SENSITIVE options, so camelCase keys would not bind.
# The signing key is handed over as an environment variable, never as an
# argument, because command lines are world-readable through /proc.
ASSERTION=$(SITE_CODE="$SITE_CODE" EXP="$EXP" \
    ISS="$(datahub::env_value DATAHUB_LICENSE_ASSERTION_ISSUER)" \
    AUD="$(datahub::env_value DATAHUB_LICENSE_ASSERTION_AUDIENCE)" \
    KEY="$(datahub::env_value DATAHUB_STAGING_TEST_SIGNING_KEY)" python3 - <<'PY'
import os, json, hmac, hashlib, base64

def b64u(raw):
    return base64.urlsafe_b64encode(raw).decode().rstrip('=')

payload = {
    "Channel": "staging",
    "SiteCodes": [os.environ["SITE_CODE"]],
    "ExpiresAt": int(os.environ["EXP"]),
    "DataHubUrl": None,
    "Seats": 1,
    "TokenVersion": 1,
    "Issuer": os.environ["ISS"],
    "Audience": os.environ["AUD"],
}
encoded = b64u(json.dumps(payload, separators=(',', ':')).encode())
# The signature covers the UTF-8 bytes of the *encoded* payload string.
signature = hmac.new(os.environ["KEY"].encode(), encoded.encode(), hashlib.sha256).digest()
print(f"v1.{encoded}.{b64u(signature)}")
PY
)
case "$ASSERTION" in
    v1.*.*) ok "assertion minted $(datahub::mask "$ASSERTION")" ;;
    *)      bad "assertion mint failed"; exit 1 ;;
esac

echo "== step 3: enroll device =="
req POST /api/v1/devices/enroll \
    -H "Authorization: Bearer $ASSERTION" \
    -H 'Content-Type: application/json' \
    -d "{\"siteCode\":\"$SITE_CODE\",\"deviceName\":\"smoke-device-1\",\"role\":\"operator\"}"
chk "enroll status" "$HTTP" 201
TOKEN=$(printf '%s' "$BODY" | jq -r '.deviceToken // empty')
DEV_SITE=$(printf '%s' "$BODY" | jq -r '.siteId // empty')
if [ -n "$TOKEN" ]; then
    ok "deviceToken issued $(datahub::mask "$TOKEN")"
else
    bad "no deviceToken: $BODY"
    exit 1
fi
chk "enrolled siteId matches provisioned" "$DEV_SITE" "$SITE_ID"
printf 'INFO  enroll body: %s\n' "$(printf '%s' "$BODY" | jq -c 'del(.deviceToken)')"
AUTH="Authorization: Bearer $TOKEN"

echo "== step 4: acquire leader lease =="
req POST "/api/v1/sites/$SITE_ID/lease/acquire" -H "$AUTH"
chk "lease acquire status" "$HTTP" 200
printf 'INFO  lease state: %s\n' "$BODY"
TERM=$(printf '%s' "$BODY" | jq -r '.leaderTerm // empty')
if [ -n "$TERM" ]; then ok "leaderTerm=$TERM"; else bad "no leaderTerm in $BODY"; fi

echo "== step 5: ingest (fenced) =="
IDEM="smoke-idem-$(date -u +%Y%m%d%H%M%S)"
# Scan times are generated, not hardcoded: waybill_scan_events is pruned 60 days
# after event_occurred_at (003_seed_retention.sql), so a frozen date would
# eventually place these rows outside the retention window.
# A naive "yyyy-MM-dd HH:mm:ss" value is Asia/Ho_Chi_Minh, so 10:00 stores as
# 03:00Z. ScanTimeParser never consults the clock, so any valid date works.
SCAN_DATE="$(date -u +%Y-%m-%d)"
ITEMS=$(cat <<JSON
{"items":[
 {"waybillNo":"SMOKE-WB-001","scanTime":"$SCAN_DATE 10:00:00","code":110,"status":"Arrived","scanTypeName":"state_transition","payload":{"src":"smoke"}},
 {"waybillNo":"SMOKE-WB-001","scanTime":"$SCAN_DATE 11:00:00","code":98,"status":"InStock","scanTypeName":"inventory","payload":{"src":"smoke"}}
]}
JSON
)
req POST "/api/v1/sites/$SITE_ID/jms/ingest" -H "$AUTH" -H 'Content-Type: application/json' \
    -H "Idempotency-Key: $IDEM" -H "X-Leader-Term: $TERM" -d "$ITEMS"
chk "ingest status" "$HTTP" 200
printf 'INFO  ingest: %s\n' "$BODY"
chk "acceptedItems" "$(printf '%s' "$BODY" | jq -r '.acceptedItems')" 2
chk "replayed" "$(printf '%s' "$BODY" | jq -r '.replayed')" false
FIRST=$(printf '%s' "$BODY" | jq -r '.firstChangeSeq')
LAST=$(printf '%s' "$BODY" | jq -r '.lastChangeSeq')
if [ "$FIRST" != "null" ]; then
    ok "firstChangeSeq=$FIRST lastChangeSeq=$LAST"
else
    bad "no change sequence emitted"
fi

echo "== step 6: replay same Idempotency-Key =="
req POST "/api/v1/sites/$SITE_ID/jms/ingest" -H "$AUTH" -H 'Content-Type: application/json' \
    -H "Idempotency-Key: $IDEM" -H "X-Leader-Term: $TERM" -d "$ITEMS"
chk "replay status" "$HTTP" 200
chk "replayed flag" "$(printf '%s' "$BODY" | jq -r '.replayed')" true

echo "== step 7: read changes =="
req GET "/api/v1/sites/$SITE_ID/changes?after=0&limit=100" -H "$AUTH"
chk "changes status" "$HTTP" 200
chk "change item count" "$(printf '%s' "$BODY" | jq -r '.items | length')" 1
printf 'INFO  change: %s\n' \
    "$(printf '%s' "$BODY" | jq -c '.items[0] | {changeSeq, entityType, entityKey, operation}')"

echo "== step 8: snapshot =="
req GET "/api/v1/sites/$SITE_ID/projections/snapshot" -H "$AUTH"
chk "snapshot status" "$HTTP" 200
chk "snapshot itemCount" "$(printf '%s' "$BODY" | jq -r '.itemCount')" 1
# snapshot_seq is the one deliberately snake_case field on the wire; the rest of
# the contract is camelCase. Guard it so a serializer change cannot silently
# rename it.
SNAPSHOT_SEQ=$(printf '%s' "$BODY" | jq -r '.snapshot_seq // "MISSING"')
if [ "$SNAPSHOT_SEQ" != "MISSING" ]; then
    ok "snake_case snapshot_seq present (=$SNAPSHOT_SEQ)"
else
    bad "snapshot_seq missing (wire-name contract)"
fi
printf 'INFO  projection: %s\n' \
    "$(printf '%s' "$BODY" | jq -c '.items[0] | {waybillNo, stateName, lastActivityAt, version}')"

echo "== step 9: negative cases =="
req POST "/api/v1/sites/$SITE_ID/jms/ingest" -H "$AUTH" -H 'Content-Type: application/json' \
    -H "Idempotency-Key: smoke-unknown-field" -H "X-Leader-Term: $TERM" \
    -d "{\"items\":[{\"waybillNo\":\"X\",\"scanTime\":\"$SCAN_DATE 10:00:00\",\"payload\":{},\"bogusField\":1}]}"
chk "unknown JSON member rejected" "$HTTP" 400
req GET "/api/v1/sites/$SITE_ID/changes?after=0" -H "Authorization: Bearer v1.bogus.bogus"
chk "forged device token rejected" "$HTTP" 401
req GET "/api/v1/sites/$SITE_ID/changes?after=0"
chk "missing token rejected" "$HTTP" 401
req POST "/api/v1/sites/$SITE_ID/jms/ingest" -H "$AUTH" -H 'Content-Type: application/json' \
    -H "Idempotency-Key: smoke-nofence" -d '{"items":[]}'
chk "ingest without X-Leader-Term fenced" "$HTTP" 409
req POST /api/v1/devices/enroll -H "Authorization: Bearer $ASSERTION" \
    -H 'Content-Type: application/json' \
    -d '{"siteCode":"NOTLICENSED","deviceName":"smoke-device-2","role":"operator"}'
chk "unlicensed site rejected" "$HTTP" 403

echo "== step 10: release lease =="
req POST "/api/v1/sites/$SITE_ID/lease/release" -H "$AUTH" -H 'Content-Type: application/json' \
    -d "{\"leaderTerm\":$TERM}"
chk "lease release status" "$HTTP" 200

echo
printf '===== SMOKE RESULT: %d passed, %d failed =====\n' "$PASS" "$FAIL"
[ "$FAIL" -eq 0 ]
