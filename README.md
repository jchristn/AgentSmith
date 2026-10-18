<p align="center"><img src="assets/logo.png" alt="Hugo" width="160" /></p>

# Hugo

Hugo is a multi-tenant agent server for operating your systems through chat and tools. Connect SSH hosts, databases, object storage, logs, cloud services and other agents once, then give each agent profile its own instructions, resources and access limits. Use the dashboard, publish a standalone chat, or connect through MCP, A2A, REST, SDKs and an OpenAI-compatible API.

The default profile is named Hugo. Its persona calls itself Agent Smith and addresses you as Mr. Anderson.

**Status:** `0.1.0-alpha`. Breaking changes are still possible before 1.0. See the [changelog](CHANGELOG.md) for shipped features, the [archived roadmap](archive/HUGO_PLAN.md) for decisions and remaining work, and the [completed document-context plan](ADDITIONAL_PROMPT_CONTEXT.md) for that feature's implementation and verification.

## Contents

- [Quick start](#quick-start)
- [Agents, instructions and memory](#agents-instructions-and-memory)
- [Chat and the dashboard](#chat-and-the-dashboard)
- [Connect clients and applications](#connect-clients-and-applications)
- [Connectors](#connectors)
- [Architecture and access controls](#architecture-and-access-controls)
- [Deployment and operations](#deployment-and-operations)
- [Documentation and development](#documentation-and-development)

## Quick start

Install Docker with Docker Compose, then start the published stack:

```bash
git clone https://github.com/jchristn/AgentSmith.git
cd AgentSmith/docker
./start.sh            # Windows: start.bat
```

The helper creates `docker/.env`, generates missing keys and starts the server, dashboard and observability services. Back up `.env` together with the database: losing `HUGO_MASTER_KEY` makes stored vault secrets unreadable. The default database is SQLite.

| Open | Default URL | First sign-in |
|---|---|---|
| Dashboard | http://127.0.0.1:8801 | Tenant `default`, `anderson@hugo.local`, password `password` |
| Server API | http://127.0.0.1:8800 | Authenticate through a client or the dashboard |
| Grafana | http://127.0.0.1:3000 | `admin` / `admin` |
| Prometheus | http://127.0.0.1:9090 | No sign-in in the local stack |

The tenant administrator is `anderson@hugo.local`; the system administrator is `admin@hugo.local`, also initially using `password`. Change both seeded passwords before exposing the stack to other users. Published host ports bind to `127.0.0.1` by default and can be changed in `.env`.

### Set up your first agent

The dashboard's setup wizard can guide the initial configuration. The same controls remain available afterward:

1. **Connections > Models:** add an inference endpoint, such as Ollama or a supported hosted provider. Use a tool-capable model for agent tool calls and Reference documents.
2. **Connections > Secrets:** create the credentials your systems require. Secrets can contain named fields, including an access-key pair or AES key material.
3. **Connections > Resources:** choose a connector, configure its target, bind the appropriate secret and run **Test Connection**.
4. **Agents > your agent:** select its model and attach resource bindings with aliases and access modes. Add working instructions or documents in its **Instructions** tab, then inspect the compiled preview.
5. Talk to Agent Smith. Tool calls show inline with their effect, and risky ones wait for your approval.

For example, bind `prod-db` as ReadOnly, `web-01` as ReadWriteWithApproval and `checkout-logs` as ReadOnly. Ask why checkout is slow: the agent can inspect the database, search logs and check the host using those bindings. A restart that needs approval pauses for your decision.

For an isolated demonstration with ready-made targets, use the [factory stack](docs/FACTORY.md) in `docker/factory/`. It has separate data and ports, including dashboard `8811` and API `8810`.

## Agents, instructions and memory

An agent profile combines a persona, an inference endpoint and resource bindings. A resource can be shared by several profiles with different aliases or permissions. The agent's working rules, authored reference material and learned resource facts have separate homes:

| Context | What belongs there | How it is managed |
|---|---|---|
| Agent instructions | Workflow rules and preferences, such as how to look up a user identifier | Edit the profile or ask the agent to remember a rule; `agent_get_instructions` and `agent_update_instructions` apply profile permissions and version checks |
| Prompt documents | Reusable runbooks, log formats, schemas and detailed procedures | Create under **Instructions > Documents**, then attach in an agent's **Instructions** tab |
| Resource context | Discovered paths, services, tables, relationships and operational facts about a connection | Agents use `resource_get_context` and `resource_update_context`; operators can review and curate the stored notes |
| Prompt templates | Shared persona, connector guidance and instruction composition | Manage shipped/global/tenant template layers under **Instructions** |

### Inline and Reference documents

Upload a UTF-8 Markdown or text file, or enter its content directly. Hugo stores a versioned database copy that can be attached to several agents. Choose **Latest** to follow changes or **Pinned** to retain a specific revision.

| Delivery mode | What the model receives |
|---|---|
| **Inline** | The full selected document in its system context |
| **Reference** | A short catalog entry with the filename, description, ID and revision; the model requests the body through `agent_read_document` |

For example, attach `LOG_MESSAGE_STRUCTURE.md` with a description explaining when to consult it. The agent can list its attachments with `agent_list_documents`, retrieve the selected document and follow continuation cursors when it needs more than one chunk. Read access is scoped to the current profile's authorized attachments. Document content is delivered literally; credential-shaped examples do not make the document unreadable.

Reference mode reduces the initial prompt size, but retrieved content still uses model context and may remain in conversation history. Models decide when to request it. An uploaded file is a managed copy, not a live filesystem mount or a URL that Hugo automatically synchronizes. See [Agent prompt documents](docs/AGENT_DOCUMENTS.md) for sharing, permissions, public eligibility and limits.

### Learning across conversations

Hugo reloads authorized resource context before hosted model calls. External MCP clients receive compiled guidance and tools to fetch it themselves. Agents explicitly record reusable discoveries rather than automatically saving every tool result. Their instructions encourage concise facts, consolidation and deduplication; operators can correct the notes.

Ask an agent to remember a workflow when it should affect that agent's future work. Save connection-specific discoveries in resource context, and keep longer authored guidance in documents. See [resource context](docs/RESOURCE_CONTEXT.md) and [prompt composition](docs/PROMPTS.md) for the storage, permissions and context budgets.

## Chat and the dashboard

The dashboard groups its controls into nine areas: **Home**, **Chat**, **Agents**, **Connections**, **Instructions**, **Activity**, **Developer Tools**, **Access** and **System**. Related tools live on tabs, including Secrets before Resources in Connections. The interface is translated into 12 languages and supports light and dark themes.

**Dashboard chat** streams Markdown responses, shows tool activity and approval requests, and accepts per-answer ratings and written feedback. Accepted turns continue if you navigate away or disconnect. Return to the conversation to see persisted progress and the answer; **Stop** explicitly cancels. A server restart interrupts active work, so background completion is not a durable job queue.

**Standalone chat** gives an agent its own `/chat/{linkId}` page. Under **Agents > your agent > Connect**, enable the link, choose **Authenticated** or **Public**, and configure its heading, logo and browser title. Authenticated visitors use their own execution permissions. Public links run under an explicitly selected scoped credential with isolated visitor sessions. Links can be disabled or rotated; public document access is separately opt-in. See [Standalone agent chat](docs/STANDALONE_CHAT.md).

**Activity > Feedback** lets an operator select an agent and review feedback with the original request, rated response and stored response telemetry. Other Activity tabs cover invocations, requests, analytics and audit events. Grafana adds metrics, logs and traces for operational diagnosis.

**Developer Tools** contains the REST, MCP and A2A explorers. MCP Explorer exposes the initialize instructions in a collapsed section, builds tool argument forms and shows structured results with raw exchanges available. A2A Explorer lays out the agent card, advertised skills and examples; selecting one prepares a message for review. JSON message parameters remain editable.

## Connect clients and applications

Hugo serves its APIs on the same server port. An MCP client uses its own model to choose tools. Dashboard chat, standalone chat, A2A and the OpenAI-compatible agent endpoint use Hugo's hosted loop and the profile's inference endpoint.

| Interface | Path or client | Reference |
|---|---|---|
| MCP over Streamable HTTP | `/mcp` or `/mcp/{profileName}` | [Client setup](docs/MCP_CLIENTS.md), [MCP API](MCP_API.md) |
| A2A | `/v1.0/a2a/{tenantId}/{profileName}` | [A2A API](A2A_API.md) |
| REST management and execution | `/v1.0/api/...` | [REST API](REST_API.md), live `GET /openapi.json` |
| OpenAI-compatible agent API | `/v1.0/openai/v1` | [Agent endpoint and client examples](REST_API.md#openai-compatible-surface) |
| C# SDK | `Hugo.Sdk` | [C# guide and examples](sdk/csharp/README.md) |
| Python SDK | `hugo-sdk` | [Python guide and examples](sdk/python/README.md) |
| JavaScript / TypeScript SDK | `@jchristn/hugo-sdk` | [JavaScript guide and examples](sdk/js/README.md) |
| CLI and stdio MCP relay | `hugo login`, `hugo profiles list`, `hugo mcp stdio` | [CLI guide](src/Hugo.Cli/README.md) |
| Postman | Collection and environment in `assets/postman/` | [Collection](assets/postman/Hugo.postman_collection.json), [environment](assets/postman/Hugo.postman_environment.json), [Newman runner](scripts/newman.sh) |

### Connect an MCP client

In **Access > Credentials**, create a credential for the profile the client should use, configure its grants and enable **Lock to profile**. A locked credential cannot switch to another profile. Set `HUGO_ACCESS_KEY` and `HUGO_SECRET_KEY` in your local environment using that credential's values, then add Hugo to Claude Code:

```bash
claude mcp add --transport http hugo http://127.0.0.1:8800/mcp \
  --header "x-access-key: $HUGO_ACCESS_KEY" \
  --header "x-secret-key: $HUGO_SECRET_KEY"
```

The client receives the selected profile's compiled instructions, resource-scoped tool definitions and document catalog. Approval-gated operations wait for an operator in Hugo. Clients that launch only stdio processes can use `hugo mcp stdio`; the [MCP client guide](docs/MCP_CLIENTS.md) covers that relay, `.mcp.json`, session tokens and profile selection.

## Connectors

Connector schemas drive resource forms, secret slots, connection tests and tool definitions. Resources supply the target settings; profile bindings decide which aliases and operations a caller can use.

| Family | Systems and capabilities | Example tools |
|---|---|---|
| Hosts | SSH, shell commands, SFTP and services | `ssh_system_info`, `ssh_exec`, `ssh_service_control` |
| Cryptography | Vault-managed AES-GCM/AES-CBC, SHA digests and hash verification | `crypto_encrypt`, `crypto_decrypt`, `crypto_hash`, `crypto_verify_hash` |
| Object storage | Amazon S3 and S3-compatible services, Azure Blob, Google Cloud Storage | `storage_list`, `storage_read`, `storage_presign_url` |
| Databases | SQLite, MySQL, PostgreSQL, SQL Server | `sql_get_agent_pack`, `sql_query`, `sql_activity`, `sql_ask` |
| Logs | SolarWinds Observability | `logs_search`, `logs_tail`, `swo_query_metric` |
| Cache | Redis | `redis_info`, `redis_scan`, `redis_slowlog` |
| Cloud | AWS | `aws_ec2_describe_instances`, `aws_logs_insights_query` |
| Orchestration | Kubernetes and Docker | `k8s_logs`, `k8s_rollout_restart`, `docker_ps` |
| Observability | Prometheus, Loki, Elasticsearch / OpenSearch | `prom_query_range`, `loki_query`, `es_search` |
| APIs | HTTP APIs with configured request limits | `http_health_check`, `http_request` |
| Agents | External MCP servers and remote A2A agents | `mcp_call_tool`, `a2a_delegate` |

SQL tools can inspect schemas, infer relationships and maintain durable database context. Cryptography tools use bound key material to transform data without requiring the model to retrieve the keys; an AES secret can hold key, salt and IV with explicit encodings. External MCP and A2A resources let an agent delegate to other tool servers or agents.

See the [connector catalog](docs/CONNECTORS.md), [tool reference](docs/MCP_CONNECTOR_TOOLS.md) and [cryptography guide](docs/connectors/cryptography.md). New connectors can be added through the [connector authoring interface](docs/CONNECTOR_AUTHORING.md).

## Architecture and access controls

One server process serves REST, MCP, A2A and the OpenAI-compatible API, backed by SQLite or PostgreSQL. The dashboard is a separate nginx container that serves the web UI and proxies requests. The hosted agent loop calls configured model endpoints; external MCP clients run their own loop.

| Component | Responsibility |
|---|---|
| Resource | A configured connection point and its vault secret bindings |
| Agent profile | Persona, model selection, instructions, documents and enabled resource bindings |
| Resource binding | Alias, access mode, tool rules, output limits and resource-context update policy |
| Invocation pipeline | Classify, authorize, obtain required approval, execute, limit output and record the result |
| Vault and persistence | Encrypted connector credentials, versioned context/documents, conversations and audit records |
| Observability services | Collect and display operational metrics, logs and traces |

Every resource tool call is classified as Read, Write or Destructive. A SELECT is Read; an UPDATE is Write; a DROP or service restart is Destructive. Unknown or unparseable operations are treated conservatively. The binding's access mode determines whether a classified call can proceed:

| Effect | ReadOnly | ReadWriteWithApproval | ReadWrite |
|---|---|---|---|
| Read | Allowed | Allowed | Allowed |
| Write | Denied | Approval required | Allowed |
| Destructive | Denied | Approval required | Approval required unless disabled for the binding |

Resource ceilings, deny patterns, tool rules, tenant lockdown and identity/credential scopes can restrict access further. Some operations always require approval. Use least-privilege credentials on the target systems as well as Hugo's policy checks; model instructions are not the authorization boundary.

Connector credentials resolve from the encrypted vault at execution time rather than being supplied in model tool arguments. Operational outputs and telemetry have secret-scrubbing protections. Authored documents are a separate, permission-controlled content channel and are delivered literally; use their visibility, attachment and public-access settings deliberately. See [Security](docs/SECURITY.md), [Safety model](docs/SAFETY_MODEL.md) and [Architecture](docs/ARCHITECTURE.md) for the full rules.

## Deployment and operations

The published images are `jchristn77/hugo-server` and `jchristn77/hugo-dashboard`, available as `v0.1.0` and `latest` for `linux/amd64` and `linux/arm64`. Supported deployments use containers and a single server replica. Running from source is for development.

| Deployment | Start here | Notes |
|---|---|---|
| Docker Compose | [Docker guide](docs/DOCKER.md), `docker/start.sh` / `start.bat` | SQLite by default, optional PostgreSQL and bundled observability |
| Kubernetes | [Kubernetes guide](docs/KUBERNETES.md), `k8s/` | Kustomize base, overlays, optional components and a Helm chart |
| minikube | [minikube guide](minikube/README.md), `minikube/scripts/` | Local cluster setup, deployment and port-forward helpers |
| Factory demo | [Factory guide](docs/FACTORY.md), `docker/factory/` | Separate seeded stack with databases, SSH, MinIO, Redis, Prometheus and Loki |

The Compose stack runs `hugo-server`, `hugo-dashboard`, `hugo-prometheus`, `hugo-tempo`, `hugo-loki`, `hugo-alloy` and `hugo-grafana`. The optional `postgres` profile adds `hugo-postgres`. Alloy receives telemetry and ships container logs; it does not publish a host port. Service ports and configuration are listed in the [Docker guide](docs/DOCKER.md) and [configuration reference](docs/CONFIGURATION.md).

To use PostgreSQL, configure `HUGO_DB_TYPE=Postgresql` and `HUGO_DB_PASSWORD` in `docker/.env`, then run `docker compose --profile postgres up -d` from `docker/`. Switching database backends does not move existing data. The server connects from inside its container, so use addresses reachable there; the Docker guide explains host networking and the opt-in Docker socket override.

For updates, back up the database and master key, then run this from `docker/`:

```bash
./update.sh          # Windows: update.bat; pull images and recreate containers, preserving data
```

Schema migrations run at startup. Preserve the `.env` configuration and database volumes across updates; follow the [upgrade guide](docs/UPGRADING.md) for rollback. `./reset.sh` (`reset.bat`) is destructive and removes stored data. Deployments exposed beyond the local machine also need the external URL, authentication and TLS/proxy configuration described in the deployment guides.

## Documentation and development

Use the guides for setup and operation, and the protocol references for exact request shapes. Detailed feature verification and benchmark results are kept separately from this overview.

| Topic | Documentation |
|---|---|
| Agent documents | [Inline/Reference delivery, revisions and retrieval](docs/AGENT_DOCUMENTS.md) |
| Agent and resource context | [Prompt composition](docs/PROMPTS.md), [resource learnings](docs/RESOURCE_CONTEXT.md) |
| Standalone chat and feedback | [Links, branding, visitor sessions and feedback review](docs/STANDALONE_CHAT.md) |
| Connectors | [Catalog](docs/CONNECTORS.md), [tool contracts](docs/MCP_CONNECTOR_TOOLS.md), [per-connector guides](docs/connectors/) |
| Security and authorization | [Security](docs/SECURITY.md), [safety model](docs/SAFETY_MODEL.md) |
| Operations | [Configuration](docs/CONFIGURATION.md), [telemetry](docs/TELEMETRY.md), [Grafana](docs/GRAFANA.md), [troubleshooting](docs/TROUBLESHOOTING.md) |
| Deployment | [Docker](docs/DOCKER.md), [Kubernetes](docs/KUBERNETES.md), [minikube](minikube/README.md), [upgrades](docs/UPGRADING.md) |
| API clients | [REST](REST_API.md), [MCP](MCP_API.md), [A2A](A2A_API.md), [Postman assets](assets/postman/), [MCP client setup](docs/MCP_CLIENTS.md) |
| Localization | [Dashboard languages and translation conventions](docs/I18N.md) |
| Development | [Builds, tests and connector testbed](docs/DEVELOPMENT.md), [connector authoring](docs/CONNECTOR_AUTHORING.md) |
| Evidence and measurements | [Simulated user testing](user-testing/), [document-context benchmarks](benchmarks/README.md) |

Hugo's backend is C# targeting .NET 8 and .NET 10; the dashboard uses React. Development setup and test commands live in [DEVELOPMENT.md](docs/DEVELOPMENT.md), while each SDK has its own build instructions, examples and harness. Changes that affect the API should keep the REST/MCP/A2A references, Postman collection and SDKs in sync.

Questions and discussion: [Discord](https://discord.gg/tRAN8HgvK5). Report bugs and feature requests through [GitHub issues](https://github.com/jchristn/AgentSmith/issues). Hugo is [MIT licensed](LICENSE.md).
