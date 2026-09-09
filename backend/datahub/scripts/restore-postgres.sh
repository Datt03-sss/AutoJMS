#!/usr/bin/env bash
# restore-postgres.sh - restore a DataHub dump into the postgres container.
#
#   ./restore-postgres.sh --env-file /opt/autojms-datahub/.env.staging --dump-file /srv/datahub-backups/datahub-<stamp>.dump
#   ./restore-postgres.sh --env-file /opt/autojms-datahub/.env.staging --dump-file <dump> --allow-existing-data
#
# The bash counterpart to restore-postgres.ps1, for a plain Ubuntu VPS that runs
# Docker and nothing else. pg_restore runs INSIDE the container, reusing the
# credentials it already holds, so no password reaches a command line. Only the
# dump file crosses the boundary. Host mode (the .ps1 script's -DatabaseUrl) is
# deliberately absent: this host has no pg_restore.
#
# Two safety properties are carried over from the .ps1 script deliberately:
#
#   --single-transaction   a restore that fails halfway leaves NOTHING behind,
#                          rather than a database that is half old and half new
#                          and looks superficially fine.
#   no --clean by default  the script refuses to drop objects unless
#                          --allow-existing-data is passed. Prefer an empty
#                          isolated database for drills; never aim a first
#                          restore at a live production database.
#
# Restoring a --critical-only dump additionally requires, once this finishes:
#
#     UPDATE site_change_counters SET pruned_through_seq = change_seq;
#
# See scripts/critical-backup-exclusions.txt for why. It is not run here because
# a dump does not record which flags produced it, and running it after a FULL
# restore would discard a change feed that is present and valid.
set -euo pipefail

. "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/_datahub-common.sh"

DUMP_FILE=""
ALLOW_EXISTING=0

datahub::parse_common_args "$@"
set -- ${DATAHUB_REST+"${DATAHUB_REST[@]}"}
while [ "$#" -gt 0 ]; do
    case "$1" in
        --dump-file)   DUMP_FILE="${2-}"; shift 2 ;;
        --dump-file=*) DUMP_FILE="${1#*=}"; shift ;;
        --allow-existing-data) ALLOW_EXISTING=1; shift ;;
        *) datahub::die "unexpected argument: $1" ;;
    esac
done

datahub::require_stack
[ -n "$DUMP_FILE" ] || datahub::die "a dump file is required: pass --dump-file PATH."
[ -f "$DUMP_FILE" ] || datahub::die "dump file does not exist: $DUMP_FILE"

CONTAINER_DUMP="/tmp/datahub-restore-$$-${RANDOM}.dump"

# A copy of the production dump left inside the container is both a disk leak and
# a copy of the data sitting somewhere nobody is watching, so remove it on every
# exit path.
cleanup() {
    datahub::compose exec -T "$DATAHUB_POSTGRES_SERVICE" \
        rm -f -- "$CONTAINER_DUMP" </dev/null >/dev/null 2>&1 || true
}
trap cleanup EXIT

RESTORE_CMD='pg_restore --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --format=custom --exit-on-error --single-transaction --no-owner --no-privileges'
if [ "$ALLOW_EXISTING" -eq 1 ]; then
    RESTORE_CMD="$RESTORE_CMD --clean --if-exists"
fi
RESTORE_CMD="$RESTORE_CMD \"\$1\""

printf 'DataHub restore\n'
printf '  service    %s\n' "$DATAHUB_POSTGRES_SERVICE"
printf '  dump       %s (%s bytes)\n' "$DUMP_FILE" "$(wc -c < "$DUMP_FILE" | tr -d '[:space:]')"
if [ "$ALLOW_EXISTING" -eq 1 ]; then
    printf '  mode       --clean --if-exists (existing objects will be dropped)\n'
else
    printf '  mode       into an empty database\n'
fi

datahub::compose cp "$DUMP_FILE" "$DATAHUB_POSTGRES_SERVICE:$CONTAINER_DUMP" \
    || datahub::die "docker compose cp failed; nothing was restored."

datahub::compose exec -T "$DATAHUB_POSTGRES_SERVICE" sh -ec "exec $RESTORE_CMD" sh "$CONTAINER_DUMP" </dev/null \
    || datahub::die "container pg_restore failed; the single transaction rolled back and the database is unchanged."

printf 'Restore completed. Run migrations and catalog assertions before serving traffic.\n'
