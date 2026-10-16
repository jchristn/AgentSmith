<p align="center"><img src="assets/logo.png" alt="Hugo" width="160" /></p>

# Hugo

Hugo is a multi-tenant super agent server. You give it access to the systems that run your production stack (SSH hosts, object storage, databases, log services, caches, cloud control planes, clusters, HTTP APIs), group that access into agent profiles, and reach those profiles from any agent or harness over MCP or A2A, from the REST API and SDKs, from any OpenAI-compatible client, or by chatting in the dashboard.

The default profile is named Hugo. It calls itself Agent Smith, and it calls you Mr. Anderson.

> Hugo is an alpha (`0.1.0-alpha`). It works end to end and is tested against real targets, and it will still change in ways that break things before 1.0. The build plan and progress tracker is [`HUGO_PLAN.md`](HUGO_PLAN.md); what shipped is in [`CHANGELOG.md`](CHANGELOG.md).

## Why it exists

A production stack is a dozen systems with a dozen consoles. When something breaks at 2 AM, the operator becomes the integration layer, hopping between terminals and dashboards and pasting fragments into a chat window. Coding agents such as Claude Code can already reason about all of it. What they lack is safe, consistent, credentialed access: every harness would need its own SSH keys, database passwords, and cloud credentials, and its own idea of which actions are safe. Today that access is scattered across laptops, nobody audits it, and nothing stops the destructive actions.

Hugo holds the access and the judgment about using it. Credentials live encrypted in Hugo, never on the laptop and never in a prompt. Every tool call is classified as Read, Write, or Destructive before it runs, checked against the access mode you chose for that resource on that profile, and sent to a human when it needs one. Everything is recorded.

## What a session looks like

1. Mr. Anderson starts the Docker stack, opens the dashboard, signs in with the seeded account, and picks a new password when Hugo asks.
2. The setup wizard asks for one model endpoint (a local Ollama, or Anthropic, OpenAI, and others with an API key).
3. He stores an SSH key, a PostgreSQL password, and a Papertrail token in the vault, and adds the resources that use them: `web-01`, `prod-db`, `papertrail`. Each form comes from the connector's schema and has a Test Connection button.
4. He binds them to the Hugo profile: `prod-db` ReadOnly, `web-01` ReadWriteWithApproval, `papertrail` ReadOnly. The instructions preview shows Agent Smith's persona followed by one section per resource.
5. He asks, "Agent Smith, why is checkout slow?" Hugo searches Papertrail, looks at `pg_stat_activity` on `prod-db`, runs `uptime` and `df -h` on `web-01`, and answers with evidence, addressing him as Mr. Anderson. Every tool call shows inline with its effect and duration.
6. He asks Hugo to restart nginx. The call is Destructive, an approval card appears, he approves, Hugo runs it and reports back.
7. He creates a credential bound to the Hugo profile and adds Hugo to Claude Code. Claude Code receives the same instructions and a tool list limited to those three resources.
8. Request history, tool invocations, the audit log, and Grafana show everything that happened.

## Deploy with Docker

Hugo ships as two container images, `jchristn77/hugo-server` and `jchristn77/hugo-dashboard` (tag `v0.1.0`), and containers are the only supported way to run it (decision D-045): with Docker Compose (below), on Kubernetes with `k8s/`, or on minikube with `minikube/`. The Kubernetes and minikube paths are described in [`docs/KUBERNETES.md`](docs/KUBERNETES.md); like the Compose stack, they run a single replica.

```bash
git clone https://github.com/jchristn/AgentSmith.git
cd AgentSmith/docker
./start.sh            # Windows: start.bat
```

`start.sh` creates `docker/.env` on first run, generates the master key and the legacy token validation key, and starts the stack. Back up `docker/.env`: losing `HUGO_MASTER_KEY` makes every stored secret unreadable. The stack is Hugo, its dashboard, and an observability stack (Prometheus, Tempo, Loki, Alloy, Grafana). It runs on SQLite by default; set `HUGO_DB_TYPE=Postgresql` in `docker/.env` and start with `docker compose --profile postgres up -d` to use PostgreSQL instead.

