#!/usr/bin/env bash
# Run the three SDK harnesses and the coverage check against a live Hugo server.
#
#   HUGO_SDK_SERVER=http://127.0.0.1:8800 sdk/scripts/run-harnesses.sh
#
# The server must already be running (for example `dotnet run --project src/Hugo.Server -f net10.0`).
# Each harness provisions its own tenant and deletes it at the end. Exit code is nonzero when any step fails.

set -u
SDK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export HUGO_SDK_SERVER="${HUGO_SDK_SERVER:-http://127.0.0.1:8800}"
failures=0

step() {
  echo
  echo "== $1"
}

step "Waiting for ${HUGO_SDK_SERVER}/v1.0/api/health"
for _ in $(seq 1 60); do
  if curl -fsS "${HUGO_SDK_SERVER}/v1.0/api/health" > /dev/null 2>&1; then break; fi
  sleep 2
done

step "C# SDK"
(cd "${SDK_DIR}/csharp/src" && dotnet build Hugo.Sdk.sln -warnaserror && dotnet run --project Test.Automated -f net10.0) || failures=$((failures + 1))

step "Python SDK"
(cd "${SDK_DIR}/python" && python3 -m venv .venv && .venv/bin/pip install -q -e ".[test]" && .venv/bin/python run_harness.py) || failures=$((failures + 1))

step "JavaScript and TypeScript SDK"
(cd "${SDK_DIR}/js" && npm ci --no-audit --no-fund && npm run build && npm run typecheck && npm test) || failures=$((failures + 1))

step "Coverage against /openapi.json"
python3 "${SDK_DIR}/scripts/check_coverage.py" --server "${HUGO_SDK_SERVER}" || failures=$((failures + 1))

echo
if [ "${failures}" -eq 0 ]; then echo "All SDK checks passed"; else echo "${failures} SDK check(s) failed"; fi
exit "${failures}"
