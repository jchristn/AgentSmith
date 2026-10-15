#!/usr/bin/env bash
# Copies jchristn77/hugo-server and jchristn77/hugo-dashboard at HUGO_TAG from the local Docker daemon into a k3s
# cluster: the k3d cluster named by K3D_CLUSTER, the rancher/k3s container named by K3S_CONTAINER, or (neither set)
# a k3s host through sudo k3s ctr. --pull runs docker pull first, so the newest published image is imported.
# Pair it with an overlay that uses imagePullPolicy IfNotPresent (overlays/k3s does).
source "$(dirname "$0")/common.sh"
need docker

PULL=0
[ "${1:-}" = "--pull" ] && PULL=1
platform="linux/$(docker version --format '{{.Server.Arch}}')"

for name in ${HUGO_IMAGES}; do
  image="${name}:${HUGO_TAG}"
  if [ "${PULL}" = "1" ]; then
    log "pulling ${image}"
    docker pull --platform "${platform}" "${image}" >/dev/null
  fi
  docker image inspect "${image}" >/dev/null 2>&1 || fail "${image} is not in the local Docker daemon (pull it, build it, or use --pull)"
  # Only this machine's platform is saved: the published images are multi-platform, and Docker holds just one of
  # them, so a full save (and k3d image import, which does one) fails inside containerd with "content digest not
  # found" while still exiting 0.
  if [ -n "${K3D_CLUSTER:-}" ]; then
    nodes="$(docker ps -q --filter "label=k3d.cluster=${K3D_CLUSTER}" --filter "label=k3d.role=server") $(docker ps -q --filter "label=k3d.cluster=${K3D_CLUSTER}" --filter "label=k3d.role=agent")"
    [ -n "${nodes// /}" ] || fail "no running nodes of k3d cluster ${K3D_CLUSTER}"
    for node in ${nodes}; do
      docker save --platform "${platform}" "${image}" | docker exec -i "${node}" ctr -n k8s.io images import - >/dev/null \
        || fail "could not import ${image} into k3d node ${node}"
    done
  elif [ -n "${K3S_CONTAINER:-}" ]; then
    docker save --platform "${platform}" "${image}" | docker exec -i "${K3S_CONTAINER}" ctr -n k8s.io images import - >/dev/null \
      || fail "could not import ${image} into ${K3S_CONTAINER}"
  else
    docker save --platform "${platform}" "${image}" | sudo k3s ctr -n k8s.io images import - >/dev/null \
      || fail "could not import ${image} into k3s"
  fi
  log "imported ${image}"
done
