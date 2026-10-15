# Hugo on minikube

This directory stands up the whole of Hugo on your workstation with minikube: the server, the dashboard, and if you want them PostgreSQL and the observability stack. It uses the published images and the same manifests as `k8s/`, through its own overlay in `overlay/`. The full reference is [`docs/KUBERNETES.md`](../docs/KUBERNETES.md); this page is the short version.

## From nothing to a running Hugo

You need minikube, `kubectl`, and `openssl`. The default profile gets 4 CPUs and 6 GB of memory, so give Docker (or your VM driver) at least that much.

```bash
minikube/scripts/start.sh                         # creates profile "hugo" with the ingress and metrics-server addons
minikube/scripts/deploy.sh --with observability   # pulls the images, creates Secret hugo-secrets, applies, waits
minikube/scripts/smoke-test.sh                    # checks health, sign-in, connectors, Grafana
minikube/scripts/port-forward.sh                  # leave it running
```

Then open the dashboard at http://127.0.0.1:8801 and sign in to tenant `default` as `anderson@hugo.local` with password `password`. Hugo asks for a new password first. (If you ran the smoke test, it already changed the password and printed the new one.) The REST API, MCP at `/mcp`, and A2A at `/v1.0/a2a` are on http://127.0.0.1:8800, and Grafana on http://127.0.0.1:3000 as `admin` with the password `generate-secrets` printed, which you can read back with:

```bash
kubectl --context hugo -n hugo get secret hugo-secrets -o jsonpath='{.data.grafana-admin-password}' | base64 --decode; echo
```

These are the same ports Docker Compose uses, so stop Compose first if it is running.

## Options

`deploy.sh` and `update.sh` take the same image options. By default minikube pulls `jchristn77/hugo-server` and `jchristn77/hugo-dashboard` at tag `v0.1.0` (or `HUGO_TAG`) from Docker Hub. `--load` copies them from your local Docker daemon instead, and `--build` builds them from this checkout and then loads them. `--with postgres` swaps SQLite for an in-cluster PostgreSQL, and `--with observability` adds Prometheus, Tempo, Loki, Alloy, and Grafana with the Hugo dashboards; combine them as `--with postgres,observability`.

| Variable | Default | Meaning |
|---|---|---|
| `MINIKUBE_PROFILE` | `hugo` | Profile name, also the kubectl context every script passes explicitly |
| `MINIKUBE_CPUS`, `MINIKUBE_MEMORY` | `4`, `6g` | Size of a new profile (use `8g` with observability for headroom) |
| `MINIKUBE_DRIVER` | minikube's choice | For example `docker`, `hyperkit`, `hyperv` |
| `HUGO_TAG` | `v0.1.0` | Image tag for both Hugo images |

## The ingress route

The overlay also creates an Ingress for `http://hugo.local` on the minikube ingress addon. On Linux, point `hugo.local` at `minikube -p hugo ip` in `/etc/hosts`. On macOS and Windows with the Docker driver the node IP is not reachable, so run `minikube -p hugo tunnel` in another terminal and point `hugo.local` at `127.0.0.1`. The port-forward is simpler and is what the dashboard's Grafana links expect.

## Day two

```bash
minikube/scripts/update.sh          # pull the tag again, re-apply, restart server and dashboard; data is kept
minikube/scripts/factory-reset.sh   # type RESET: delete namespace hugo (data and secrets) and redeploy fresh
minikube/scripts/teardown.sh        # type the profile name: delete the whole minikube profile
```

`update.sh` and `factory-reset.sh` remember which components you deployed. Hugo always runs one server replica (the reason is in `docs/KUBERNETES.md`), so an update is a few seconds of downtime while the new pod starts.

Every script has a `.bat` twin for Windows. Data lives in the `hugo-data` volume inside the minikube node and survives `minikube stop`; it goes away with `factory-reset` or `teardown`.
