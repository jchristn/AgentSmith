# Changelog

All notable changes to Hugo are recorded here. Versions follow `VERSIONING.md`: pre-1.0 releases are labeled alpha and may contain breaking changes.

## 0.1.0-alpha (unreleased)

The first release of Hugo: a multi-tenant super agent server that holds credentialed access to your production systems, groups that access into agent profiles, and serves those profiles over MCP, A2A, REST, an OpenAI-compatible endpoint, and dashboard chat. The default profile is Hugo, whose persona is Agent Smith and who calls you Mr. Anderson.

Everything below is new in this release. The images are `jchristn77/hugo-server:v0.1.0` and `jchristn77/hugo-dashboard:v0.1.0`, built for `linux/amd64` and `linux/arm64`.

### Agent instructions

- Document create/edit accepts literal administrator content without secret-pattern rejection. UTF-8, size, access and concurrency validation remain enforced.

- AES secrets record independent base64 or hex formats for key, salt and IV, so one resource can use mixed-format material without guessing.

- Agents can read and update their current profile's persistent Extra instructions through hosted chat and MCP. The model handles version checks and concise consolidation, with profile permissions, atomic updates, secret guards, auditing, and fresh instructions before each hosted model call. No database migration is needed.

### Connectors

- AES key material is a multi-field secret containing key, salt, and IV. Cryptography resources bind the bundle once and support direct AES keys or explicit PBKDF2-SHA256 derivation; existing Generic key bindings remain compatible.

- Cryptography resources provide vault-key AES-GCM/AES-CBC encryption and decryption, SHA-256/384/512 hashing, and hash verification. Binary storage reads can feed encrypted objects to the connector without exposing keys.

### Dashboard

- MCP Explorer starts initialize instructions collapsed and displays structured tool results directly, with raw responses available separately. A2A Explorer lays out agent cards, skills, examples, interfaces and authentication, with a visual message flow and editable JSON parameters.

- Modal identifiers use monospace text with the shared copy control. Clipboard fallback works inside dialogs on non-secure HTTP origins and restores focus. Feedback details uses a wider responsive modal.

- Activity includes Feedback review: select an agent, page through ratings and comments, and inspect the associated request, response, token counts, timing, model endpoint, conversation, and turn. Access follows normal tenant and conversation visibility.

- Standalone chat has configurable heading, logo, and browser title, a compact composer anchored below the scrolling transcript, an AI accuracy notice, and per-answer thumbs-up/down with optional written feedback. Markdown renders in both user and assistant messages. Migration 11 stores branding and visitor feedback on SQLite and PostgreSQL.

- User-managed standalone agent chat links support Public and Authenticated access, isolated visitor sessions, URL rotation and revocation, profile Connect controls, and a responsive chat page without dashboard navigation. Migration 10 preserves existing profiles as unpublished.

- Create resource starts with a searchable connector card grid instead of a dropdown near the bottom of the form.

- Model endpoint editing and health inspection use separate modals. The health view presents summary cards, a chronological health histogram, timestamps, latest failure, and expandable check details.

- Nine sidebar entries replace the previous 25, with related pages on URL-addressable tabs. Legacy bookmarks and detail routes remain usable, role restrictions apply per tab, and pending approvals remain visible on Agents and the topbar.

### Deployment

- Wave 1 testbed starts SSH, storage, and SQL targets with automatic SSH key initialization; the full testbed remains available through `compose.all.yaml`. Dedicated CI validates live connector coverage for SQLite and PostgreSQL.
- CI checks a reviewed direct-package inventory and rejects floating or unreviewed dependency pins.

