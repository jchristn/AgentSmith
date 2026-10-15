#!/usr/bin/env bash
# Builds and pushes every Hugo image (server, dashboard) for linux/amd64 and linux/arm64, tagged <tag> and latest.
# Usage: ./build-all.sh <tag>   (for example v0.1.0)
set -euo pipefail
if [ $# -lt 1 ] || [ -z "$1" ]; then
  echo "usage: $0 <tag>" >&2
  exit 2
fi
cd "$(dirname "$0")"
./build-server.sh "$1"
./build-dashboard.sh "$1"
