#!/usr/bin/env bash
# Shared settings for the minikube scripts. Sourced, not executed.
#
#   MINIKUBE_PROFILE   minikube profile, which is also the kubectl context the scripts use (default hugo). The scripts
#                      pass it explicitly, so your current kubectl context is never relied on.
#   MINIKUBE_CPUS      CPUs for a new profile (default 4)
#   MINIKUBE_MEMORY    memory for a new profile (default 6g; 8g is comfortable with --with observability)
#   MINIKUBE_DRIVER    minikube driver (default: minikube's choice, usually docker)
#   HUGO_TAG           image tag (default v0.1.0)
set -euo pipefail

MINIKUBE_PROFILE="${MINIKUBE_PROFILE:-hugo}"
MINIKUBE_SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HUGO_REPO_DIR="$(cd "${MINIKUBE_SCRIPT_DIR}/../.." && pwd)"
K8S_SCRIPTS="${HUGO_REPO_DIR}/k8s/scripts"
export HUGO_KUBE_CONTEXT="${MINIKUBE_PROFILE}"
export HUGO_TAG="${HUGO_TAG:-v0.1.0}"

mlog() { printf '[hugo minikube] %s\n' "$*"; }
mfail() { printf '[hugo minikube] error: %s\n' "$*" >&2; exit 1; }
command -v minikube >/dev/null 2>&1 || mfail "minikube is required (https://minikube.sigs.k8s.io/docs/start/)"
command -v kubectl >/dev/null 2>&1 || mfail "kubectl is required"

running() { minikube -p "${MINIKUBE_PROFILE}" status --format '{{.APIServer}}' 2>/dev/null | grep -q Running; }
require_running() { running || mfail "minikube profile ${MINIKUBE_PROFILE} is not running; run minikube/scripts/start.sh first"; }

# Puts the Hugo images into minikube. pull: from Docker Hub (minikube image pull, which refreshes a re-pushed tag).
# load: from the local Docker daemon (minikube image load). build: docker build here, then load.
provide_images() {
  local mode="$1" image
  case "${mode}" in
    pull)
      for image in jchristn77/hugo-server jchristn77/hugo-dashboard; do
        mlog "pulling ${image}:${HUGO_TAG} into minikube"
        minikube -p "${MINIKUBE_PROFILE}" image pull "${image}:${HUGO_TAG}"
      done
      ;;
    build)
      command -v docker >/dev/null 2>&1 || mfail "--build needs docker"
      mlog "building jchristn77/hugo-server:${HUGO_TAG} and jchristn77/hugo-dashboard:${HUGO_TAG} with docker"
      docker build -t "jchristn77/hugo-server:${HUGO_TAG}" -f "${HUGO_REPO_DIR}/src/Hugo.Server/Dockerfile" "${HUGO_REPO_DIR}"
      docker build -t "jchristn77/hugo-dashboard:${HUGO_TAG}" "${HUGO_REPO_DIR}/dashboard"
      provide_images load
      ;;
    load)
      for image in jchristn77/hugo-server jchristn77/hugo-dashboard; do
        mlog "loading ${image}:${HUGO_TAG} from the local Docker daemon"
        minikube -p "${MINIKUBE_PROFILE}" image load --overwrite=true "${image}:${HUGO_TAG}"
      done
      ;;
    *) mfail "unknown image mode ${mode}" ;;
  esac
}