- Docker start helpers repair missing, blank, and quoted-whitespace keys without replacing populated keys, including existing files without a final newline.
- Opt-in Docker socket override keeps the server non-root and joins the socket group; deployment testing inventory covers Compose, Kubernetes, and minikube.
- CI checks product version mirrors and Grafana dashboard mirrors; visual QA captures all nine required views at three widths in both themes.
- Docker Compose deployment (`docker/`): `start.sh` and `start.bat` generate the master key and legacy token validation key on first run; `update.sh` and `update.bat` pull and recreate the stack without touching data; `reset.sh` and `reset.bat` delete everything after you type RESET.
- Kubernetes (`k8s/`: Kustomize base and overlays, a Helm chart, and scripts) and minikube (`minikube/`) deployments on the same images, one server replica by design. See `docs/KUBERNETES.md`.
- A seeded demo stack in `docker/factory/` with its own project name and ports (API 8810, dashboard 8811), a demo profile, sample resources, and its own `factory/reset.sh`. See `docs/FACTORY.md`.
- The stack runs the server, the dashboard, Prometheus, Tempo, Loki, Alloy, and Grafana, with a curl or wget healthcheck on every service and dependency ordering. Every host port binds to `127.0.0.1`.
- SQLite by default; PostgreSQL with `HUGO_DB_TYPE=Postgresql` and the `postgres` Compose profile.
- First boot seeds the `default` tenant, the system administrator `admin@hugo.local`, the tenant administrator `anderson@hugo.local`, the built-in roles, and the Hugo profile. Both seeded accounts carry the advisory `mustChangePassword` flag: the dashboard prompts for a new password, and the prompt can be skipped (D-048).

### Identity, access control, and audit

- Logs pass through a shared redaction boundary before console, file, syslog, trace, and telemetry sinks, including model-library messages. Bounded secret registration covers configured and decrypted credentials, and quoted sensitive values retain valid JSON framing.
- Native application logs export through a bounded Radiant queue to OTLP/Alloy or directly to Loki. Deployment collectors retain dashboard and worker logs while avoiding duplicate server collection.

- REST API enum inputs require declared names; numeric enum settings files retain compatibility.

- Tenants, users, credentials (access key and secret, returned once), roles, permissions, user role assignments, and credential scope assignments, with tenant-wide or per-resource scope.
- Authentication by bearer session token (`Authorization` or `x-token`), user login headers, access key and secret, HMAC-signed requests with nonce and clock-skew checks, and an optional system administrator API key.
- Passwords hashed with PBKDF2-HMAC-SHA256 (600,000 iterations). Sessions expire (60 minutes for users, 30 for administrators) and refresh up to an absolute 12 hours. Re-authentication route for sensitive actions.
- Built-in roles including the Hugo-specific Operator, Builder, and ProfileUser; effective-permission inspection for users and credentials; deny-overrides; a credential can never exceed its owner.
- Session tokens use the versioned master-key HKDF subkey with AES-256-GCM; pre-upgrade signing-key tokens remain readable until expiry or revocation. All administrator bypasses, including reads, are audited.
- Every route, MCP tool, and A2A method is mapped to a resource type and operation, and unmapped routes fail closed.
- Cross-tenant reads return 404. Authorization denials return 403 with the required permission and are always audited.
- Filtered audit summaries with total and per-event counts, available through REST and all three SDKs.
- Audit trail, request history with captured headers and bodies (secrets scrubbed), and rate limiting on sign-in and secret reveal.

### Secrets vault

- Secrets encrypted with AES-256-GCM under a versioned master key, with HKDF-derived keys and associated data binding each value to its tenant and secret.
- Ten secret kinds (password, private key, API token, bearer token, access key pair, service account JSON, kubeconfig, connection string, certificate, generic; BearerToken is for tokens sent exactly as `Authorization: Bearer`, D-049) with import parsing for SSH keys, GCP service accounts, kubeconfigs, and PEM certificates.
- Values are never returned except through the audited reveal route, which needs the `Reveal` permission, a reason, and a recent password re-entry.
- Startup refuses to run with a master key that does not match the database. Resumable master key rotation for system administrators.
- A secret scrubber removes known secret values and credential patterns from tool output, logs, request history, and audit text.

### Connectors

- AWS S3 decodes copied JSON escapes such as `\u002B` in credentials before signing, while preserving stored secrets and literal custom-endpoint credentials.
- S3 connection failures distinguish AWS credential, signature, region, and access-policy errors safely and explain the bucket-list permission required by the probe.
- Removed the legacy Papertrail connector. SolarWinds Observability remains available for SolarWinds Cloud API tokens and log queries.

- Scheduled resource health checks default to HTTP and Redis every five minutes, with explicit connector opt-in, bounded probes, and aggregate health metrics. Scheduled checks cannot learn SSH host keys.

Nineteen connector types, each with its own settings schema, secret slots, tools, effect classifier, default instructions, and test-connection check:

