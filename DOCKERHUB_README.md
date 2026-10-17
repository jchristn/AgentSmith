<p align="center"><img src="https://raw.githubusercontent.com/jchristn/AgentSmith/main/assets/logo.png" alt="Hugo" width="160" /></p>

# Hugo

Hugo is a multi-tenant super agent server. You give it access to the systems that run your production stack (SSH hosts, object storage, databases, log services, caches, cloud control planes, clusters, HTTP APIs), group that access into agent profiles, and reach those profiles from any agent or harness over MCP or A2A, from the REST API and SDKs, from any OpenAI-compatible client, or by chatting in the dashboard.

The default profile is named Hugo. It calls itself Agent Smith, and it calls you Mr. Anderson.

Source, documentation, and issues: [github.com/jchristn/AgentSmith](https://github.com/jchristn/AgentSmith). This is an alpha (`0.1.0-alpha`): it works end to end, and it will still change in ways that break things before 1.0.

## Images

| Image | Tag | What it is |
|---|---|---|
| [`jchristn77/hugo-server`](https://hub.docker.com/r/jchristn77/hugo-server) | `v0.1.0` | The server: REST, MCP, A2A, and the OpenAI-compatible endpoint on port 8800 |
| [`jchristn77/hugo-dashboard`](https://hub.docker.com/r/jchristn77/hugo-dashboard) | `v0.1.0` | The operator console (nginx on port 8801 in the stack), which proxies API calls to the server |

Both are built for `linux/amd64` and `linux/arm64` and include `curl` for their healthchecks; the server runs as a non-root user (UID 10001). They are configured through environment variables and an optional settings file.

## Why it exists

When something breaks at 2 AM, the operator becomes the integration layer between a dozen consoles. Coding agents such as Claude Code can reason about all of it, but they lack safe, consistent, credentialed access: every harness would need its own SSH keys, database passwords, and cloud credentials, and its own idea of which actions are safe.

Hugo holds the access and the judgment about using it. Credentials live encrypted in Hugo, never on a laptop and never in a prompt. Every tool call is classified as Read, Write, or Destructive before it runs, checked against the access mode you chose for that resource, sent to a human when it needs one, and recorded.

## Quick start

The images are meant to run as a stack: the server, the dashboard, and an observability stack (Prometheus, Tempo, Loki, Alloy, Grafana). The Compose file and its helper scripts live in the repository:

```bash
git clone https://github.com/jchristn/AgentSmith.git
cd AgentSmith/docker
./start.sh            # Windows: start.bat
```

`start.sh` creates `docker/.env`, generates the master key (`HUGO_MASTER_KEY`) and the legacy token validation key (`HUGO_AUTH_SIGNING_KEY`), and starts the stack. Back up `docker/.env`: without the master key, every stored secret is unreadable.

| Service | URL |
|---|---|
| Dashboard | http://127.0.0.1:8801 |
| REST, MCP (`/mcp`), A2A (`/v1.0/a2a`), OpenAI-compatible (`/v1.0/openai/v1`) | http://127.0.0.1:8800 |
| Grafana | http://127.0.0.1:3000 |
| Prometheus | http://127.0.0.1:9090 |

Sign in as `anderson@hugo.local` with the password `password` (tenant `default`) and change the password when Hugo asks. `./update.sh` pulls new images and recreates the stack without touching data; `./reset.sh` deletes everything and starts fresh. The same images run on Kubernetes and minikube with the manifests in `k8s/` and `minikube/` ([KUBERNETES.md](https://github.com/jchristn/AgentSmith/blob/main/docs/KUBERNETES.md)). A seeded demo stack with real targets to talk to lives in `docker/factory/` ([FACTORY.md](https://github.com/jchristn/AgentSmith/blob/main/docs/FACTORY.md)).

## Deployment paths

Hugo supports three container deployment paths. Each runs one server replica; its event hub and approval signals currently live in that process. Running from source is for development.

| Path | Start here | Variants and operations |
|---|---|---|
| Docker Compose | `docker/start.sh` (`start.bat` on Windows), as above; [Docker guide](https://github.com/jchristn/AgentSmith/blob/main/docs/DOCKER.md) | SQLite or the `postgres` profile, bundled observability, updates and resets; the isolated [factory demo](https://github.com/jchristn/AgentSmith/blob/main/docs/FACTORY.md) has its own stack and ports. Host Docker access is an opt-in socket override. |
| Kubernetes | `k8s/scripts/deploy.sh k3s`; [Kubernetes guide](https://github.com/jchristn/AgentSmith/blob/main/docs/KUBERNETES.md) | Kustomize base, k3s and production overlays, PostgreSQL and observability components, or the equivalent `k8s/helm/hugo` Helm chart. Production needs database and TLS secrets first. |
| minikube | `minikube/scripts/start.sh`, then `minikube/scripts/deploy.sh --with observability`; [minikube guide](https://github.com/jchristn/AgentSmith/blob/main/minikube/README.md) | A workstation profile with image pull, load, or build options; SQLite or PostgreSQL, port-forwarding, update, reset, and teardown scripts. |

The Kubernetes and minikube scripts have Windows `.bat` versions. Back up their database volumes together with `hugo-secrets`; Compose needs its database volumes and `docker/.env`. The [deployment testing inventory](https://github.com/jchristn/AgentSmith/blob/main/user-testing/DEPLOYMENT_INVENTORY.md) records the installation paths to cover in simulated user testing.

### Standalone server container

Running the server image on its own is fine for a quick look:

```bash
docker run -d --name hugo -p 127.0.0.1:8800:8800 \
  -e HUGO_REST_HOSTNAME=0.0.0.0 \
  -e HUGO_MASTER_KEY="$(head -c 32 /dev/urandom | base64)" \
  -e HUGO_AUTH_SIGNING_KEY="$(head -c 32 /dev/urandom | base64)" \
  -v hugo-data:/app/data \
  jchristn77/hugo-server:v0.1.0
curl -s http://127.0.0.1:8800/v1.0/api/health
```

`HUGO_REST_HOSTNAME=0.0.0.0` makes the server listen on the container's interface (its default is loopback). Keep the generated master key somewhere safe if you store secrets in that container. The important environment variables are `HUGO_MASTER_KEY`, `HUGO_AUTH_SIGNING_KEY`, `HUGO_DB_TYPE` (`Sqlite` or `Postgresql`) with the `HUGO_DB_*` connection variables, and `HUGO_EXTERNAL_BASE_URL` when Hugo sits behind a proxy. Every setting is listed in [CONFIGURATION.md](https://github.com/jchristn/AgentSmith/blob/main/docs/CONFIGURATION.md).

## Connect Claude Code

Create a credential bound to the profile Claude Code should use (dashboard: Credentials, New, with Lock to profile on), then:

```bash
claude mcp add --transport http hugo http://127.0.0.1:8800/mcp \
  --header "x-access-key: $HUGO_ACCESS_KEY" \
  --header "x-secret-key: $HUGO_SECRET_KEY"
```

Claude Code receives the profile's compiled instructions in the MCP handshake and a tool list limited to that profile's resources. Writes that need approval wait for you in the dashboard. More clients: [MCP_CLIENTS.md](https://github.com/jchristn/AgentSmith/blob/main/docs/MCP_CLIENTS.md).

## How it works

- **Resources and profiles are separate.** A resource is one configured system, with credentials attached from the encrypted vault by slot. A profile is a persona plus bindings, and each binding gives a resource an alias and an access mode. The same database can be ReadOnly for one profile and ReadWriteWithApproval for another.
- **Instructions compose.** Every connector ships instructions that teach an agent how to use it, and each bound resource adds its section, notes, and durable knowledge. All of it is editable in layers with a reset to default.
- **Every surface is a client of the same profile.** Over MCP the caller's model drives Hugo's tools; over A2A, the dashboard, and the OpenAI-compatible endpoint, Hugo's own agent loop drives on the profile's model. Every tool call from every surface goes through one pipeline that classifies, authorizes, approves, limits, executes, scrubs, and records.
- **The server enforces.** A model that ignores its instructions gets a tool error, not access.

Details: [ARCHITECTURE.md](https://github.com/jchristn/AgentSmith/blob/main/docs/ARCHITECTURE.md).

## Connectors

Nineteen connector types and 114 connector-specific tools plus two shared resource context tools ship in the server image: SSH; Amazon S3 (and S3-compatible), Azure Blob, and Google Cloud Storage; SQLite, MySQL, PostgreSQL, and SQL Server, with schema crawling, relationship inference, and durable database context; SolarWinds Observability; Redis; AWS; Kubernetes; Docker; generic HTTP; Prometheus, Loki, and Elasticsearch or OpenSearch; external MCP servers; and remote A2A agents. See [CONNECTORS.md](https://github.com/jchristn/AgentSmith/blob/main/docs/CONNECTORS.md) and [MCP_CONNECTOR_TOOLS.md](https://github.com/jchristn/AgentSmith/blob/main/docs/MCP_CONNECTOR_TOOLS.md).

## Safety model

| Effect | ReadOnly | ReadWriteWithApproval | ReadWrite |
|---|---|---|---|
| Read | allowed | allowed | allowed |
| Write | denied | approval | allowed |
| Destructive | denied | approval | approval (unless turned off for the binding) |

Every call is classified per call: a SELECT is Read, an UPDATE is Write, a DROP or `systemctl restart` is Destructive, and anything Hugo cannot parse is Destructive. Tool rules, deny patterns, resource ceilings, and a tenant-wide lockdown tighten it further. Secrets never enter a prompt, and a scrubber removes them from every output, log, and history record. Treat the classifiers as a second line of defense and give Hugo least-privilege logins upstream. See [SAFETY_MODEL.md](https://github.com/jchristn/AgentSmith/blob/main/docs/SAFETY_MODEL.md) and [SECURITY.md](https://github.com/jchristn/AgentSmith/blob/main/docs/SECURITY.md).

## Documentation

- APIs: [REST_API.md](https://github.com/jchristn/AgentSmith/blob/main/REST_API.md), [MCP_API.md](https://github.com/jchristn/AgentSmith/blob/main/MCP_API.md), [A2A_API.md](https://github.com/jchristn/AgentSmith/blob/main/A2A_API.md), and a [Postman collection](https://github.com/jchristn/AgentSmith/tree/main/assets/postman).
- SDKs for [C#](https://github.com/jchristn/AgentSmith/tree/main/sdk/csharp), [Python](https://github.com/jchristn/AgentSmith/tree/main/sdk/python), and [JavaScript and TypeScript](https://github.com/jchristn/AgentSmith/tree/main/sdk/js).
- Operating it: [DOCKER.md](https://github.com/jchristn/AgentSmith/blob/main/docs/DOCKER.md), [UPGRADING.md](https://github.com/jchristn/AgentSmith/blob/main/docs/UPGRADING.md), [TROUBLESHOOTING.md](https://github.com/jchristn/AgentSmith/blob/main/docs/TROUBLESHOOTING.md), [TELEMETRY.md](https://github.com/jchristn/AgentSmith/blob/main/docs/TELEMETRY.md).
- Changes: [CHANGELOG.md](https://github.com/jchristn/AgentSmith/blob/main/CHANGELOG.md).

## Community

Questions and ideas are welcome on Discord: https://discord.gg/tRAN8HgvK5. Bugs and feature requests go to [GitHub issues](https://github.com/jchristn/AgentSmith/issues).

## License

MIT. See [LICENSE.md](https://github.com/jchristn/AgentSmith/blob/main/LICENSE.md).
