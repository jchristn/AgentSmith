#!/usr/bin/env bash
# Create docker/.env from .env.example on first run, generate missing keys, and start the stack.
# Extra arguments are passed to "docker compose up" (for example --build, or put --profile before up yourself).
set -euo pipefail
cd "$(dirname "$0")"
[ -f .env ] || cp .env.example .env
gen() { head -c 32 /dev/urandom | base64 | tr -d '\n'; }
for key in HUGO_MASTER_KEY HUGO_AUTH_SIGNING_KEY; do
  existing="$(sed -n "s/^${key}=//p" .env | tail -n 1 | sed -E "s/^[[:space:]]*//; s/[[:space:]]*$//")"
  case "${existing}" in
    \"*\") existing="${existing#\"}"; existing="${existing%\"}" ;;
    \'*\') existing="${existing#\'}"; existing="${existing%\'}" ;;
  esac
  existing="$(printf '%s' "${existing}" | sed -E "s/^[[:space:]]*//; s/[[:space:]]*$//")"
  if [ -z "${existing}" ]; then
    value="$(gen)"
    if grep -qE "^${key}=" .env; then
      sed -i.bak "s|^${key}=.*|${key}=${value}|" .env
      rm -f .env.bak
    else
      printf '\n%s=%s\n' "${key}" "${value}" >> .env
    fi
    echo "Generated ${key} in docker/.env. Back it up: losing HUGO_MASTER_KEY makes stored secrets unreadable."
  fi
done
docker compose up -d "$@"
docker ps -a --filter "label=com.docker.compose.project=hugo"
