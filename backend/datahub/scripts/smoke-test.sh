#!/usr/bin/env bash
# smoke-test.sh — end-to-end staging smoke against a running DataHub stack.
#
#   ./smoke-test.sh --env-file /path/to/.env.staging
#   ./smoke-test.sh --env-file /path/to/.env.staging --base https://datahub.example.com
#
# Ten steps: provision a site, obtain a license assertion, enroll a device,
# acquire the leader lease, ingest behind the fence, replay the same
# Idempotency-Key, read the change feed, read the snapshot, run five negative
# cases, release the lease. Exits non-zero if any check fails.
#
# Only ever run this against staging: it writes real rows.
#
# THREE WAYS TO OBTAIN THE ASSERTION, tried in this order. The API registers
# exactly ONE validator -- IdentityServiceCollectionExtensions.cs:14-25 is an
# if/else-if -- so the mode has to match how the stack is configured:
#
#   1. DATAHUB_SMOKE_TEST_ASSERTION=<token>
#      Used verbatim. Works against any validator because nothing is signed here.
#      The site code is then READ OUT of the token instead of generated; see the
#      step 0 comment for why it cannot be the other way round.
#
#   2. DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY, or
#      DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY_FILE=<path>
#      Mints v1rs256.<payload>.<signature> with the RSA private half. This is the
#      mode to use once the stack runs DATAHUB_ALLOW_STAGING_TEST_ISSUER=false,
#      because RsaLicenseAssertionValidator rejects every other version prefix
#      (RsaLicenseAssertionValidator.cs:53). The key may come from the process
#      environment, from --env-file, or from a file path; it is never written to
#      disk and never appears on a command line. Adds no tool requirement -- see
#      the signer in step 2 for why it does its own PKCS#1 padding.
#
#   3. otherwise: mints v1.<payload>.<signature> with
#      DATAHUB_STAGING_TEST_SIGNING_KEY. Only works while the stack still has
#      DATAHUB_ALLOW_STAGING_TEST_ISSUER=true -- which production must never have
#      enabled, and which switches RsaLicenseAssertionValidator OFF. Never turn
#      that flag back on just to make this script green; use mode 1 or 2 instead.
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

# An env file holds a PEM on ONE line, quoted, with two-character \n sequences --
# the exact shape server.js formatKey() undoes (server.js:58-61). Undo the same two
# things here. printf %b is safe on a PEM: the body is base64 plus dashes, so it
# contains no backslash of its own for %b to reinterpret.
unwrap_pem() {
    local raw="${1-}"
    raw="${raw#\"}"
    raw="${raw%\"}"
    printf '%b\n' "$raw"
}

echo "== step 0: choose assertion mode =="
# This has to be settled BEFORE the site is provisioned, because in mode 1 the site
# code is dictated by the token rather than generated: EnrollmentEndpoints.cs:87
# requires the requested siteCode to be inside the SIGNED SiteCodes set, so
# enrolling a freshly generated code against a pre-supplied assertion returns 403
# SITE_NOT_LICENSED every single time. The site code cannot be chosen first and the
# assertion fitted to it afterwards -- that is what the signature prevents.
RSA_KEY=""
ENV_RSA_KEY="$(datahub::env_value DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY)"
if [ -n "${DATAHUB_SMOKE_TEST_ASSERTION-}" ]; then
    ASSERTION_MODE=presupplied
elif [ -n "${DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY-}" ]; then
    ASSERTION_MODE=rs256
    RSA_KEY="$(unwrap_pem "$DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY")"
elif [ -n "${DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY_FILE-}" ]; then
    ASSERTION_MODE=rs256
    [ -r "$DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY_FILE" ] || datahub::die \
        "DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY_FILE is not readable: $DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY_FILE"
    RSA_KEY="$(cat "$DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY_FILE")"
elif [ -n "$ENV_RSA_KEY" ]; then
    ASSERTION_MODE=rs256
    RSA_KEY="$(unwrap_pem "$ENV_RSA_KEY")"
else
    ASSERTION_MODE=hmac
fi
unset ENV_RSA_KEY
printf 'INFO  assertion mode: %s\n' "$ASSERTION_MODE"
if [ "$ASSERTION_MODE" = rs256 ]; then
    # Fail here, with the reason, rather than emit a signature the API will reject.
    # The commonest mistake is handing over the PUBLIC half, which is the only half
    # the API is supposed to have.
    case "$RSA_KEY" in
        *"-----BEGIN "*"PRIVATE KEY-----"*) ;;
        *) datahub::die "the RSA key did not resolve to a PEM PRIVATE key block; the public half cannot sign." ;;
    esac
fi