| Service | URL |
|---|---|
| Dashboard | http://127.0.0.1:8801 |
| REST, MCP (`/mcp`), A2A (`/v1.0/a2a`), OpenAI-compatible (`/v1.0/openai/v1`) | http://127.0.0.1:8800 |
| Grafana (admin / admin, change it) | http://127.0.0.1:3000 |
| Prometheus | http://127.0.0.1:9090 |

Sign in to the dashboard as `anderson@hugo.local` with the password `password` (tenant `default`). Hugo prompts you to choose a new password; change it right away or skip the prompt for now, but do not leave a published default password on anything reachable from a network. A system administrator, `admin@hugo.local`, is seeded the same way.

Keeping it current and starting over:

```bash
./update.sh               # pull new images and recreate the stack; data is kept
./reset.sh                # delete everything, including stored secrets, and start fresh
```

Every host port binds to `127.0.0.1` and can be changed in `docker/.env`. [`docs/DOCKER.md`](docs/DOCKER.md) covers networking to your own systems (`host.docker.internal`, the Docker socket for the Docker connector), PostgreSQL, and backups, and [`docs/UPGRADING.md`](docs/UPGRADING.md) covers upgrades.

Want to see it working before you wire up your own systems? `docker/factory/` is a separate demo stack (dashboard on port 8811, API on 8810) seeded with a demo profile and real targets to talk to: a PostgreSQL and a MySQL database, an SSH host, MinIO, Redis, Prometheus, and Loki. [`docs/FACTORY.md`](docs/FACTORY.md) explains it.

## Connect Claude Code

Create a credential bound to the profile Claude Code should use (Credentials, then New, in the dashboard; turn on Lock to profile), then:

```bash
claude mcp add --transport http hugo http://127.0.0.1:8800/mcp \
  --header "x-access-key: $HUGO_ACCESS_KEY" \
  --header "x-secret-key: $HUGO_SECRET_KEY"
```

Claude Code receives the profile's compiled instructions in the MCP handshake, so it knows from the first message which systems it can reach and what it may do there. Writes that need approval wait for you in the dashboard. [`docs/MCP_CLIENTS.md`](docs/MCP_CLIENTS.md) has the `.mcp.json` form, the stdio relay (`hugo mcp stdio`) for clients that only launch processes, and settings for other clients. [`MCP_API.md`](MCP_API.md) documents every tool, prompt, and resource.

## How it works

```
  Claude Code, Cursor,     other agents      scripts, SDKs     Open WebUI,        browser
  any MCP client           (A2A)             (REST)            OpenAI clients
        |                     |                  |                  |                |
        | /mcp                | /v1.0/a2a        | /v1.0/api        | /v1.0/openai    | dashboard (nginx, :8801)
        v                     v                  v                  v                v
  +-------------------------------------------------------------------------------------+
  |  hugo-server (:8800)   authentication, tenancy, RBAC, request history, audit         |
  |                                                                                      |
  |  agent profiles --- bindings (alias, access mode, tool rules) --- resources          |
  |        |                                                              |              |
  |  instruction compiler (layered prompt templates)        secrets vault (AES-256-GCM)  |
  |        |                                                              |              |
  |  Hugo's own agent loop (chat, A2A, OpenAI) ---> tool invocation pipeline <--- MCP     |
  |        |                                classify, authorize, approve, limit,         |
  |  model endpoints (Ollama, OpenAI,       execute, scrub, record                       |
  |  Anthropic, Gemini, ...)                          |                                  |
  +---------------------------------------------------|----------------------------------+
                                                      v
                connectors: SSH, S3/Azure/GCS, SQLite/MySQL/PostgreSQL/SQL Server,
                Papertrail, SolarWinds, Redis, AWS, Kubernetes, Docker, HTTP,
                Prometheus, Loki, Elasticsearch, external MCP servers, remote A2A agents
```

Four ideas carry the design.

