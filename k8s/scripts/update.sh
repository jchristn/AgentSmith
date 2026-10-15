#!/usr/bin/env bash
# Non-destructive update, the Kubernetes counterpart of docker/update.sh: refreshes the images, re-applies the
# overlay, restarts the server and dashboard so they run the new images and settings, waits for the rollout, and
# prints pod status. Volumes (PersistentVolumeClaims) and Secret hugo-secrets are kept.
#
# Usage: k8s/scripts/update.sh [k3s|production|minikube] [--with postgres,observability] [--import]
#   Without a target or --with, the shape recorded by the last deploy is used.
#   --import   docker pull the images and import them into k3s first (K3D_CLUSTER, K3S_CONTAINER, or a k3s host);
#              needed with overlays/k3s, whose IfNotPresent policy never re-pulls a tag on its own.
# The base and production overlay use imagePullPolicy Always, so the restart itself pulls the newest image.
source "$(dirname "$0")/common.sh"
need kubectl

TARGET=""
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

read -r REC_TARGET REC_WITH <<<"$(recorded_shape) "
[ -n "${TARGET}" ] || TARGET="${REC_TARGET:-}"
[ -n "${TARGET}" ] || fail "no target given and none recorded on namespace ${HUGO_NAMESPACE}; pass k3s, production, or minikube"
[ "${WITH_SET}" = "1" ] || WITH="${REC_WITH:-}"
check_components "${WITH}"

if [ "${IMPORT}" = "1" ]; then
  "${SCRIPT_DIR}/import-images.sh" --pull
fi

apply_target "${TARGET}" "${WITH}"
kc rollout restart deployment/hugo-server deployment/hugo-dashboard
wait_for_rollout
kc get pods -o wide
