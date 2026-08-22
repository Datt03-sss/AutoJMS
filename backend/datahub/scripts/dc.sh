#!/usr/bin/env bash
# dc.sh — docker compose for the DataHub stack, with the env file and compose
# file already wired up.
#
#   ./dc.sh --env-file /path/to/.env.staging ps
#   ./dc.sh --env-file /path/to/.env.staging logs --tail 50 api
#   DATAHUB_ENV_FILE=/path/to/.env.staging ./dc.sh restart api
#
# The compose file deliberately has no env_file: key, so --env-file on the CLI is
# the only thing that wires the stack's configuration. Forgetting it makes every
# ${VAR:?} in docker-compose.yml fail; this wrapper exists so that cannot happen.
set -euo pipefail

. "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/_datahub-common.sh"

datahub::parse_common_args "$@"
datahub::require_stack

exec docker compose \
    --env-file "$DATAHUB_ENV_FILE" \
    --file "$DATAHUB_COMPOSE_FILE" \
    ${DATAHUB_REST+"${DATAHUB_REST[@]}"}