**Resources and profiles are separate.** A resource is one configured system with its credentials attached by vault slot. A profile is a persona plus a set of bindings, and each binding gives one resource an alias and an access mode. The same `prod-db` can be ReadOnly for the Hugo profile and ReadWriteWithApproval for an on-call profile, and deleting a profile never touches a resource.

**Instructions compose.** Every connector ships instructions that teach an agent how to use it well, and every bound resource contributes its section, its notes, and its durable knowledge to the profile's compiled instructions. An agent connected to a profile with eight resources is told about exactly those eight. All of the text is editable in layers (shipped, global, tenant), each with a reset to default. See [`docs/PROMPTS.md`](docs/PROMPTS.md).

**Every surface is a client of the same profile.** Over MCP, the caller's model drives Hugo's tools. Over A2A, the dashboard, and the OpenAI-compatible endpoint, Hugo's own agent loop drives, on the profile's model endpoint. REST and the SDKs do either. Whatever the surface, every tool call goes through one pipeline.

**The server enforces.** Instructions tell the model the rules; the pipeline enforces them. A model that ignores its instructions gets a tool error, not access.

One server process serves REST, MCP, A2A, and the OpenAI-compatible surface on one port, backed by SQLite or PostgreSQL. [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) goes deeper.

## Connectors

Twenty connector types in fourteen families ship in the image, with 118 tools between them.

| Family | Connectors | A few of the tools |
|---|---|---|
| Hosts | SSH | `ssh_system_info`, `ssh_exec`, `ssh_service_control`, `ssh_tail_file` |
| Object storage | Amazon S3 (and S3-compatible), Azure Blob, Google Cloud Storage | `storage_list`, `storage_read`, `storage_presign_url`, `storage_delete` |
| Databases | SQLite, MySQL, PostgreSQL, SQL Server | `sql_get_agent_pack`, `sql_query`, `sql_activity`, `sql_ask` |
| Logs | Papertrail, SolarWinds Observability | `logs_search`, `logs_tail`, `swo_query_metric` |
| Cache | Redis | `redis_info`, `redis_scan`, `redis_slowlog`, `redis_delete` |
| Cloud | AWS | `aws_ec2_describe_instances`, `aws_logs_insights_query`, `aws_ecs_update_service` |
| Orchestration | Kubernetes, Docker | `k8s_logs`, `k8s_rollout_restart`, `docker_ps`, `docker_container_action` |
| Observability | Prometheus, Loki, Elasticsearch and OpenSearch | `prom_query_range`, `loki_query`, `es_search` |
| APIs | Generic HTTP | `http_health_check`, `http_request` |
| Agents | External MCP servers, remote A2A agents | `mcp_call_tool`, `a2a_delegate` |

The SQL connectors do more than run queries: they crawl the schema, infer relationships, derive what the data likely means, and keep durable context that operators and agents can edit, so an agent understands a database instead of guessing at it. [`docs/CONNECTORS.md`](docs/CONNECTORS.md) explains each connector and its settings, [`docs/MCP_CONNECTOR_TOOLS.md`](docs/MCP_CONNECTOR_TOOLS.md) documents every tool, and [`docs/CONNECTOR_AUTHORING.md`](docs/CONNECTOR_AUTHORING.md) shows how to write a new one.

## Safety model

Every binding has an access mode, and every tool call is classified per call. A SELECT is Read; an UPDATE is Write; a DROP, a `kubectl delete`, or `systemctl restart` is Destructive. Anything Hugo cannot parse is treated as Destructive.

| Effect | ReadOnly | ReadWriteWithApproval | ReadWrite |
|---|---|---|---|
| Read | allowed | allowed | allowed |
| Write | denied | approval | allowed |
| Destructive | denied | approval | approval (unless turned off for the binding) |

Tool rules and deny patterns on a binding tighten that further, a resource can carry a ceiling no binding may exceed, and a tenant administrator can lock the whole tenant down to reads in one click. Approvals reach a human through the dashboard on every surface: chat pauses inline, MCP waits and then hands back an approval ID, A2A tasks move to input-required, and REST answers 202. Secrets never enter a prompt, and a scrubber removes known secret values and credential patterns from every tool output, log line, and history record.

