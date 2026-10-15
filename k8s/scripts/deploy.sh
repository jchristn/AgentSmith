#!/usr/bin/env bash
# Deploys Hugo: creates the Secret if missing, applies an overlay with optional components, waits for the rollout,
# and prints how to reach it. Works against the current kubectl context (or HUGO_KUBE_CONTEXT).
#
# Usage: k8s/scripts/deploy.sh [k3s|production|minikube] [--with postgres,observability] [--import]
#   target           k3s (default), production, or minikube (minikube/scripts/deploy.sh is the easier way there)
#   --with LIST      add components: postgres (in-cluster PostgreSQL instead of SQLite), observability (Prometheus,
#                    Tempo, Loki, Alloy, Grafana with the Hugo dashboards). Not for production, which uses an
#                    external database.
#   --import         copy jchristn77/hugo-server and hugo-dashboard at HUGO_TAG from the local Docker daemon into
#                    the cluster first: into the k3d cluster named by K3D_CLUSTER, the k3s container named by
#                    K3S_CONTAINER, or a k3s host with sudo k3s ctr. Without it the cluster pulls from Docker Hub.
#
# Examples:
#   k8s/scripts/deploy.sh k3s
#   k8s/scripts/deploy.sh k3s --with postgres,observability
#   K3D_CLUSTER=hugo k8s/scripts/deploy.sh k3s --import
source "$(dirname "$0")/common.sh"
need kubectl

TARGET="k3s"
IMPORT=0
while [ "$#" -gt 0 ]; do
  if parse_with "$@"; then
    [ "$1" = "--with" ] && shift
    shift
    continue
  fi
  case "$1" in
    --import) IMPORT=1 ;;
    k3s|production|minikube) TARGET="$1" ;;
    *) fail "unknown argument $1" ;;
  esac
  shift
done
check_components "${WITH}"
[ "${TARGET}" = "production" ] && [ -n "${WITH}" ] && case ",${WITH}," in *,postgres,*) fail "production uses an external database (Secret hugo-db); do not add components/postgres" ;; esac

if [ "${IMPORT}" = "1" ]; then
  "${SCRIPT_DIR}/import-images.sh"
fi

"${SCRIPT_DIR}/generate-secrets.sh"
if [ "${TARGET}" = "production" ] && ! kc get secret hugo-db >/dev/null 2>&1; then
  log "warning: Secret hugo-db (external PostgreSQL: host, port, database, username, password) is missing; the server waits for it"
fi
apply_target "${TARGET}" "${WITH}"
wait_for_rollout
kc get pods -o wide
print_access
