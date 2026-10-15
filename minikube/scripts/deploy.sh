#!/usr/bin/env bash
# Deploys Hugo to minikube in one step: starts the profile if needed, puts the images in place, creates Secret
# hugo-secrets if missing, applies minikube/overlay (plus components), and waits for the rollout.
#
# Usage: minikube/scripts/deploy.sh [--load|--build] [--with postgres,observability]
#   (default)   minikube pulls jchristn77/hugo-server and hugo-dashboard at HUGO_TAG from Docker Hub
#   --load      use images from your local Docker daemon (minikube image load), for example after build-all.sh
#   --build     docker build both images from this checkout, then load them
#   --with      add components: postgres (PostgreSQL instead of SQLite), observability (Prometheus, Tempo, Loki,
#               Alloy, Grafana with the Hugo dashboards)
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

running || "${MINIKUBE_SCRIPT_DIR}/start.sh"
provide_images "${MODE}"
"${K8S_SCRIPTS}/deploy.sh" minikube "${PASS[@]+"${PASS[@]}"}"
mlog "next: minikube/scripts/port-forward.sh, then open http://127.0.0.1:8801"
mlog "or, with the ingress addon: add '$(minikube -p "${MINIKUBE_PROFILE}" ip) hugo.local' to /etc/hosts (Linux), or run"
mlog "'minikube -p ${MINIKUBE_PROFILE} tunnel' and add '127.0.0.1 hugo.local' (macOS and Windows), then open http://hugo.local"
