#!/usr/bin/env bash
# Pull the latest published Hugo images and recreate the stack. Non-destructive: named volumes are preserved.
# For a destructive reset use factory/reset.sh.
set -euo pipefail
cd "$(dirname "$0")"
docker compose pull
docker compose down
docker compose up -d
docker ps -a
