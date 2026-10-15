#!/bin/sh
# Seeds the Hugo k3s testbed: rewrites the kubeconfig for the host, applies the fixtures, waits for the workloads,
# and exports the read-only service account token and the cluster CA. Runs inside the k3s-init container.
set -eu

while [ ! -s /output/kubeconfig.yaml ]; do sleep 1; done

# In-cluster copy for this container (the k3s server is reachable as k3s:6443) and the host copy (127.0.0.1).
sed 's#https://127.0.0.1:6443#https://k3s:6443#' /output/kubeconfig.yaml > /tmp/kubeconfig
sed "s#https://127.0.0.1:6443#https://127.0.0.1:${HOST_PORT}#" /output/kubeconfig.yaml > /output/kubeconfig.tmp
mv /output/kubeconfig.tmp /output/kubeconfig
export KUBECONFIG=/tmp/kubeconfig

until kubectl get --raw=/readyz >/dev/null 2>&1; do sleep 1; done
until kubectl -n default get serviceaccount default >/dev/null 2>&1; do sleep 1; done

kubectl apply -f /manifests/crd.yaml
kubectl wait --for=condition=Established crd/widgets.testbed.hugo.dev --timeout=120s
kubectl apply -f /manifests/fixtures.yaml
kubectl -n hugo-test rollout status deployment/web --timeout=300s

until [ -n "$(kubectl -n hugo-test get secret hugo-reader-token -o jsonpath='{.data.token}' 2>/dev/null)" ]; do sleep 1; done
kubectl -n hugo-test get secret hugo-reader-token -o jsonpath='{.data.token}' | base64 -d > /output/reader-token
kubectl -n hugo-test get secret hugo-reader-token -o jsonpath='{.data.ca\.crt}' | base64 -d > /output/ca.crt

touch /tmp/seeded
echo "k3s testbed seeded"
exec sleep infinity
