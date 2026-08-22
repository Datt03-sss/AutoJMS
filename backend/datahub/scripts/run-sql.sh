#!/usr/bin/env bash
# run-sql.sh — run a .sql file against the DataHub database inside the container.
#
#   ./run-sql.sh --env-file /path/to/.env.staging ../tests/001_core_catalog_assertions.sql
#   ./run-sql.sh --env-file /path/to/.env.staging file.sql --variable site_code=ABC
#
# Any argument after the .sql file is passed through to psql, so :'var'
# placeholders in the file can be filled with --variable name=value. That works
# here because the file is fed to psql on stdin: psql expands :'var' only for SQL
# read from stdin or -f, never for a string passed to --command/-c.
set -euo pipefail

. "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/_datahub-common.sh"

datahub::parse_common_args "$@"
datahub::require_stack

set -- ${DATAHUB_REST+"${DATAHUB_REST[@]}"}
[ "$#" -ge 1 ] || datahub::die "usage: run-sql.sh --env-file PATH <file.sql> [psql args...]"

sql_file="$1"; shift
datahub::psql_file "$sql_file" --set ON_ERROR_STOP=1 "$@"
