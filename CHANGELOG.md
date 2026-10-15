# Changelog

All notable changes to Hugo are recorded here. Versions follow `VERSIONING.md`: pre-1.0 releases are labeled alpha and may contain breaking changes.

## 0.1.0-alpha (unreleased)

The first release of Hugo: a multi-tenant super agent server that holds credentialed access to your production systems, groups that access into agent profiles, and serves those profiles over MCP, A2A, REST, an OpenAI-compatible endpoint, and dashboard chat. The default profile is Hugo, whose persona is Agent Smith and who calls you Mr. Anderson.

Everything below is new in this release. The images are `jchristn77/hugo-server:v0.1.0` and `jchristn77/hugo-dashboard:v0.1.0`, built for `linux/amd64` and `linux/arm64`.

### Deployment

- Docker Compose is the supported deployment (`docker/`). `start.sh` and `start.bat` generate the master key and session signing key on first run; `update.sh` and `update.bat` pull and recreate the stack without touching data; `reset.sh` and `reset.bat` delete everything after you type RESET.
- A seeded demo stack in `docker/factory/` with its own project name and ports (API 8810, dashboard 8811), a demo profile, sample resources, and its own `factory/reset.sh`. See `docs/FACTORY.md`.
- The stack runs the server, the dashboard, Prometheus, Tempo, Loki, Alloy, and Grafana, with a curl or wget healthcheck on every service and dependency ordering. Every host port binds to `127.0.0.1`.
- SQLite by default; PostgreSQL with `HUGO_DB_TYPE=Postgresql` and the `postgres` Compose profile.
- First boot seeds the `default` tenant, the system administrator `admin@hugo.local`, the tenant administrator `anderson@hugo.local`, the built-in roles, and the Hugo profile. Both seeded accounts must change their password at first sign-in.

### Identity, access control, and audit

- Tenants, users, credentials (access key and secret, returned once), roles, permissions, user role assignments, and credential scope assignments, with tenant-wide or per-resource scope.
- Authentication by bearer session token (`Authorization` or `x-token`), user login headers, access key and secret, HMAC-signed requests with nonce and clock-skew checks, and an optional system administrator API key.
- Passwords hashed with PBKDF2-HMAC-SHA256 (600,000 iterations). Sessions expire (60 minutes for users, 30 for administrators) and refresh up to an absolute 12 hours. Re-authentication route for sensitive actions.
- Built-in roles including the Hugo-specific Operator, Builder, and ProfileUser; effective-permission inspection for users and credentials; deny-overrides; a credential can never exceed its owner.
- Every route, MCP tool, and A2A method is mapped to a resource type and operation, and unmapped routes fail closed.
- Cross-tenant reads return 404. Authorization denials return 403 with the required permission and are always audited.
- Audit trail, request history with captured headers and bodies (secrets scrubbed), and rate limiting on sign-in and secret reveal.

### Secrets vault

- Secrets encrypted with AES-256-GCM under a versioned master key, with HKDF-derived keys and associated data binding each value to its tenant and secret.
- Nine secret kinds (password, private key, API token, access key pair, service account JSON, kubeconfig, connection string, certificate, generic) with import parsing for SSH keys, GCP service accounts, kubeconfigs, and PEM certificates.
- Values are never returned except through the audited reveal route, which needs the `Reveal` permission, a reason, and a recent password re-entry.
- Startup refuses to run with a master key that does not match the database. Resumable master key rotation for system administrators.
- A secret scrubber removes known secret values and credential patterns from tool output, logs, request history, and audit text.

### Connectors

Twenty connector types, each with its own settings schema, secret slots, tools, effect classifier, default instructions, and test-connection check:

- **SSH**: commands, files, services, and system overview, with host key pinning on first use and a shell command classifier.
- **Object storage**: Amazon S3, Azure Blob Storage, and Google Cloud Storage, including signed presigned URLs.
- **SQL databases**: SQLite, MySQL, SQL Server, and PostgreSQL, with guarded queries, activity and lock views, and database intelligence: schema crawling, inferred relationships, durable database and table context, and question answering with verified SQL.
- **Logs**: Papertrail and SolarWinds Observability.
- **Redis**, the **AWS** control plane (EC2, ECS, RDS, Lambda, CloudWatch, Logs Insights, Route 53, SNS, SQS, ELB, Auto Scaling), **Kubernetes**, **Docker** hosts, and generic **HTTP** APIs.
- **Prometheus**, **Grafana Loki**, and **Elasticsearch or OpenSearch**.
- **External MCP servers** and **remote A2A agents**, reached through fixed tools (`mcp_call_tool`, `a2a_delegate`, and friends).
- Plugin connectors load from a `plugins/` directory in an isolated load context when `Connectors.EnablePlugins` is on.
- Durable per-resource knowledge that connectors, agents, and operators can read and write.

