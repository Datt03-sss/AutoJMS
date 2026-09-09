#!/usr/bin/env bash
# apply-migrations.sh — bash counterpart to apply-migrations.ps1 (-ComposeFile mode).
#
#   ./apply-migrations.sh --env-file /path/to/.env.staging
#   ./apply-migrations.sh --env-file /path/to/.env.staging --migration-dir /other/migrations
#
# Migrations are forward-only and each one records its own version marker inside
# its transaction. This script applies every not-yet-applied file with
# --single-transaction + ON_ERROR_STOP=1 and then re-reads the marker, so a
# migration that succeeds without recording itself is treated as a failure
# rather than silently re-running on the next deploy.
#
# A file named *_notx.sql, or containing a `-- no-transaction` line, is applied
# WITHOUT --single-transaction so it can run CREATE INDEX CONCURRENTLY. Such a
# file is not atomic and must be re-runnable as-is; see the long note above
# Test-NoTransactionMigration in apply-migrations.ps1 for the full trade-off,
# including the INVALID-index trap that IF NOT EXISTS does not cover.
#
# Only the container mode is implemented. Use apply-migrations.ps1 -DatabaseUrl
# when a psql client is installed on the host; adding an untested host-psql path
# here would be worse than not having it.
set -euo pipefail

. "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/_datahub-common.sh"

MIGRATION_DIR=""
datahub::parse_common_args "$@"
set -- ${DATAHUB_REST+"${DATAHUB_REST[@]}"}
while [ "$#" -gt 0 ]; do
    case "$1" in
        --migration-dir)   MIGRATION_DIR="${2-}"; shift 2 ;;
        --migration-dir=*) MIGRATION_DIR="${1#*=}"; shift ;;
        *) datahub::die "unexpected argument: $1" ;;
    esac
done

datahub::require_stack
MIGRATION_DIR="${MIGRATION_DIR:-$DATAHUB_DIR/migrations}"
[ -d "$MIGRATION_DIR" ] || datahub::die "migration directory does not exist: $MIGRATION_DIR"

datahub::psql --set ON_ERROR_STOP=1 --command "CREATE TABLE IF NOT EXISTS schema_migrations (
    version text PRIMARY KEY,
    applied_at timestamptz NOT NULL DEFAULT now()
);" >/dev/null

# Passing the version as a psql --variable instead of splicing it into the SQL
# text means a filename can never alter the query. The SQL therefore has to come
# in on stdin, which is the only form where psql expands :'version'.
migration_recorded() {
    printf '%s\n' "SELECT 1 FROM schema_migrations WHERE version = :'version';" \
        | datahub::psql_stdin --tuples-only --no-align --set ON_ERROR_STOP=1 \
              --variable "version=$1" \
        | tr -d '[:space:]'
}

# Mirrors Test-NoTransactionMigration in apply-migrations.ps1 — the two runners
# must agree, or the same file becomes atomic or not depending on which host ran
# the deploy. grep's non-match exit status is the function's result on purpose.
migration_is_no_transaction() {
    case "$1" in
        *_notx) return 0 ;;
    esac
    grep -Eq '^[[:space:]]*--[[:space:]]*no-transaction([[:space:]]|$)' "$2"
}

applied_count=0
skipped_count=0
for file in $(ls -1 "$MIGRATION_DIR"/*.sql | sort); do
    version="$(basename "$file" .sql)"
    # Mirrors the ^\d+_.+\.sql$ filter in apply-migrations.ps1.
    case "$version" in
        [0-9]*_*) ;;
        *) printf 'IGNORE %s (does not match NNN_name.sql)\n' "$version"; continue ;;
    esac

    if [ "$(migration_recorded "$version")" = "1" ]; then
        printf 'SKIP  %s\n' "$version"
        skipped_count=$((skipped_count + 1))
        continue
    fi

    if migration_is_no_transaction "$version" "$file"; then
        printf 'APPLY %s (NO TRANSACTION)\n' "$version"
        printf '      not atomic: a mid-file failure leaves earlier statements applied\n'
        printf '      and the marker unwritten, so this file must be re-runnable as-is.\n'
        datahub::psql_file "$file" --set ON_ERROR_STOP=1 >/dev/null
    else
        printf 'APPLY %s\n' "$version"
        datahub::psql_file "$file" --set ON_ERROR_STOP=1 --single-transaction >/dev/null
    fi

    if [ "$(migration_recorded "$version")" != "1" ]; then
        datahub::die "migration $version completed without recording its version marker."
    fi
    applied_count=$((applied_count + 1))
done

# CONCURRENTLY's blind spot, and the reason contract 19.2 exists. A cancelled or
# failed CREATE INDEX CONCURRENTLY leaves an INVALID index behind. The file's own
# IF NOT EXISTS then finds that name on the next run, skips it, and lets the
# version marker record -- so the loop above reports success over an index the
# planner will never use, and readiness cannot tell either: it counts tables and
# migration rows, both of which are present. Every statement genuinely succeeded,
# so no per-file check can see this. Ask the catalogue instead.
#
# Scoped to public and run on every invocation, including an all-SKIP one: that is
# what turns it into a detector for damage an earlier run left behind. An index
# being built CONCURRENTLY by another session right now also reads invalid here,
# which is the correct answer during a migration run -- nothing else should be
# building indexes on this database while this script holds the deploy.
invalid_indexes="$(printf '%s\n' "SELECT string_agg(c.relname, ',' ORDER BY c.relname)
      FROM pg_index i
      JOIN pg_class c ON c.oid = i.indexrelid
      JOIN pg_namespace n ON n.oid = c.relnamespace
     WHERE n.nspname = 'public' AND NOT i.indisvalid;" \
    | datahub::psql_stdin --tuples-only --no-align --set ON_ERROR_STOP=1 \
    | tr -d '[:space:]')"
if [ -n "$invalid_indexes" ]; then
    datahub::die "invalid index after migration: $invalid_indexes
  A CREATE INDEX CONCURRENTLY did not finish. Per contract 19.2: DROP INDEX
  CONCURRENTLY the name above, re-run this script once, and stop and report if it
  fails again. The run above did NOT leave a usable schema."
fi

printf 'DataHub migrations complete (%d applied, %d already present, no invalid indexes).\n' \
    "$applied_count" "$skipped_count"