if [ "$ASSERTION_MODE" = presupplied ]; then
    ASSERTION="$DATAHUB_SMOKE_TEST_ASSERTION"
    # The payload is base64url JSON, readable with no key at all, so the site code
    # the token was signed for is recovered here instead of guessed. Only the
    # signature needs a key; reading the claims does not.
    SITE_CODE=$(ASSERTION="$ASSERTION" python3 - <<'PY'
import base64, json, os, sys

parts = os.environ["ASSERTION"].split('.')
if len(parts) != 3:
    sys.exit("not in <prefix>.<payload>.<signature> form")
raw = parts[1]
try:
    payload = json.loads(base64.urlsafe_b64decode(raw + '=' * (-len(raw) % 4)))
except Exception as exc:
    sys.exit(f"payload is not base64url JSON: {exc}")
# Normalised the same way the API normalises it (LicenseAssertionPayload.cs:42-48,
# EnrollmentEndpoints.cs:85), or the comparison there would miss.
codes = [str(c).strip().upper() for c in (payload.get("SiteCodes") or []) if str(c).strip()]
if not codes:
    sys.exit("the payload carries no SiteCodes, so no site can be enrolled")
print(
    "INFO  supplied claims: prefix={} channel={} issuer={} audience={} expiresAt={} siteCodes={}".format(
        parts[0], payload.get("Channel"), payload.get("Issuer"),
        payload.get("Audience"), payload.get("ExpiresAt"), codes),
    file=sys.stderr)
print(codes[0])
PY
) || datahub::die "cannot read DATAHUB_SMOKE_TEST_ASSERTION (reason above)"
    printf 'INFO  site code taken from the supplied assertion: %s\n' "$SITE_CODE"
else
    # create_datahub_site is not idempotent (sites_site_code_key), and steps 7-8
    # assert exact counts, so every minted run gets its own site rather than reusing
    # one. Only the minting modes can do this, because the code has to be inside the
    # signature and here the script is the signer.
    SITE_CODE="SMK$(date -u +%y%m%d%H%M%S)"
fi

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
elif [ "$ASSERTION_MODE" = presupplied ]; then
    # A signed site code cannot be regenerated per run, so on the second run against
    # the same token the INSERT hits sites_site_code_key. Adopt the existing row's
    # id, because step 3 compares the enrolled siteId against SITE_ID.
    SITE_ID=$(printf '%s\n' "SELECT id FROM sites WHERE site_code = upper(btrim(:'site_code'));" \
        | datahub::psql_stdin --set ON_ERROR_STOP=1 --tuples-only --no-align \
              --variable "site_code=$SITE_CODE" 2>/dev/null | tr -d '[:space:]')
    case "$SITE_ID" in
        ????????-????-????-????-????????????)
            ok "reusing existing site $SITE_CODE ($SITE_ID)"
            printf 'WARN  steps 7-8 assert EXACT counts (1 change row, 1 projection item).\n'
            printf 'WARN  A site that already holds smoke rows will fail those two checks;\n'
            printf 'WARN  that is stale data, not a broken contract.\n'
            ;;
        *)  bad "create_datahub_site, and no existing site named $SITE_CODE"; exit 1 ;;
    esac
else
    bad "create_datahub_site"
    exit 1
fi

echo "== step 2: obtain license assertion ($ASSERTION_MODE) =="
if [ "$ASSERTION_MODE" = presupplied ]; then
    ok "using pre-supplied assertion $(datahub::mask "$ASSERTION")"