### Safety

- Every tool call is classified Read, Write, or Destructive per call. Bindings are ReadOnly, ReadWriteWithApproval, or ReadWrite, and Destructive calls need approval even under ReadWrite unless a binding opts out. Unclassifiable calls are treated as Destructive.
- Per-binding tool rules (Allow, Deny, RequireApproval), command deny patterns, output limits, and a per-resource access mode ceiling.
- Approvals with expiry, self-approval policy, and per-surface behavior: inline in chat, `input-required` in A2A, a bounded wait plus `hugo_await_approval` in MCP, and 202 from REST.
- Tenant lockdown denies every non-Read call and cancels pending approvals.
- Every tool call from every surface goes through one pipeline and is recorded as a tool invocation. A dry-run route reports the decision without executing.

### Agent profiles and instructions

- Agent profiles bind resources with an alias, access mode, approval policy, and instruction override, and expose themselves over MCP, A2A, and the OpenAI-compatible API independently.
- Layered prompt templates (Built-in, Global, Tenant) with versions, diffs, reset at each layer, and detection of built-in updates under customized text.
- Compiled instructions per profile, caller, and surface, with a source map and token estimate. Profiles with more than three resources use compact connector instructions automatically, and `hugo_describe_resource` returns the full text on demand.
- Profile resolution by path, header, credential binding, user default, then tenant default. A credential can be locked to its profile.

### Agent runtime and protocol surfaces

- Model endpoints for Ollama, OpenAI, OpenAI-compatible servers, Anthropic, Gemini, Azure OpenAI, Bedrock, Vertex AI, and VoyageAI embeddings, with validation, health sweeps, concurrency gates, retries, a circuit breaker, and fallback.
- Hugo's own agent loop: streaming, thinking blocks, parallel read calls, tool retrieval for large profiles, history compaction, cancellation, and a forced final answer at the iteration limit.
- Conversations with Server-Sent Events chat streaming, export as Markdown or JSON, and analytics for turns, tokens, tools, and models.
- **MCP** at `/mcp` and `/mcp/{profile}` over Streamable HTTP: per-caller tool lists, the `hugo_*` meta tools, `ask_agent_smith`, optional management tools, prompts, and resources.
- **A2A** 1.0 at `/v1.0/a2a/{tenantId}/{profileName}` with agent cards, background tasks, streaming, and both current and legacy method names.
- **OpenAI-compatible** chat completions and model listing at `/v1.0/openai/v1`.
- A tenant event stream at `/v1.0/api/events`.

### Dashboard

- React 19 operator console: home with external services, chat with inline tool calls and approval cards, agent profiles with a compiled-instructions preview, resources with schema-driven forms and Test Connection, secrets, connectors and their default instructions, model endpoints with a setup wizard, approvals inbox, tool invocations, conversations, analytics, request history, API explorer, audit, identity and access pages, settings with restart indicators, and lockdown.
- Twelve languages (English plus eleven machine-seeded catalogs marked beta), right-to-left support, and two pseudo-locales.

### SDKs and tools

- SDKs for C# (`Hugo.Sdk`), Python (`hugo-sdk`), and JavaScript and TypeScript (`@jchristn/hugo-sdk`), each with a live test harness.
- The `hugo` command line (`dotnet tool install -g Hugo.Cli`): a stdio MCP relay for clients that only launch local servers, plus login, whoami, and profile listing.

### Observability

- Watson HTTP telemetry and `hugo_*` application metrics and spans for agent turns, connector calls, model calls, MCP, A2A, approvals, secrets, and background work.
- Prometheus metrics at `/metrics`, OTLP traces to Tempo through Alloy, container logs to Loki, and provisioned Grafana dashboards in a Hugo folder.

### Documentation

- `README.md`, `REST_API.md`, `MCP_API.md`, `A2A_API.md`, and a Postman collection and environment in `assets/postman/`.
- Guides in `docs/` for architecture, configuration, security, the safety model, prompts, connectors and their tools, MCP clients, Docker, telemetry and Grafana, troubleshooting, upgrading, internationalization, and development.

### Known issues

- `GET /v1.0/api/model-endpoints/{id}/models` answers 500 when the provider is unreachable instead of 502 or 503.
- Saving `section.resource` or `instructions.root` to the Global or Tenant layer fails validation, because the save-time check does not accept `this` outside `{{#each}}` or the `compactInstructions` flag. Leave those two keys at their defaults for now.
- Some create routes are documented in OpenAPI as returning 200 while they return 201.
- MCP sessions live in memory, so clients reconnect after a server restart. Hugo runs as a single replica.
