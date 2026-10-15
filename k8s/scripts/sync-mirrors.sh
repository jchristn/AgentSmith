#!/usr/bin/env bash
# Refreshes the copies Kustomize and Helm need from their sources (neither can read files outside its own
# directory). Run it after changing assets/grafana, docker/observability (Loki, Tempo, Grafana provisioning),
# k8s/base/config/hugo.json, or k8s/components/observability/config; k8s/tests/check-render.sh fails until you do.
source "$(dirname "$0")/common.sh"

OBS="${K8S_DIR}/components/observability"
CHART="${K8S_DIR}/helm/hugo/files"

rm -f "${OBS}/dashboards/"*.json "${CHART}/observability/dashboards/"*.json
cp "${REPO_DIR}/assets/grafana/"*.json "${OBS}/dashboards/"
cp "${REPO_DIR}/assets/grafana/"*.json "${CHART}/observability/dashboards/"
for f in loki.yaml tempo.yaml grafana.ini grafana-dashboards.yaml grafana-datasources.yaml; do
  cp "${REPO_DIR}/docker/observability/${f}" "${OBS}/config/${f}"
done
cp "${OBS}/config/"* "${CHART}/observability/"
cp "${K8S_DIR}/base/config/hugo.json" "${CHART}/hugo.json"
log "mirrors refreshed. If dashboards were added or removed, update the generator list in"
log "k8s/components/observability/kustomization.yaml and the projected volume in grafana.yaml (the chart follows by itself)."
