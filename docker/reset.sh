#!/usr/bin/env bash
# DESTRUCTIVE reset of the Hugo stack in this directory: stops it, deletes every named volume (database, stored
# secrets, history, metrics, traces, logs, Grafana state), and starts fresh with the default seed data.
# The seeded demo stack has its own reset: factory/reset.sh.
set -euo pipefail
cd "$(dirname "$0")"
read -r -p "This deletes ALL Hugo data in this stack, including stored secrets. Type RESET to continue: " answer
[ "${answer}" = "RESET" ] || { echo "Cancelled."; exit 1; }
docker compose --profile postgres down -v --remove-orphans
docker compose up -d
docker ps -a --filter "label=com.docker.compose.project=hugo"
echo "Hugo was reset. Log in as anderson@hugo.local / password (tenant default) and change the password."
