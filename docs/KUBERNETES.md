# Running Hugo on Kubernetes and minikube

Hugo ships as two images, `jchristn77/hugo-server` and `jchristn77/hugo-dashboard`, built for `linux/amd64` and `linux/arm64`. Docker Compose (`docker/`, see [`DOCKER.md`](DOCKER.md)) runs them on one machine. This document covers the other two supported ways to run the same images: a Kubernetes cluster, through `k8s/`, and minikube on a workstation, through `minikube/`. Running from source is for working on Hugo and is not a deployment method ([`DEVELOPMENT.md`](DEVELOPMENT.md)).

The Kubernetes assets follow the Compose stack closely. The settings file, the environment variables, the health endpoints, the observability images, the Grafana provisioning, and the datasource UIDs are all the same, so anything you learned from Compose carries over. What changes is how keys are stored (a Kubernetes Secret instead of `docker/.env`), how data is kept (PersistentVolumeClaims instead of named volumes), and how you reach the stack (a port-forward or an Ingress instead of published ports).

## One replica, on purpose

Before anything else: Hugo runs as exactly one server replica on every runtime, and the manifests enforce it. The event hub that feeds the dashboard's live updates and the signals that wake a tool call when its approval is granted live in the server process (decisions D-027 and D-045 in `HUGO_PLAN.md`). A second replica would not see events or approvals raised on the first, and calls waiting on an approval would hang until they expire. The state that matters (approvals, conversations, A2A tasks) is in the database, so a restart loses nothing, but the in-process signals are not shared yet.

So the server Deployment has `replicas: 1` and the `Recreate` strategy. Recreate stops the old pod before the new one starts, which also lets the ReadWriteOnce data volume move from one to the other. An upgrade or a node drain therefore takes Hugo offline for the few seconds the new pod needs to pass its readiness probe. The Helm chart refuses to render with `server.replicas` set to anything but 1, and there is no autoscaler. The dashboard is a different story: it is stateless nginx, so production runs two of it.

## What is where

| Path | What it is |
|---|---|
| `k8s/base` | The server Deployment and Service, the dashboard Deployment and Service, the `hugo-data` PersistentVolumeClaim for `/app/data`, the settings ConfigMap (`config/hugo.json`), service accounts, and network policies. SQLite is the database. No secrets. |
| `k8s/components/postgres` | An in-cluster PostgreSQL 16 StatefulSet with its own volume, plus a server patch that switches to PostgreSQL and waits for it. |
| `k8s/components/observability` | Prometheus, Tempo, Loki, Alloy, and Grafana with the Hugo dashboards in folder Hugo, the same as Compose. |
| `k8s/components/ingress` | One Ingress for the dashboard and the API (MCP and A2A included) with Server-Sent Events settings. |
| `k8s/overlays/k3s` | Base plus the Ingress on Traefik, the `local-path` storage class, and `IfNotPresent` pulls for imported images. |
| `k8s/overlays/production` | Base plus an external PostgreSQL, larger requests and limits, disruption budgets, topology spread, two dashboard replicas, and TLS on an ingress-nginx Ingress. |
| `k8s/helm/hugo` | A Helm chart that renders the same objects as the base and components, with values files for k3s, minikube, and production. |
| `k8s/scripts` | `generate-secrets`, `deploy`, `port-forward`, `update`, `factory-reset`, `wait-rollout`, `smoke-test`, `import-images`, and `sync-mirrors`, each as `.sh` and `.bat`. |
| `k8s/tests` | `check-render.sh`, which renders every shape, validates it, and compares Kustomize with Helm. CI runs it on every push. |
| `minikube/overlay` | Base plus the Ingress on the minikube ingress addon, the `standard` storage class, and `IfNotPresent` pulls. |
| `minikube/scripts` | `start`, `deploy`, `port-forward`, `update`, `factory-reset`, `teardown`, and `smoke-test`, each as `.sh` and `.bat`. |

