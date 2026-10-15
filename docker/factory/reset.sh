#!/usr/bin/env bash
# DESTRUCTIVE factory reset of the Hugo factory stack (docker/factory/compose.yaml, project hugo-factory).
# Stops the stack, deletes every factory volume (database, secrets, history, metrics, traces, logs, dashboard state,
# demo targets), starts it fresh, waits for the seed container, and prints the URLs and demo credentials.
# The plain stack in docker/ (project hugo) is not touched. docs/FACTORY.md describes what gets seeded.
#
#   ./reset.sh                    # asks you to type RESET
#   echo RESET | ./reset.sh       # unattended
set -euo pipefail
cd "$(dirname "$0")"

read -r -p "This deletes ALL Hugo factory data, including stored secrets. Type RESET to continue: " answer
[ "${answer}" = "RESET" ] || { echo "Cancelled."; exit 1; }

docker compose down -v --remove-orphans
docker compose up -d
echo "Waiting for the seed container (first start pulls images and initializes the databases)..."
status=0
docker compose wait hugo-seed > /dev/null || status=$?
docker ps -a --filter "label=com.docker.compose.project=hugo-factory"
echo
docker compose logs --no-log-prefix hugo-seed
if [ "${status}" -ne 0 ]; then
  echo "The seed container failed (exit ${status}). Inspect it with: docker compose -f docker/factory/compose.yaml logs hugo-seed" >&2
  exit 1
fi
echo
echo "Hugo factory stack was reset and seeded."