- **SSH**: commands, files, services, and system overview, with host key pinning on first use and a shell command classifier.
- **Object storage**: Amazon S3, Azure Blob Storage, and Google Cloud Storage, including signed presigned URLs.
- **SQL databases**: SQLite, MySQL, SQL Server, and PostgreSQL, with guarded queries, activity and lock views, and database intelligence: schema crawling, inferred relationships, durable database and table context, and question answering with verified SQL.
- **Logs**: SolarWinds Observability.
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

- Every connector exposes resource_get_context and resource_update_context for versioned learnings in Hugo's resource knowledge store. Fresh bounded context informs each internal agent and connector-model call; permissions, audit, and operator-authored notes remain enforced.

- Agent profiles bind resources with an alias, access mode, approval policy, and instruction override, and expose themselves over MCP, A2A, and the OpenAI-compatible API independently.
- Layered prompt templates (Built-in, Global, Tenant) with versions, diffs, reset at each layer, and detection of built-in updates under customized text.
- Compiled instructions per profile, caller, and surface, with a source map and token estimate. Profiles with more than three resources use compact connector instructions automatically, and `hugo_describe_resource` returns the full text on demand.
- Profile resolution by path, header, credential binding, user default, then tenant default. A credential can be locked to its profile.

### Agent runtime and protocol surfaces

- Model endpoints for Ollama, OpenAI, OpenAI-compatible servers, Anthropic, Gemini, Azure OpenAI, Bedrock, Vertex AI, and VoyageAI embeddings, with validation of saved and unsaved settings (`POST /v1.0/api/model-endpoints/validate`), names that may contain spaces and `. _ - : / @`, health sweeps, concurrency gates, retries, a circuit breaker, and fallback.
- Hugo's own agent loop: streaming, thinking blocks, parallel read calls, tool retrieval for large profiles, history compaction, cancellation, and a forced final answer at the iteration limit.
- Conversations with Server-Sent Events chat streaming, export as Markdown or JSON, and analytics for turns, tokens, tools, and models.
- **MCP** at `/mcp` and `/mcp/{profile}` over Streamable HTTP: per-caller tool lists, the `hugo_*` meta tools, `ask_agent_smith`, optional management tools, prompts, and resources.
- **A2A** 1.0 at `/v1.0/a2a/{tenantId}/{profileName}` with agent cards, background tasks, streaming, and both current and legacy method names.
- **OpenAI-compatible** chat completions and model listing at `/v1.0/openai/v1`.
- A tenant event stream at `/v1.0/api/events`.

### Dashboard

- Required dropdowns show an empty choice until selected. New profile bindings can select the first existing resource directly; editing a binding locks its resource to match the API contract.

- Chat token totals count persisted history once and add new streamed turns without doubling the total after reload.

- React 19 operator console: home with external services, chat with inline tool calls and approval cards, agent profiles with a compiled-instructions preview, resources with schema-driven forms and Test Connection, secrets, connectors and their default instructions, model endpoints with a setup wizard, approvals inbox, tool invocations, conversations, analytics, request history, API explorer, audit, identity and access pages, settings with restart indicators, lockdown, and a Discord link in the topbar.
- Twelve languages (English plus eleven machine-seeded catalogs marked beta), right-to-left support, and two pseudo-locales.

### SDKs and tools

- Live model smoke requests have explicit output/time limits and successful-final-answer checks; isolated local MCP client runners capture sanitized acceptance traces.

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
- Approving a call while an MCP client is already waiting on it in `hugo_await_approval` leaves the call unexecuted; the waiter returns `approval_pending`. Approve first, then await (or let the original call wait).
- A template that includes itself (for example `{{> system.tool-usage}}` saved as `system.tool-usage`) is accepted at save time; compilation then detects the cycle and falls back to the built-in templates for the whole profile, with only a warning.
- List filters with an unrecognized enum value (`?status=Bogus`) are ignored instead of rejected.

### Agent documents and background chat

- Reusable versioned Markdown documents can be attached to agents with Inline or Reference delivery, scoped read tools, revision pins and caller/public previews.
- Dashboard and standalone streaming chat turns continue after navigation or disconnect. Returning viewers refresh saved progress; Stop explicitly cancels a turn.
