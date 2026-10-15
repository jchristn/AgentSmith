#!/usr/bin/env bash
# DESTRUCTIVE factory reset: stops the Hugo stack, deletes every named volume (database, secrets, history,
# metrics, traces, logs, dashboards state), and starts fresh with the default seed data.
set -euo pipefail
cd "$(dirname "$0")/.."
read -r -p "This deletes ALL Hugo data, including stored secrets. Type RESET to continue: " answer
[ "${answer}" = "RESET" ] || { echo "Cancelled."; exit 1; }
docker compose --profile postgres down -v
docker compose up -d
docker ps -a
echo "Hugo was reset. Log in as anderson@hugo.local / password (tenant default) and change the password."
