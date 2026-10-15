#!/usr/bin/env bash
# Pull the latest published Hugo images and recreate the stack. Non-destructive: named volumes are preserved.
# When HUGO_DB_TYPE is Postgresql (in the environment or docker/.env), the postgres profile is included so the
# PostgreSQL container is pulled and recreated with the rest of the stack.
# For a destructive reset use factory/reset.sh.
set -euo pipefail
cd "$(dirname "$0")"
db_type="${HUGO_DB_TYPE:-}"
if [ -z "${db_type}" ] && [ -f .env ]; then
  db_type="$(grep -E '^HUGO_DB_TYPE=' .env | tail -n 1 | cut -d= -f2- | tr -d "\"' \r")"
fi
profile_args=""
case "$(printf '%s' "${db_type}" | tr '[:upper:]' '[:lower:]')" in
  postgresql|postgres) profile_args="--profile postgres" ;;
esac
docker compose ${profile_args} pull
docker compose ${profile_args} down
docker compose ${profile_args} up -d
docker ps -a
