#!/usr/bin/env bash
# cleanup-empty-sites.sh — empty-site cleanup, part 2 of 2: the operator's half.
#
#   ./cleanup-empty-sites.sh --env-file /path/to/.env.staging
#   ./cleanup-empty-sites.sh --env-file /path/to/.env.staging --older-than '7 days'
#   ./cleanup-empty-sites.sh --env-file /path/to/.env.staging --older-than '7 days' --delete
#
# Enrollment provisions its own site now, so a licence key with a mistyped middle
# code creates one instead of failing with 404. This lists the sites that came out
# of that — sites holding no scan events, no projections, no change feed and no
# idempotency records, whose change counter has never moved.
#
# READ-ONLY unless --delete is passed. That is the whole point: the retention
# worker can sweep these on a policy (see 011_vacant_sites.sql), but nobody should
# install that policy without first seeing what the interval would take.
#
# --older-than defaults to the interval of the installed `sites` retention policy,
# so with no arguments this prints exactly what the worker would remove. With no
# policy installed it previews 30 days and says so.
#
# Both this script and the worker read the `vacant_sites` view and apply the same
# age test, so the dry run and the sweep cannot disagree about what is empty.
#
# --delete writes a backup first, one JSON object per row, to a 0600 file under
# --backup-dir. devices.credential_hash is redacted from it: it is the digest a
# device authenticates with, and restoring a site is followed by re-enrollment
# anyway, so the backup has no reason to carry it.
set -euo pipefail

. "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/_datahub-common.sh"

OLDER_THAN=""
DO_DELETE=0
BACKUP_DIR="${DATAHUB_BACKUP_DIR:-$HOME/datahub-backups}"

datahub::parse_common_args "$@"
set -- ${DATAHUB_REST+"${DATAHUB_REST[@]}"}
while [ "$#" -gt 0 ]; do
    case "$1" in
        --older-than)    OLDER_THAN="${2-}"; shift 2 ;;
        --older-than=*)  OLDER_THAN="${1#*=}"; shift ;;
        --backup-dir)    BACKUP_DIR="${2-}"; shift 2 ;;
        --backup-dir=*)  BACKUP_DIR="${1#*=}"; shift ;;
        --delete)        DO_DELETE=1; shift ;;
        -h|--help)       sed -n '2,26p' "${BASH_SOURCE[0]}"; exit 0 ;;
        *) datahub::die "unexpected argument: $1" ;;
    esac
done

datahub::require_stack

# An interval PostgreSQL cannot parse must fail here, not halfway through the
# delete transaction. A rejected value also cannot smuggle SQL into the queries
# below, though :'older_than' already quotes it as a literal.
if [ -z "$OLDER_THAN" ]; then
    installed="$(printf '%s\n' "SELECT delete_after FROM retention_policies
         WHERE site_id IS NULL AND table_name = 'sites' AND delete_after IS NOT NULL;" \
        | datahub::psql_stdin --tuples-only --no-align --set ON_ERROR_STOP=1 | tr -d '\r')"
    if [ -n "$installed" ]; then
        OLDER_THAN="$installed"
        printf 'Previewing the installed sites retention policy: %s\n\n' "$OLDER_THAN"
    else
        OLDER_THAN="30 days"
        printf 'No sites retention policy is installed. Previewing %s; the worker is sweeping nothing.\n\n' "$OLDER_THAN"
    fi
fi
printf '%s\n' "SELECT :'older_than'::interval;" \
    | datahub::psql_stdin --tuples-only --no-align --set ON_ERROR_STOP=1 \
          --variable "older_than=$OLDER_THAN" >/dev/null \
    || datahub::die "--older-than is not a PostgreSQL interval: $OLDER_THAN"

# The age half of the predicate, kept identical to VacantSiteCandidateSql in
# RetentionRepository.cs. The view supplies the data half.
AGE_FILTER="v.created_at < now() - :'older_than'::interval
       AND (v.last_device_seen_at IS NULL OR v.last_device_seen_at < now() - :'older_than'::interval)"

printf 'Sites holding no data, untouched for %s:\n' "$OLDER_THAN"
datahub::psql_stdin --set ON_ERROR_STOP=1 --variable "older_than=$OLDER_THAN" <<SQL
SELECT v.site_code,
       v.site_id,
       v.created_at,
       coalesce(v.last_device_seen_at::text, 'never') AS last_device_seen_at,
       v.device_count,
       v.audit_log_count
  FROM vacant_sites v
 WHERE $AGE_FILTER
 ORDER BY v.created_at;
SQL

ids="$(printf '%s\n' "SELECT string_agg(v.site_id::text, ',')
      FROM vacant_sites v
     WHERE $AGE_FILTER;" \
    | datahub::psql_stdin --tuples-only --no-align --set ON_ERROR_STOP=1 \
          --variable "older_than=$OLDER_THAN" | tr -d '[:space:]')"

if [ -z "$ids" ]; then
    printf '\nNothing to clean up.\n'
    exit 0
fi
# Fields, not lines: $ids has no trailing newline, so `wc -l` reports one fewer
# than there are ids and reads "0 site(s)" for a single candidate.
candidate_count="$(printf '%s' "$ids" | awk -F',' '{print NF}')"

