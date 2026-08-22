# shellcheck shell=bash
# Shared helpers for the DataHub bash scripts. Source this; do not execute it.
#
# These scripts are bash counterparts to the .ps1 scripts in this directory, for
# hosts where PowerShell 7 and the psql client are not installed (a plain Ubuntu
# VPS running only Docker). Behaviour mirrors the -ComposeFile mode of the .ps1
# scripts: psql always runs *inside* the postgres container.
#
# Nothing here may contain an IP, hostname, password or key: this repository is
# public. Every environment-specific value arrives at runtime via --env-file.

if [ -z "${BASH_VERSION-}" ]; then
    echo "_datahub-common.sh requires bash." >&2
    exit 1
fi
if [ "${BASH_SOURCE[0]}" = "${0}" ]; then
    echo "_datahub-common.sh is a library; source it from another script." >&2
    exit 1
fi

DATAHUB_SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# backend/datahub — the directory holding docker-compose.yml and migrations/.
DATAHUB_DIR="$(cd -- "$DATAHUB_SCRIPT_DIR/.." && pwd)"
DATAHUB_COMPOSE_FILE="${DATAHUB_COMPOSE_FILE:-$DATAHUB_DIR/docker-compose.yml}"
DATAHUB_ENV_FILE="${DATAHUB_ENV_FILE-}"
DATAHUB_POSTGRES_SERVICE="${DATAHUB_POSTGRES_SERVICE:-postgres}"

# psql invoked inside the container, reusing the credentials the container
# already has in its environment so no secret is ever passed on a command line
# (command lines are visible to every user via /proc).
DATAHUB_PSQL_EXEC='exec psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" "$@"'

datahub::die() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }

datahub::need() {
    command -v "$1" >/dev/null 2>&1 || datahub::die "$1 is required but was not found on PATH."
}

# Consumes the flags every DataHub script shares and leaves anything it does not
# recognise in DATAHUB_REST for the calling script to interpret.
datahub::parse_common_args() {
    DATAHUB_REST=()
    while [ "$#" -gt 0 ]; do
        case "$1" in
            --env-file)      DATAHUB_ENV_FILE="${2-}"; shift 2 ;;
            --env-file=*)    DATAHUB_ENV_FILE="${1#*=}"; shift ;;
            --compose-file)  DATAHUB_COMPOSE_FILE="${2-}"; shift 2 ;;
            --compose-file=*) DATAHUB_COMPOSE_FILE="${1#*=}"; shift ;;
            --service)       DATAHUB_POSTGRES_SERVICE="${2-}"; shift 2 ;;
            --service=*)     DATAHUB_POSTGRES_SERVICE="${1#*=}"; shift ;;
            --)              shift; [ "$#" -eq 0 ] || DATAHUB_REST+=("$@"); break ;;
            *)               DATAHUB_REST+=("$1"); shift ;;
        esac
    done
}

datahub::require_stack() {
    datahub::need docker
    [ -n "$DATAHUB_ENV_FILE" ] || datahub::die \
        "an env file is required: pass --env-file PATH or set DATAHUB_ENV_FILE. It holds the stack secrets, lives outside the repo and is never committed."
    [ -f "$DATAHUB_ENV_FILE" ] || datahub::die "env file not found: $DATAHUB_ENV_FILE"
    [ -f "$DATAHUB_COMPOSE_FILE" ] || datahub::die "compose file not found: $DATAHUB_COMPOSE_FILE"
}

datahub::compose() {
    docker compose --env-file "$DATAHUB_ENV_FILE" --file "$DATAHUB_COMPOSE_FILE" "$@"
}

# --- psql wrappers -----------------------------------------------------------
# Three wrappers exist because how the SQL reaches psql changes what psql does
# with it. Picking the wrong one produces silent, confusing failures.

# SQL supplied through CLI arguments (--command). stdin is explicitly closed:
# `docker compose exec -T` DRAINS STDIN, so a call in the middle of a piped
# script swallows the remainder of that script without any error.
datahub::psql() {
    datahub::compose exec -T "$DATAHUB_POSTGRES_SERVICE" \
        sh -ec "$DATAHUB_PSQL_EXEC" sh "$@" </dev/null
}

# SQL read from the caller's stdin. Required whenever the SQL uses :'var'
# placeholders: psql expands :'var' only for SQL read from stdin or -f. A string
# passed to --command/-c is forwarded to the server verbatim, so the colon
# reaches Postgres and fails with `syntax error at or near ":"`.
datahub::psql_stdin() {
    datahub::compose exec -T "$DATAHUB_POSTGRES_SERVICE" \
        sh -ec "$DATAHUB_PSQL_EXEC" sh "$@"
}

# SQL read from a file on the host, fed to the container on stdin.
datahub::psql_file() {
    local file="$1"; shift
    [ -f "$file" ] || datahub::die "SQL file not found: $file"
    datahub::compose exec -T "$DATAHUB_POSTGRES_SERVICE" \
        sh -ec "$DATAHUB_PSQL_EXEC" sh "$@" < "$file"
}

# --- secret handling ---------------------------------------------------------

# Reads one key from the env file. Prints only that value, never the whole file.
datahub::env_value() {
    sed -n "s/^$1=//p" "$DATAHUB_ENV_FILE" | head -n 1
}

# Renders a token as first4...last4 so logs can prove a token was issued without
# disclosing it. Anything short enough to be guessable is fully redacted.
datahub::mask() {
    local value="${1-}"
    if [ "${#value}" -le 12 ]; then
        printf '<redacted>'
    else
        printf '%s...%s (%d chars)' "${value:0:4}" "${value: -4}" "${#value}"
    fi
}