Treat Hugo's classifiers as a second line of defense. Give it read-only database logins, scoped IAM policies, namespace-scoped Kubernetes service accounts, and restricted SSH users wherever the job allows. [`docs/SAFETY_MODEL.md`](docs/SAFETY_MODEL.md) has the full rules and [`docs/SECURITY.md`](docs/SECURITY.md) covers tenancy, authentication, RBAC, and the vault.

## APIs and SDKs

| Surface | Reference |
|---|---|
| REST management API, including the OpenAI-compatible surface | [`REST_API.md`](REST_API.md), and `GET /openapi.json` on a running server |
| MCP | [`MCP_API.md`](MCP_API.md) |
| A2A | [`A2A_API.md`](A2A_API.md) |
| Postman | [`assets/postman/`](assets/postman/): a collection covering every route plus MCP and A2A, and an environment; run it with `scripts/newman.sh` |
| C# | [`sdk/csharp`](sdk/csharp/README.md) (`Hugo.Sdk`) |
| Python | [`sdk/python`](sdk/python/README.md) (`hugo-sdk`) |
| JavaScript and TypeScript | [`sdk/js`](sdk/js/README.md) (`@jchristn/hugo-sdk`) |
| Command line | [`src/Hugo.Cli`](src/Hugo.Cli/README.md) (`hugo login`, `hugo whoami`, `hugo mcp stdio`) |

## Documentation

| Topic | Document |
|---|---|
| Architecture | [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) |
| Every setting and environment variable | [`docs/CONFIGURATION.md`](docs/CONFIGURATION.md) |
| Security: tenancy, authentication, RBAC, the vault, the master key | [`docs/SECURITY.md`](docs/SECURITY.md) |
| Safety model: effects, access modes, approvals, lockdown | [`docs/SAFETY_MODEL.md`](docs/SAFETY_MODEL.md) |
| Prompt templates and instruction composition | [`docs/PROMPTS.md`](docs/PROMPTS.md) |
| Connectors | [`docs/CONNECTORS.md`](docs/CONNECTORS.md), [`docs/connectors/`](docs/connectors/) |
| Telemetry and Grafana | [`docs/TELEMETRY.md`](docs/TELEMETRY.md), [`docs/GRAFANA.md`](docs/GRAFANA.md) |
| Deployment: Docker, Kubernetes and minikube, the factory demo, upgrades | [`docs/DOCKER.md`](docs/DOCKER.md), [`docs/KUBERNETES.md`](docs/KUBERNETES.md), [`docs/FACTORY.md`](docs/FACTORY.md), [`docs/UPGRADING.md`](docs/UPGRADING.md) |
| When something goes wrong | [`docs/TROUBLESHOOTING.md`](docs/TROUBLESHOOTING.md) |
| Dashboard languages | [`docs/I18N.md`](docs/I18N.md) |

## Community

Questions, ideas, and war stories are welcome on Discord: https://discord.gg/tRAN8HgvK5. Bugs and feature requests go to [GitHub issues](https://github.com/jchristn/AgentSmith/issues).

## Kubernetes and minikube

The same images deploy to Kubernetes with `k8s/` (Kustomize base, postgres, observability, and ingress components, k3s and production overlays, a Helm chart, and scripts) and to a laptop with `minikube/`:

```bash
minikube/scripts/start.sh && minikube/scripts/deploy.sh --with observability && minikube/scripts/port-forward.sh
k8s/scripts/deploy.sh k3s --with postgres,observability      # any cluster in your current kubectl context
```

Hugo runs one server replica everywhere. See [`docs/KUBERNETES.md`](docs/KUBERNETES.md) for secrets, updates, factory reset, and backups.

## Developing Hugo

Building, testing, and the connector testbed are covered in [`docs/DEVELOPMENT.md`](docs/DEVELOPMENT.md). Those instructions are for working on Hugo's source; they are not a way to deploy it.

## License

MIT. See [`LICENSE.md`](LICENSE.md).