Everything goes into namespace `hugo`. The namespace name is part of one URL (the dashboard's upstream, `hugo-server.hugo.svc.cluster.local`), so if you change it, change that too, or set `dashboard.apiUpstream` in Helm.

## Choosing a runtime

| Runtime | Pick it when | You need |
|---|---|---|
| minikube | You want the whole product on a laptop with one command and no cluster of your own. | minikube, `kubectl`, `openssl`, and 4 CPUs with 6 GB for the profile (8 GB is comfortable with observability) |
| k3s, k3d, or k3s in Docker | You want a real, small cluster: an edge box, a VM, or k3d on macOS and Windows. | `kubectl` pointed at it and `openssl`; Docker and k3d (or `sudo k3s ctr`) only to import local images |
| Production overlay or Helm | A shared cluster with a managed PostgreSQL, an ingress controller, and TLS. | `kubectl` (and `helm` for the chart), the published images or your registry |
| Docker Compose | One machine, no Kubernetes. | Docker ([`DOCKER.md`](DOCKER.md)) |

Helm is optional everywhere. The scripts use Kustomize, which is built into `kubectl`, and CI proves the chart and the Kustomize trees render the same objects. Pick Helm when your cluster is already managed with it.

## Secrets come first

Nothing secret lives in the repository. Before the first deploy, `k8s/scripts/generate-secrets.sh` (or `.bat`) creates Secret `hugo-secrets` in namespace `hugo` with random values. The deploy scripts call it for you, so you only run it by hand when you deploy with plain `kubectl` or Helm.

| Key | Becomes | What it does |
|---|---|---|
| `master-key` | `HUGO_MASTER_KEY` | 32 random bytes, base64. Encrypts every credential Hugo stores. |
| `signing-key` | `HUGO_AUTH_SIGNING_KEY` | Signs session tokens. Rotating it signs everyone out. |
| `db-password` | `HUGO_DB_PASSWORD`, `POSTGRES_PASSWORD` | Used only with `components/postgres`. |
| `grafana-admin-password` | `GF_SECURITY_ADMIN_PASSWORD` | Used only with `components/observability`. Set `HUGO_GRAFANA_PASSWORD` to choose it; otherwise it is random and printed once. |

The script never overwrites an existing `hugo-secrets`, because a new master key cannot decrypt credentials encrypted with the old one. `--force` replaces it anyway, which you want only when you are also starting from an empty database. The master key deserves the same care as the database itself, so the script tells you to export a copy:

```bash
kubectl -n hugo get secret hugo-secrets -o yaml > hugo-secrets.backup.yaml   # store it outside the cluster and the repository
```

The seeded accounts do not need a secret of their own. On first boot the server creates tenant `default`, the system administrator `admin@hugo.local`, and the tenant administrator `anderson@hugo.local` (Thomas Anderson), both with password `password`. Hugo refuses every other route until the password is changed, so the default never survives the first sign-in.

## minikube

`minikube/` is a self-contained path for evaluators and developers. Four commands take you from nothing to a running Hugo:

```bash
minikube/scripts/start.sh                              # profile "hugo": 4 CPUs, 6 GB, ingress and metrics-server addons
minikube/scripts/deploy.sh --with observability        # pull the images, create secrets, apply, wait
minikube/scripts/smoke-test.sh                         # health, sign-in, password change, connectors, instructions, Grafana
minikube/scripts/port-forward.sh                       # dashboard :8801, API :8800, Grafana :3000
```

`deploy.sh` pulls the published images from Docker Hub by default. `--load` copies `jchristn77/hugo-server` and `jchristn77/hugo-dashboard` from your local Docker daemon instead (useful right after `./build-all.sh`), and `--build` builds both from your checkout first. `--with postgres` runs PostgreSQL instead of SQLite; `--with postgres,observability` gives you the full Compose-equivalent stack. Profile, CPUs, memory, driver, and image tag come from `MINIKUBE_PROFILE`, `MINIKUBE_CPUS`, `MINIKUBE_MEMORY`, `MINIKUBE_DRIVER`, and `HUGO_TAG`.

The scripts always pass the profile as the kubectl context, so they work no matter what your current context is. `minikube start` itself makes the profile your current context; switch back with `kubectl config use-context` if you need to. [`minikube/README.md`](../minikube/README.md) has the details, including the ingress route to `http://hugo.local`.

## k3s

`k8s/overlays/k3s` targets k3s as it comes out of the box: Traefik serves the Ingress for `http://hugo.local`, and the local-path provisioner backs the volumes. It works the same on a k3s host, under k3d, and with `rancher/k3s` in a container.

```bash
k8s/scripts/deploy.sh k3s                                   # SQLite
k8s/scripts/deploy.sh k3s --with postgres,observability     # PostgreSQL and the observability stack
```

The overlay sets `imagePullPolicy: IfNotPresent` so images you import are used as they are. Without an import the cluster pulls from Docker Hub on first use. To import what your local Docker has, add `--import` and say where k3s is:

```bash
k3d cluster create hugo -p "127.0.0.1:8080:80@loadbalancer"
docker pull jchristn77/hugo-server:v0.1.0 && docker pull jchristn77/hugo-dashboard:v0.1.0
K3D_CLUSTER=hugo k8s/scripts/deploy.sh k3s --import         # or K3S_CONTAINER=<container>, or neither for sudo k3s ctr
```

For k3s in plain Docker, the testbed in `docker/testbed/kubernetes/compose.yaml` shows the flags a privileged `rancher/k3s` container needs. Add `127.0.0.1 hugo.local` to `/etc/hosts` (use the node IP for a remote k3s) and open `http://hugo.local`, or `http://hugo.local:8080` with the k3d port mapping above.

## Production

`k8s/overlays/production` is a starting point to copy, not something to apply blind. It expects two Secrets besides `hugo-secrets`: `hugo-db` for the external PostgreSQL and `hugo-tls` for the Ingress certificate.

```bash
kubectl -n hugo create secret generic hugo-db \
  --from-literal=host=db.example.com --from-literal=port=5432 \
  --from-literal=database=hugo --from-literal=username=hugo --from-literal=password='...'
k8s/scripts/deploy.sh production
```

Replace `hugo.example.com` in `ingress-tls.yaml` and in `server.yaml` (`HUGO_EXTERNAL_BASE_URL`, which A2A agent cards advertise). The server waits in its init container until PostgreSQL answers `pg_isready`, then creates or migrates its schema on first boot. Resource requests are 500m CPU and 512 MiB for the server, with limits of 4 CPUs and 2 GiB; size them for your tool traffic. Images use `imagePullPolicy: Always` here, so pin a tag and treat a re-pushed tag as an upgrade.

The disruption budget for the server is `maxUnavailable: 1`. That sounds like no protection at all, and with one replica it mostly is: a drain always evicts Hugo, and it comes back on another node. A `minAvailable: 1` budget would block every drain forever, which is worse. Plan drains for a quiet moment. Open approvals survive because they are stored, but in-flight chat streams and MCP sessions reconnect.

## Helm

The chart in `k8s/helm/hugo` renders the same objects as the Kustomize trees. The values files mirror the overlays, and two switches add the components:

```bash
k8s/scripts/generate-secrets.sh
helm install hugo k8s/helm/hugo -n hugo -f k8s/helm/hugo/values-k3s.yaml \
  --set database.mode=embedded --set observability.enabled=true
```

`values.yaml` documents every value. `database.mode` is `sqlite`, `embedded` (the postgres component), or `external` (the production overlay). `observability.enabled` adds the stack. `ingress.enabled`, `ingress.className`, and `ingress.host` place the Ingress; with class `nginx` the chart adds the streaming annotations itself. The chart does not create the namespace or the Secret: use `--create-namespace` or run `generate-secrets` first.

## Reaching Hugo

The quickest way in is `k8s/scripts/port-forward.sh` (or `minikube/scripts/port-forward.sh`). It forwards the same local ports as Docker Compose, so bookmarks, MCP client configuration, and the Grafana links on the dashboard all work unchanged.

| What | Local URL |
|---|---|
| Dashboard | http://127.0.0.1:8801 |
| REST, MCP (`/mcp`), A2A (`/v1.0/a2a`) | http://127.0.0.1:8800 |
| Grafana (user `admin`, password in `hugo-secrets`) | http://127.0.0.1:3000 |
| Prometheus, Loki, Tempo | http://127.0.0.1:9090, :3100, :3200 |

Stop Docker Compose first if it is running, because it holds the same ports; or pick others with `REST_PORT`, `DASHBOARD_PORT`, `GRAFANA_PORT`, and friends.

With the Ingress, one host serves everything. `/v1.0`, `/mcp`, and `/openapi.json` go straight to the server and everything else to the dashboard. Chat, the event feed, and MCP streamable HTTP are Server-Sent Events, so on ingress-nginx the component turns response buffering off and allows one-hour reads. Traefik streams without buffering by default and needs nothing extra. `/metrics` is never routed.

## Health and startup order

The probes are the Kubernetes form of the Compose healthchecks. Hugo's own images carry `curl`, and every probe runs `curl` against `127.0.0.1` inside the container every 5 seconds: two failures take the pod out of service (readiness) or restart it (liveness), and two successes bring it back. The server's readiness probe is `/v1.0/api/health`, which also checks the database and the schema, and its liveness probe is `/v1.0/api/health/live`, which only checks that the process answers. A startup probe gives the first boot up to five minutes for schema creation and seeding, the counterpart of Compose's `start_period`; liveness and readiness take over once it passes.

Prometheus, Tempo, Loki, Alloy, and Grafana have no `curl` in their images, so they use kubelet HTTP probes (TCP for Loki and Tempo liveness) on the same 5 second period and threshold of 2. PostgreSQL uses `pg_isready`.

Startup order works through init containers. With PostgreSQL, the server pod starts with `wait-for-database`, which loops on `pg_isready` until the database accepts connections; Compose expresses the same thing with `depends_on: condition: service_healthy`. Nothing else needs ordering: the dashboard retries its upstream on each request, and the server exports traces asynchronously, so it starts fine before Alloy.

## Observability

`components/observability` deploys what Compose deploys, with the same pinned images: Prometheus `v3.5.4`, Tempo `2.6.1`, Loki `3.5.5`, Alloy `v1.10.2`, and Grafana `12.1.1`. Loki, Tempo, Grafana provisioning, and the datasources (UIDs `prometheus`, `tempo`, `loki`) are byte-for-byte copies of the files in `docker/observability`, and the dashboards are copies of `assets/grafana`. Two files differ because Kubernetes has no Docker socket: Prometheus finds the server pod through the Kubernetes API and scrapes `/metrics` on port 8800, keeping the job name `hugo-server` the dashboards expect, and Alloy reads pod logs through the API with the same `app`, `component`, `node`, `level`, and `tenant` labels the Compose Alloy produces. Both use namespaced Roles, not cluster-wide permissions.

The server exports traces to `hugo-alloy:4317` when the component is present. Without it, OTLP export is off in `k8s/base/config/hugo.json`, so the server does not try to reach a collector that is not there. The dashboard's external services card links to Grafana and the other tools at the port-forward URLs; patch `HUGO_GRAFANA_URL` and its siblings (or the Helm `observability.urls`) when you expose them another way. Grafana keeps no state of its own, since everything is provisioned, so it runs on an emptyDir.

## Network policies

The base allows the server's port 8800 from anywhere, because the dashboard, an ingress controller in another namespace, and Prometheus all need it. Server egress is open except the cloud metadata address `169.254.169.254`: Hugo connects to whatever systems you configure, and it must not hand those requests the node's cloud identity. The dashboard may only resolve DNS and call the server. PostgreSQL admits only the server, and Prometheus, Loki, Tempo, and Alloy admit only pods in the namespace. These policies work on clusters whose network plugin enforces them, k3s included; minikube's default plugin ignores them.

## Configuration

The server reads `/app/config/hugo.json` from the `hugo-server-settings` ConfigMap, generated from `k8s/base/config/hugo.json`, and then applies `HUGO_*` environment variables, exactly as in Compose. The ConfigMap is read-only and `HUGO_SETTINGS_READONLY` is set, so the dashboard's Settings page shows the values but cannot save them to the file. Change the file (or patch it in your overlay) and run `update`. Kustomize renames the generated ConfigMap when its content changes, and the Helm chart adds a checksum annotation, so either way the pod restarts with the new settings.

The file differs from `docker/hugo/hugo.json` in two places: OTLP export is off until the observability component turns it on, and `SettingsFileReadOnly` is true.

## Updating

```bash
k8s/scripts/update.sh                 # minikube: minikube/scripts/update.sh
```

`update` is the Kubernetes counterpart of `docker/update.sh` and never touches data. It re-applies the same overlay and components you deployed (the deploy scripts record them as annotations on the namespace), restarts the server and the dashboard, waits for the rollout, and prints pod status. How the new image arrives depends on the pull policy. The base and production use `Always`, so the restart itself pulls the tag again. minikube's `update.sh` pulls the tag into minikube first (`--load` and `--build` work as in deploy). On k3s, pass `--import` to pull locally and import, or move to a new tag with `HUGO_TAG=v0.1.1`.

Because the server uses Recreate, an update is a short outage, not a rolling one. The PersistentVolumeClaims and `hugo-secrets` stay as they are, so users, credentials, profiles, and history are all still there afterwards.

## Factory reset

```bash
k8s/scripts/factory-reset.sh          # minikube: minikube/scripts/factory-reset.sh
```

This is the counterpart of `docker/factory/reset.sh`, and it is destructive. After you type `RESET`, it deletes namespace `hugo`. That removes every PersistentVolumeClaim (the SQLite or PostgreSQL data, Prometheus, Loki, and Tempo) and `hugo-secrets`, then it creates new secrets and redeploys the same shape. You are back to the seeded accounts with password `password`. An external database (production) is outside the namespace and is not touched; drop it yourself if you mean to start over.

`minikube/scripts/teardown.sh` goes further and deletes the whole minikube profile.

## Back up

Back up two things together: the data and `hugo-secrets`. One without the other is not a backup. The data without the master key is unreadable for every stored credential, and the key without the data protects nothing.

With SQLite, the data is the `hugo-data` volume. Copy the database while the server is stopped, or use your storage's snapshot feature:

```bash
kubectl -n hugo scale deployment/hugo-server --replicas=0
kubectl -n hugo run hugo-backup --rm -i --restart=Never --image=busybox:1.36 \
  --overrides='{"spec":{"volumes":[{"name":"d","persistentVolumeClaim":{"claimName":"hugo-data"}}],"containers":[{"name":"b","image":"busybox:1.36","command":["cat","/d/hugo.db"],"volumeMounts":[{"name":"d","mountPath":"/d"}]}]}}' > hugo.db
kubectl -n hugo scale deployment/hugo-server --replicas=1
kubectl -n hugo get secret hugo-secrets -o yaml > hugo-secrets.backup.yaml
```

With in-cluster PostgreSQL, use `pg_dump` from the StatefulSet instead (`kubectl -n hugo exec statefulset/hugo-postgres -- pg_dump -U hugo hugo > hugo.sql`); the server can keep running. With an external database, use its own backups. In every case, keep the Secret export next to the data export and protect both.

## Checking that it works

`k8s/scripts/smoke-test.sh` (and `minikube/scripts/smoke-test.sh`) checks a deployment from your machine through its own temporary port-forwards, so it does not collide with `port-forward.sh`. It verifies that the server runs one replica with Recreate, that health answers on the server and through the dashboard, that the console loads, that `anderson@hugo.local` can sign in and is forced to change the password on a fresh install, that the connector catalog lists connectors, and that the compiled instructions of profile Hugo introduce Agent Smith. With observability it also checks that Grafana's Hugo folder holds every Hugo dashboard and that Prometheus is scraping the server.

On a fresh install the test changes Mr. Anderson's password to a random one and prints it once; set `HUGO_SMOKE_NEW_PASSWORD` to choose it, and `HUGO_SMOKE_PASSWORD` to tell later runs the current one.

`k8s/tests/check-render.sh` is the static counterpart that CI runs on every push (`.github/workflows/k8s.yaml`). It renders every overlay with and without the components, renders the chart with the matching values, validates everything with kubeconform, verifies the mirrored files, and fails when Kustomize and Helm disagree about any object.

## Keeping the copies in step

Kustomize and Helm can only read files inside their own directories, so a few files exist twice. The sources are `assets/grafana` (dashboards), `docker/observability` (Loki, Tempo, Grafana provisioning), `k8s/base/config/hugo.json`, and `k8s/components/observability/config`. After changing any of them, run `k8s/scripts/sync-mirrors.sh`. When you add or remove a dashboard, also update the ConfigMap generator list in `k8s/components/observability/kustomization.yaml` and the projected volume in `grafana.yaml`; the chart picks dashboards up by itself. `check-render.sh` fails until the copies match.
