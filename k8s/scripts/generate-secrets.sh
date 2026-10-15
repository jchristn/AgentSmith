#!/usr/bin/env bash
# Creates Secret hugo-secrets in namespace hugo with random values:
#   master-key              HUGO_MASTER_KEY, 32 random bytes in base64; encrypts every stored credential
#   signing-key             HUGO_AUTH_SIGNING_KEY, signs session tokens
#   db-password             PostgreSQL password (components/postgres; unused with SQLite)
#   grafana-admin-password  Grafana admin password (components/observability; unused without it)
# Set HUGO_GRAFANA_PASSWORD to choose the Grafana password instead of a random one.
#
# An existing hugo-secrets is kept unless --force is given: a new master key cannot decrypt credentials stored with
# the old one, so rotating it after first boot loses them. Values are never written to the repository or to disk,
# and only a generated Grafana password is printed, once. Back the Secret up with the data (docs/KUBERNETES.md).
source "$(dirname "$0")/common.sh"
need kubectl
need openssl

FORCE=0
[ "${1:-}" = "--force" ] && FORCE=1

rand() { openssl rand -base64 48 | tr -dc 'A-Za-z0-9' | head -c "$1"; }

# Created through apply (not create) so the later kubectl apply of the overlay owns it cleanly.
if ! kcc get namespace "${HUGO_NAMESPACE}" >/dev/null 2>&1; then
  kcc create namespace "${HUGO_NAMESPACE}" --dry-run=client -o yaml | kcc apply -f - >/dev/null
fi

if [ "${FORCE}" = "1" ] || ! kc get secret hugo-secrets >/dev/null 2>&1; then
  GRAFANA_PASSWORD="${HUGO_GRAFANA_PASSWORD:-$(rand 24)}"
  kc create secret generic hugo-secrets \
    --from-literal=master-key="$(openssl rand -base64 32)" \
    --from-literal=signing-key="$(rand 48)" \
    --from-literal=db-password="$(rand 32)" \
    --from-literal=grafana-admin-password="${GRAFANA_PASSWORD}" \
    --dry-run=client -o yaml | kc apply -f - >/dev/null
  log "created secret hugo-secrets in namespace ${HUGO_NAMESPACE} (context $(context_name))"
  if [ -z "${HUGO_GRAFANA_PASSWORD:-}" ]; then
    log "  Grafana user admin, password (generated, shown once): ${GRAFANA_PASSWORD}"
  fi
  log "  back it up now, outside the cluster and outside the repository; without master-key the stored credentials are lost:"
  log "  kubectl -n ${HUGO_NAMESPACE} get secret hugo-secrets -o yaml > hugo-secrets.backup.yaml"
else
  log "secret hugo-secrets exists; keeping it (use --force to rotate, which makes stored credentials unreadable)"
fi
