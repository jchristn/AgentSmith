#!/usr/bin/env bash
# Non-destructive update: refreshes the images (default: pull HUGO_TAG again from Docker Hub; --load or --build as
# in deploy.sh), re-applies the overlay with the components chosen at deploy time, restarts the server and dashboard,
# waits, and prints pod status. Data volumes and Secret hugo-secrets are kept.
# Usage: minikube/scripts/update.sh [--load|--build] [--with postgres,observability]
source "$(dirname "$0")/common.sh"

MODE="pull"
PASS=()
while [ "$#" -gt 0 ]; do
  case "$1" in
    --load) MODE="load" ;;
    --build) MODE="build" ;;
    --with) PASS+=(--with "$2"); shift ;;
    --with=*) PASS+=("$1") ;;
    *) mfail "unknown argument $1" ;;
  esac
  shift
done

require_running
provide_images "${MODE}"
"${K8S_SCRIPTS}/update.sh" minikube "${PASS[@]+"${PASS[@]}"}"