if [ "$DO_DELETE" -ne 1 ]; then
    printf '\n%s site(s) would be removed. This was a dry run; pass --delete to remove them.\n' "$candidate_count"
    exit 0
fi

umask 077
mkdir -p "$BACKUP_DIR"
backup_file="$BACKUP_DIR/vacant-sites-$(date -u +%Y%m%dT%H%M%SZ).pre-delete.jsonl"
datahub::psql_stdin --tuples-only --no-align --set ON_ERROR_STOP=1 --variable "ids=$ids" <<'SQL' > "$backup_file"
WITH victims AS (SELECT unnest(string_to_array(:'ids', ','))::uuid AS site_id)
SELECT 'sites ' || to_jsonb(s)::text FROM sites s JOIN victims v ON v.site_id = s.id
UNION ALL
SELECT 'devices ' || (to_jsonb(d) - 'credential_hash')::text FROM devices d JOIN victims v ON v.site_id = d.site_id
UNION ALL
SELECT 'site_fetch_leases ' || to_jsonb(l)::text FROM site_fetch_leases l JOIN victims v ON v.site_id = l.site_id
UNION ALL
SELECT 'site_change_counters ' || to_jsonb(c)::text FROM site_change_counters c JOIN victims v ON v.site_id = c.site_id
UNION ALL
SELECT 'audit_logs ' || to_jsonb(a)::text FROM audit_logs a JOIN victims v ON v.site_id = a.site_id;
SQL
chmod 600 "$backup_file"
printf '\nBackup written: %s (%s rows)\n' "$backup_file" "$(wc -l < "$backup_file" | tr -d '[:space:]')"

# Locks before the delete, and vacancy re-read under them — the same sequence
# DeleteVacantSitesAsync uses, for the same reason. Enrollment locks the site row
# before inserting a device and ingest locks the counter before writing anything,
# so a site that came alive between the listing above and this transaction drops
# out of cleanup_victims instead of losing a device a station already holds a
# token for. The locks land via CREATE TABLE AS so they print no rows.
deleted="$(datahub::psql_stdin --tuples-only --no-align --set ON_ERROR_STOP=1 \
    --variable "ids=$ids" --variable "older_than=$OLDER_THAN" <<SQL | tr -d '[:space:]'
BEGIN;
CREATE TEMP TABLE cleanup_candidates ON COMMIT DROP AS
    SELECT unnest(string_to_array(:'ids', ','))::uuid AS site_id;
CREATE TEMP TABLE cleanup_locked_sites ON COMMIT DROP AS
    SELECT s.id FROM sites s WHERE s.id IN (SELECT site_id FROM cleanup_candidates)
     ORDER BY s.id FOR UPDATE;
CREATE TEMP TABLE cleanup_locked_counters ON COMMIT DROP AS
    SELECT c.site_id FROM site_change_counters c WHERE c.site_id IN (SELECT site_id FROM cleanup_candidates)
     ORDER BY c.site_id FOR UPDATE;
CREATE TEMP TABLE cleanup_victims ON COMMIT DROP AS
    SELECT v.site_id, v.site_code FROM vacant_sites v
     WHERE $AGE_FILTER
       AND v.site_id IN (SELECT site_id FROM cleanup_candidates);
-- Child rows first, in the one order the foreign keys accept: every reference to
-- sites is NO ACTION, and site_fetch_leases.leader_device_id is RESTRICT, which is
-- checked the moment the device row goes rather than at end of statement.
DELETE FROM site_fetch_leases    WHERE site_id IN (SELECT site_id FROM cleanup_victims);
DELETE FROM site_change_counters WHERE site_id IN (SELECT site_id FROM cleanup_victims);
DELETE FROM audit_logs           WHERE site_id IN (SELECT site_id FROM cleanup_victims);
DELETE FROM devices              WHERE site_id IN (SELECT site_id FROM cleanup_victims);
-- The trace outlives the site, so it carries a null site_id.
INSERT INTO audit_logs (site_id, actor, action, payload)
SELECT NULL, 'datahub-operator', 'site.vacant_delete',
       jsonb_build_object('siteId', v.site_id, 'siteCode', v.site_code)
  FROM cleanup_victims v;
DELETE FROM sites WHERE id IN (SELECT site_id FROM cleanup_victims);
-- Tagged, because --tuples-only silences column headers but not the command
-- status psql prints per statement: BEGIN, DELETE 1, COMMIT and the rest all
-- land on the same stdout this count is read from.
SELECT 'removed=' || count(*) FROM cleanup_victims;
COMMIT;
SQL
)"
deleted="$(printf '%s' "$deleted" | sed -n 's/.*removed=\([0-9]*\).*/\1/p')"
[ -n "$deleted" ] || datahub::die "the delete transaction reported no count; check the rows above before re-running."

printf 'Removed %s of %s candidate site(s).\n' "$deleted" "$candidate_count"
if [ "$deleted" != "$candidate_count" ]; then
    printf 'The rest stopped being empty between the listing and the delete, and were left alone.\n'
fi