else
    EXP=$(( $(date -u +%s) + 8 * 3600 ))
    # Channel comes from the env file, not a literal: LicenseAssertionPayload.cs:49-50
    # compares it ordinally against DATAHUB_CHANNEL on the API side, so a hardcoded
    # "staging" fails CHANNEL_MISMATCH the moment the stack runs another channel.
    CHANNEL="$(datahub::env_value DATAHUB_CHANNEL)"
    [ -n "$CHANNEL" ] || CHANNEL=staging
    # One payload definition for both algorithms, so the two modes can never drift
    # apart in a field the API compares. Every secret is handed over as an
    # environment variable, never as an argument, because command lines are
    # world-readable through /proc; nothing is written to disk.
    ASSERTION=$(MODE="$ASSERTION_MODE" SITE_CODE="$SITE_CODE" EXP="$EXP" CHANNEL="$CHANNEL" \
        ISS="$(datahub::env_value DATAHUB_LICENSE_ASSERTION_ISSUER)" \
        AUD="$(datahub::env_value DATAHUB_LICENSE_ASSERTION_AUDIENCE)" \
        RSA_KEY="$RSA_KEY" \
        HMAC_KEY="$(datahub::env_value DATAHUB_STAGING_TEST_SIGNING_KEY)" python3 - <<'PY'
import base64, hashlib, hmac, json, os, sys


def b64u(raw):
    return base64.urlsafe_b64encode(raw).decode().rstrip('=')


# --- the smallest DER reader that can find n and d in an RSA private key -------
# RSASSA-PKCS1-v1_5 is done here rather than shelled out to `openssl dgst -sign`
# for two reasons. Handing openssl the key without touching the disk means a
# process substitution, and OpenSSL 3 loads a key through OSSL_STORE, which tries
# several loaders and may rewind the file -- something a pipe cannot do. And the
# alternative, a temp file, puts the private key on disk where a crash leaves it.
# python3 is already a hard requirement of this script; openssl is not.
def der_tlv(buf, i):
    tag = buf[i]; i += 1
    length = buf[i]; i += 1
    if length & 0x80:
        width = length & 0x7F
        length = int.from_bytes(buf[i:i + width], 'big'); i += width
    return tag, buf[i:i + length], i + length


def der_items(buf):
    out, i = [], 0
    while i < len(buf):
        tag, body, i = der_tlv(buf, i)
        out.append((tag, body))
    return out


def rsa_numbers(pem):
    body = ''.join(
        line.strip() for line in pem.strip().splitlines()
        if line.strip() and not line.startswith('-----'))
    try:
        _, seq, _ = der_tlv(base64.b64decode(body), 0)
        items = der_items(seq)
    except Exception as exc:
        sys.exit(f'cannot parse the RSA private key: {exc}')
    # PKCS#8 PrivateKeyInfo is INTEGER, SEQUENCE, OCTET STRING; the octet string
    # holds the PKCS#1 RSAPrivateKey. PKCS#1 on its own is INTEGER version then
    # INTEGER n, so the second item's tag is what tells the two apart.
    if len(items) >= 3 and items[1][0] == 0x30 and items[2][0] == 0x04:
        _, seq, _ = der_tlv(items[2][1], 0)
        items = der_items(seq)
    if len(items) < 4:
        sys.exit('that PEM is not an RSA private key (no privateExponent in it)')
    return int.from_bytes(items[1][1], 'big'), int.from_bytes(items[3][1], 'big')


def sign_pkcs1_sha256(message, n, d):
    # EMSA-PKCS1-v1_5, RFC 8017 section 9.2, with the fixed SHA-256 DigestInfo
    # prefix. This is what RSASignaturePadding.Pkcs1 + SHA256 verifies against in
    # RsaLicenseAssertionValidator.
    digest_info = bytes.fromhex('3031300d060960864801650304020105000420')
    digest_info += hashlib.sha256(message).digest()
    k = (n.bit_length() + 7) // 8
    em = b'\x00\x01' + b'\xff' * (k - len(digest_info) - 3) + b'\x00' + digest_info
    return pow(int.from_bytes(em, 'big'), d, n).to_bytes(k, 'big')


# The payload is compact PascalCase JSON: the API deserializes it with the
# default, case-SENSITIVE options, so camelCase keys would not bind.
payload = {
    "Channel": os.environ["CHANNEL"],
    "SiteCodes": [os.environ["SITE_CODE"]],
    "ExpiresAt": int(os.environ["EXP"]),
    "DataHubUrl": None,
    "Seats": 1,
    "TokenVersion": 1,
    "Issuer": os.environ["ISS"],
    "Audience": os.environ["AUD"],
}
# Both algorithms sign the UTF-8 bytes of the *encoded* payload string.
encoded = b64u(json.dumps(payload, separators=(',', ':')).encode())

if os.environ["MODE"] == "rs256":
    modulus, exponent = rsa_numbers(os.environ["RSA_KEY"])
    if modulus.bit_length() < 2048:
        sys.exit(f'the RSA key is {modulus.bit_length()} bits; the API requires 2048 '
                 '(RsaLicenseAssertionValidator.MinimumKeySizeBits)')
    signature = sign_pkcs1_sha256(encoded.encode(), modulus, exponent)
    print(f"v1rs256.{encoded}.{b64u(signature)}")
else:
    if not os.environ["HMAC_KEY"]:
        sys.exit('DATAHUB_STAGING_TEST_SIGNING_KEY is not in the env file, and no RSA '
                 'private key was supplied either, so nothing can sign the assertion')
    signature = hmac.new(
        os.environ["HMAC_KEY"].encode(), encoded.encode(), hashlib.sha256).digest()
    print(f"v1.{encoded}.{b64u(signature)}")
PY
)
    if [ -z "$ASSERTION" ]; then bad "assertion mint failed ($ASSERTION_MODE)"; exit 1; fi
    ok "assertion minted $(datahub::mask "$ASSERTION")"
fi
# A wrong prefix is not a transport error, it is an instant 401 with
# LICENSE_ASSERTION_MALFORMED (RsaLicenseAssertionValidator.cs:53), so name it here
# rather than let step 3 report an opaque status. No ok() on the happy path: this is
# a guard, not one of the contracts the suite is counting.
case "$ASSERTION" in
    v1.*.*|v1rs256.*.*) : ;;
    *) bad "assertion has an unusable version prefix; the API accepts only v1. (staging issuer) or v1rs256. (RSA)"; exit 1 ;;
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
