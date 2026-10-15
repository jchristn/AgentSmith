#!/usr/bin/env bash
# DESTRUCTIVE factory reset, the Kubernetes counterpart of docker/factory/reset.sh. Deletes namespace hugo, which
# removes every PersistentVolumeClaim (the SQLite or PostgreSQL data, Prometheus, Loki, Tempo) and Secret hugo-secrets,
# then creates new secrets and redeploys the same shape. Requires typing RESET. An external database
# (overlays/production) is not touched: drop and recreate it yourself.
#
# Usage: k8s/scripts/factory-reset.sh [k3s|production|minikube] [--with postgres,observability]
#   Without a target or --with, the shape recorded by the last deploy is used.
source "$(dirname "$0")/common.sh"
need kubectl

TARGET=""
while [ "$#" -gt 0 ]; do
  if parse_with "$@"; then
    [ "$1" = "--with" ] && shift
    shift
    continue
  fi
  case "$1" in
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

printf 'This deletes namespace %s in context %s: ALL Hugo data, volumes, and Secret hugo-secrets (the master key).\n' "${HUGO_NAMESPACE}" "$(context_name)"
printf 'It then redeploys %s%s with new secrets.\n' "$(target_dir "${TARGET}")" "${WITH:+ with ${WITH}}"
printf 'Type RESET to continue: '
read -r answer
[ "${answer}" = "RESET" ] || fail "aborted"

kcc delete namespace "${HUGO_NAMESPACE}" --wait=true --ignore-not-found
"${SCRIPT_DIR}/generate-secrets.sh"
apply_target "${TARGET}" "${WITH}"
wait_for_rollout
kc get pods -o wide
print_access
