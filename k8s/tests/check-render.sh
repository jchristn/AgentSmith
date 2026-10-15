#!/usr/bin/env bash
# Renders and checks every Hugo Kubernetes deployment shape; the k8s CI workflow runs it on every push.
#
#   1. Mirrors: the copies Kustomize and Helm need (they cannot read files outside their own directories) match their
#      sources: assets/grafana, docker/observability, k8s/base/config, and k8s/components/observability.
#   2. Kustomize: k8s/overlays/k3s, k8s/overlays/production, and minikube/overlay, each alone and wrapped with the
#      postgres and observability components the deploy scripts can add.
#   3. Helm: helm lint and helm template with values-k3s, values-minikube, and values-production, with the same
#      database and observability variations.
#   4. kubeconform validates every rendered file (strict, Kubernetes 1.31).
#   5. compare-render.py checks that each Kustomize shape and its Helm counterpart produce the same objects.
#
# Usage: k8s/tests/check-render.sh            (output in k8s/.work/rendered, which git ignores)
# Needs kubectl (or kustomize), helm, kubeconform, python3, and PyYAML or yq.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT="${OUT:-${ROOT}/k8s/.work/rendered}"
WORK="${ROOT}/k8s/.work/check"
KUBE_VERSION="${KUBE_VERSION:-1.31.0}"
FAILED=0

log() { printf '[check-render] %s\n' "$*"; }
bad() { printf '[check-render] FAIL %s\n' "$*" >&2; FAILED=1; }

kustomize_build() {
  if command -v kustomize >/dev/null 2>&1; then kustomize build "$1"; else kubectl kustomize "$1"; fi
}

for tool in helm kubeconform python3; do
  command -v "${tool}" >/dev/null 2>&1 || { echo "[check-render] ${tool} is required" >&2; exit 1; }
done
command -v kustomize >/dev/null 2>&1 || command -v kubectl >/dev/null 2>&1 || { echo "[check-render] kubectl or kustomize is required" >&2; exit 1; }

rm -rf "${OUT}" "${WORK}"
mkdir -p "${OUT}" "${WORK}"

# 1. Mirrors.
log "mirrors"
mirror() {
  if diff -r "$1" "$2" >/dev/null; then log "  ok   $2 = $1"; else diff -r "$1" "$2" | head -20 >&2; bad "$2 differs from $1 (run k8s/scripts/sync-mirrors.sh)"; fi
}
mirror "${ROOT}/assets/grafana" "${ROOT}/k8s/components/observability/dashboards"
mirror "${ROOT}/assets/grafana" "${ROOT}/k8s/helm/hugo/files/observability/dashboards"
for f in loki.yaml tempo.yaml grafana.ini grafana-dashboards.yaml grafana-datasources.yaml; do
  mirror "${ROOT}/docker/observability/${f}" "${ROOT}/k8s/components/observability/config/${f}"
done
for f in "${ROOT}"/k8s/components/observability/config/*; do
  mirror "${f}" "${ROOT}/k8s/helm/hugo/files/observability/$(basename "${f}")"
done
mirror "${ROOT}/k8s/base/config/hugo.json" "${ROOT}/k8s/helm/hugo/files/hugo.json"

# 2. Kustomize. wrap NAME TARGET [COMPONENT...] writes a kustomization in k8s/.work/check/NAME that adds components
# to TARGET (a path relative to the repository root), the same way k8s/scripts/deploy.sh does.
wrap() {
  local name="$1" target="$2"; shift 2
  local dir="${WORK}/${name}"
  mkdir -p "${dir}"
  {
    echo "apiVersion: kustomize.config.k8s.io/v1beta1"
    echo "kind: Kustomization"
    echo "namespace: hugo"
    echo "resources:"
    echo "  - ../../../../${target}"
    if [ "$#" -gt 0 ]; then
      echo "components:"
      for c in "$@"; do echo "  - ../../../../k8s/components/${c}"; done
    fi
  } > "${dir}/kustomization.yaml"
  kustomize_build "${dir}" > "${OUT}/kustomize-${name}.yaml" || bad "kustomize ${name}"
}

log "kustomize"
for shape in k3s production minikube; do
  case "${shape}" in
    minikube) target="minikube/overlay" ;;
    *) target="k8s/overlays/${shape}" ;;
  esac
  wrap "${shape}" "${target}"
  [ "${shape}" = "production" ] && continue
  wrap "${shape}-postgres" "${target}" postgres
  wrap "${shape}-observability" "${target}" observability
  wrap "${shape}-postgres-observability" "${target}" postgres observability
done
# The base alone, as kubectl apply -k k8s/base would install it.
kustomize_build "${ROOT}/k8s/base" > "${OUT}/kustomize-base.yaml" || bad "kustomize base"

# 3. Helm.
log "helm"
CHART="${ROOT}/k8s/helm/hugo"
helm lint "${CHART}" >/dev/null || bad "helm lint (defaults)"
template() {
  local name="$1" values="$2"; shift 2
  helm lint "${CHART}" -f "${CHART}/${values}.yaml" "$@" >/dev/null || bad "helm lint ${name}"
  helm template hugo "${CHART}" -n hugo -f "${CHART}/${values}.yaml" "$@" > "${OUT}/helm-${name}.yaml" || bad "helm template ${name}"
}
helm template hugo "${CHART}" -n hugo > "${OUT}/helm-base.yaml" || bad "helm template base"
for shape in k3s production minikube; do
  template "${shape}" "values-${shape}"
  [ "${shape}" = "production" ] && continue
  template "${shape}-postgres" "values-${shape}" --set database.mode=embedded
  template "${shape}-observability" "values-${shape}" --set observability.enabled=true
  template "${shape}-postgres-observability" "values-${shape}" --set database.mode=embedded --set observability.enabled=true
done
if helm template hugo "${CHART}" -n hugo --set server.replicas=2 >/dev/null 2>&1; then bad "the chart rendered server.replicas=2"; fi

# 4. kubeconform.
log "kubeconform"
kubeconform -strict -summary -kubernetes-version "${KUBE_VERSION}" "${OUT}" || bad "kubeconform"

# 5. Compare.
log "compare kustomize and helm"
for f in "${OUT}"/kustomize-*.yaml; do
  name="$(basename "${f}" .yaml)"; name="${name#kustomize-}"
  python3 "${ROOT}/k8s/tests/compare-render.py" "${f}" "${OUT}/helm-${name}.yaml" || bad "compare ${name}"
done

if [ "${FAILED}" = "1" ]; then
  log "FAILED"
  exit 1
fi
log "all checks passed (${OUT})"
