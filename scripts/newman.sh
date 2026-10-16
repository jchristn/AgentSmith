#!/usr/bin/env bash
# Run the Hugo Postman collection with Newman against a running server.
#
#   scripts/newman.sh                                                 # docker/ stack on http://127.0.0.1:8800, seeded passwords
#   HUGO_PASSWORD='Your-Password-1!' HUGO_ADMIN_PASSWORD='...' scripts/newman.sh   # accounts whose passwords you changed
#   HUGO_PORT=8810 HUGO_PASSWORD='HugoDemo2026!' HUGO_ADMIN_PASSWORD='password' scripts/newman.sh
#                                                                     # factory demo stack (docs/FACTORY.md)
#   HUGO_MODEL_BASE_URL=http://127.0.0.1:11434 HUGO_MODEL_NAME=llama3.1 scripts/newman.sh
#                                                                     # also run the requests that need a model
#
# Extra arguments are passed to newman (for example --folder MCP). Uses newman from PATH when installed, otherwise
# npx downloads newman 6. The collection signs in with HUGO_PASSWORD and HUGO_ADMIN_PASSWORD (default "password") and
# never changes them (mustChangePassword is advisory, D-048). HUGO_NEW_PASSWORD, when set, is tried as a fallback
# for both accounts. Exit code is newman's: 0 when every assertion passes.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
COLLECTION="${ROOT}/assets/postman/Hugo.postman_collection.json"
ENVIRONMENT="${ROOT}/assets/postman/Hugo.postman_environment.json"

if command -v newman >/dev/null 2>&1; then
    NEWMAN=(newman)
else
    NEWMAN=(npx --yes newman@6)
fi

"${NEWMAN[@]}" run "${COLLECTION}" \
    -e "${ENVIRONMENT}" \
    --env-var "host=${HUGO_HOST:-127.0.0.1}" \
    --env-var "port=${HUGO_PORT:-8800}" \
    --env-var "tenantId=${HUGO_TENANT_ID:-default}" \
    --env-var "email=${HUGO_EMAIL:-anderson@hugo.local}" \
    --env-var "password=${HUGO_PASSWORD:-password}" \
    --env-var "adminEmail=${HUGO_ADMIN_EMAIL:-admin@hugo.local}" \
    --env-var "adminPassword=${HUGO_ADMIN_PASSWORD:-password}" \
    --env-var "newPassword=${HUGO_NEW_PASSWORD:-}" \
    --env-var "modelBaseUrl=${HUGO_MODEL_BASE_URL:-}" \
    --env-var "modelName=${HUGO_MODEL_NAME:-llama3.1}" \
    --timeout-request 360000 \
    "$@"
