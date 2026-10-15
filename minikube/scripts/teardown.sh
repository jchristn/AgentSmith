#!/usr/bin/env bash
# DESTRUCTIVE: deletes the whole minikube profile (the cluster, Hugo, its data, and the loaded images) and its kubectl
# context. Requires typing the profile name, or --yes for scripts.
# Usage: minikube/scripts/teardown.sh [--yes]
source "$(dirname "$0")/common.sh"

if [ "${1:-}" != "--yes" ]; then
  printf 'This deletes minikube profile %s with everything in it. Type %s to continue: ' "${MINIKUBE_PROFILE}" "${MINIKUBE_PROFILE}"
  read -r answer
  [ "${answer}" = "${MINIKUBE_PROFILE}" ] || mfail "aborted"
fi
minikube delete -p "${MINIKUBE_PROFILE}"
mlog "profile ${MINIKUBE_PROFILE} deleted"
