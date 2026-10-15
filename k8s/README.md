# Hugo Kubernetes assets

Kustomize trees, a Helm chart, and scripts that deploy Hugo's published images (`jchristn77/hugo-server`, `jchristn77/hugo-dashboard`) to a Kubernetes cluster. [`docs/KUBERNETES.md`](../docs/KUBERNETES.md) explains how to choose a runtime, the secrets, deployment, updates, factory reset, backups, and why the server always runs one replica. For a laptop, [`minikube/`](../minikube/README.md) wraps all of this in four commands.

## Quick start

```bash
k8s/scripts/deploy.sh k3s                                  # current kubectl context; creates Secret hugo-secrets first
k8s/scripts/deploy.sh k3s --with postgres,observability    # PostgreSQL and Prometheus, Tempo, Loki, Alloy, Grafana
k8s/scripts/smoke-test.sh
k8s/scripts/port-forward.sh                                # dashboard :8801, API :8800, Grafana :3000
```

Targets are `k3s`, `production`, and `minikube`. Set `HUGO_KUBE_CONTEXT` to use a context other than the current one and `HUGO_TAG` for another image tag. With Helm instead:

```bash
k8s/scripts/generate-secrets.sh
helm install hugo k8s/helm/hugo -n hugo -f k8s/helm/hugo/values-k3s.yaml
```

## Layout

| Path | Contents |
|---|---|
| `base/` | Server (one replica, Recreate) and dashboard Deployments and Services, the `hugo-data` claim, the settings ConfigMap from `config/hugo.json`, service accounts, network policies |
| `components/postgres/` | In-cluster PostgreSQL StatefulSet and the server patch that uses it and waits for it |
| `components/observability/` | Prometheus, Tempo, Loki, Alloy, Grafana; `config/` and `dashboards/` are mirrored (see below) |
| `components/ingress/` | The Ingress for dashboard, REST, MCP, and A2A with Server-Sent Events settings |
| `overlays/k3s/` | Traefik, `local-path` storage, `IfNotPresent` for imported images |
| `overlays/production/` | External PostgreSQL (`hugo-db`), TLS (`hugo-tls`), resources, disruption budgets, topology spread |
| `helm/hugo/` | The chart; `values-k3s.yaml`, `values-minikube.yaml`, `values-production.yaml` match the overlays |
| `scripts/` | `generate-secrets`, `deploy`, `port-forward`, `update`, `factory-reset`, `wait-rollout`, `smoke-test`, `import-images`, `sync-mirrors` (`.sh` and `.bat`) |
| `tests/` | `check-render.sh` and `compare-render.py`: render, kubeconform, mirror checks, and the Kustomize-to-Helm comparison |

Components are added on top of an overlay. The scripts do it with a generated kustomization in `k8s/.work/` (git ignores it); by hand it looks like this:

```yaml
apiVersion: kustomize.config.k8s.io/v1beta1
kind: Kustomization
namespace: hugo
resources: [../../overlays/k3s]
components: [../../components/postgres, ../../components/observability]
```

## Rules for changes

Keep the server at one replica with the Recreate strategy until the event hub and approval signals are shared across replicas (D-045, D-027). The chart fails to render otherwise.

Every change to the Kustomize trees needs the same change in the chart, and the reverse. Run `k8s/tests/check-render.sh` before you push; CI (`.github/workflows/k8s.yaml`) runs it too. It needs `kubectl`, `helm`, `kubeconform`, `python3`, and PyYAML or `yq`.

`components/observability/dashboards/` and `helm/hugo/files/observability/dashboards/` are copies of `assets/grafana/`. `components/observability/config/` copies Loki, Tempo, and the Grafana provisioning from `docker/observability/`, and `helm/hugo/files/` copies `base/config/hugo.json` and `components/observability/config/`. Edit the sources, then run `k8s/scripts/sync-mirrors.sh`.
