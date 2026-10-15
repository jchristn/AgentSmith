#!/usr/bin/env bash
# Creates (or starts) the minikube profile for Hugo with the ingress and metrics-server addons.
# Defaults: profile hugo, 4 CPUs, 6 GB of memory; change them with MINIKUBE_PROFILE, MINIKUBE_CPUS, MINIKUBE_MEMORY,
# and MINIKUBE_DRIVER. CPU and memory apply when the profile is created; delete it (teardown.sh) to change them.
# minikube makes the profile your current kubectl context; the Hugo scripts do not depend on that.
source "$(dirname "$0")/common.sh"

if running; then
  mlog "profile ${MINIKUBE_PROFILE} is already running"
else
  ARGS=(start -p "${MINIKUBE_PROFILE}" --cpus="${MINIKUBE_CPUS:-4}" --memory="${MINIKUBE_MEMORY:-6g}" --addons=ingress --addons=metrics-server)
  [ -n "${MINIKUBE_DRIVER:-}" ] && ARGS+=(--driver="${MINIKUBE_DRIVER}")
  mlog "minikube ${ARGS[*]}"
  minikube "${ARGS[@]}"
fi
minikube -p "${MINIKUBE_PROFILE}" addons enable ingress >/dev/null
minikube -p "${MINIKUBE_PROFILE}" addons enable metrics-server >/dev/null
mlog "profile ${MINIKUBE_PROFILE} is up (kubectl context ${MINIKUBE_PROFILE}); next: minikube/scripts/deploy.sh"
