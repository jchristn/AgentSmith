# Architecture

Hugo is one .NET server process with one public port, a React dashboard served by nginx, and a database. Everything else in the deployment (Prometheus, Tempo, Loki, Alloy, Grafana, optionally PostgreSQL) is supporting cast. The design keeps the interesting complexity in two places: the connectors, which know how to talk to SSH hosts, buckets, databases, clusters, and log services, and the tool invocation pipeline, which decides whether a call may run. The surfaces in front (REST, MCP, A2A, the OpenAI-compatible endpoint, dashboard chat) are deliberately thin.

## Topology

```
                     browser                     Claude Code / agents / scripts
                        |                                       |
                        v                                       |
            +-----------------------+                           |
            |  hugo-dashboard :8801 |  nginx: static React app, |
            |  /v1.0, /mcp, and     |  proxies API calls        |
            |  /openapi.json proxied|                           |
            +-----------+-----------+                           |
                        |                                       |
                        v                                       v
   +----------------------------------------------------------------------------+
   |  hugo-server :8800  (Watson 7 listener, one port)                          |
   |                                                                            |
   |   /v1.0/api/...  /v1.0/token     REST management API                       |
   |   /mcp, /mcp/{profile}  -------> MCP proxy --> Voltaic MCP server          |
   |                                                (internal 127.0.0.1 port)   |
   |   /v1.0/a2a/{tenant}/{profile}   A2A JSON-RPC and agent cards              |
   |   /v1.0/openai/v1/...            OpenAI-compatible chat completions        |
   |   /metrics                       Prometheus, relayed from :9464            |
   |                                                                            |
   |   auth -> route registrar -> services -> ToolInvocationService -> connector |
   +------+----------------------------+---------------------------+------------+
          |                            |                           |
          v                            v                           v
   SQLite file or PostgreSQL     your systems: SSH, S3, SQL,   model endpoints:
   (hugo-postgres profile)       Redis, AWS, Kubernetes, ...   Ollama, OpenAI, Anthropic, ...

   OTLP traces and logs --> hugo-alloy --> hugo-tempo, hugo-loki
   hugo-prometheus scrapes /metrics;  hugo-grafana reads all three
```

The server listens on a single port (8800 in the container) for REST, MCP, A2A, and the OpenAI-compatible routes. MCP is the one surface that is not a Watson route at heart: Voltaic's MCP server runs as its own `HttpListener` on a loopback port that nobody outside the process can reach, and `McpProxy` relays `/mcp` and `/mcp/{profileName}` to it (D-005). The proxy authenticates each request with the same schemes as REST, resolves the agent profile, and passes the caller to the internal endpoint through a per-request handle protected by a per-process secret. From the outside, MCP looks like any other path on the port.

The dashboard container serves the built React app and proxies `/v1.0`, `/mcp`, and `/openapi.json` to the server, so a browser only ever talks to one origin. The Compose stack, ports, and volumes are described in [DOCKER.md](DOCKER.md); Kubernetes and minikube layouts are in [KUBERNETES.md](KUBERNETES.md).

## Projects

| Project | What it holds |
|---|---|
| `src/Hugo.Connectors.Abstractions` | The connector contract: `IConnector`, `IConnectorTool`, descriptors, effects, `ToolResult`, error codes, the knowledge store and model interfaces connectors may use. It depends on nothing in Hugo. |
| `src/Hugo.Connectors.*` | One project per service family: Ssh, Storage (S3, Azure Blob, GCS), Sql (SQLite, MySQL, SQL Server, PostgreSQL), Logging (Papertrail, SolarWinds Observability), Redis, Aws, Kubernetes, Docker, Http, Observability (Prometheus, Loki, Elasticsearch), and Agents (external MCP servers, remote A2A agents). |
| `src/Hugo.Core` | Models, enums, the data layer, security primitives (token service, secret cipher, request signer, scrubber, permission evaluator), the template renderer and instruction compiler, the access policy, the agent runtime, telemetry, and the built-in prompt templates. |
| `src/Hugo.Server` | The host: bootstrapper, `HugoServer`, modules, route registrars, the authentication and authorization services, the tool invocation pipeline, the MCP proxy and tool executor, A2A, the OpenAI-compatible surface, and background jobs. |
| `src/Hugo.Cli` | The `hugo` .NET tool: login, whoami, and the stdio MCP relay for clients that only speak stdio. |
| `src/Test.*` | Touchstone suites (`Test.Shared`), their runners (`Test.Automated`, `Test.Xunit`, `Test.Nunit`), and a sample plugin. |
| `dashboard/` | The React 19 and Vite operator console. |
| `sdk/` | C#, Python, and JavaScript SDKs, each with its own README and test harness. |

The core never contains connector-specific code. That rule is what lets a plugin add a service family without touching the server.

## Inside the server

### Startup

`Program.Main` hands off to `Bootstrapper`, which resolves and loads the settings file, applies `HUGO_*` overrides, validates, opens the database and applies migrations, loads the master key and checks it against the stored canary, seeds the default tenant and the two seeded users, and builds the shared services (audit, tokens, authentication, authorization, telemetry) into a `ServerContext`. [CONFIGURATION.md](CONFIGURATION.md) covers the settings side of this.

`HugoServer` then runs its modules. Each module creates services and publishes them on the context, registers its routes, and starts its background work; they initialize in order and stop in reverse:

| Module | Responsibility |
|---|---|
| `IdentityModule` | Tenants, users, credentials, roles, permissions, assignments, sessions, audit, request history, settings (file-backed and database-backed), server info, the event stream, observability status, and retention. |
| `ConfigurationModule` | The secrets vault, master key rotation, tenant prompt templates, and system routes. |
| `IntegrationModule` | Connectors (built-in and plugins), resources, resource knowledge, the tool invocation pipeline, approvals, lockdown, model endpoints, agent profiles, bindings, layered prompt templates, the instruction compiler, and the tool catalog. |
| `AgentModule` | Hugo's own agent loop and the surfaces that run it: conversations and dashboard chat, delegation (`ask`), analytics, A2A, and the OpenAI-compatible endpoint. |
| `McpModule` | The Voltaic MCP server, the proxy, the per-caller tool catalog, meta tools, prompts, and resources. |

### A request's path

Every HTTP request follows the same path through Watson's hooks:

1. **Preflight** answers CORS. The allowed origins come from `Rest.CorsOrigins`.
2. **Authentication** (`AuthenticationService.AuthenticateRequestAsync`) resolves exactly one scheme into a `RequestContext`: principal type and ID, tenant, admin flags, session, credential. Failures answer 401 before any route runs. [SECURITY.md](SECURITY.md) describes the schemes.
3. **Routing.** Each route was registered through `RouteRegistrar` as anonymous, scoped, or handler-authorized, and each registration recorded its resource type and operation in the `OperationScopeMap`, the single list of what every route, MCP tool, and A2A method requires. Before the handler runs, the wrapper checks that a tenant named in the path exists, blocks users who must change their password, and (for scoped routes) asks the authorization service for a decision. A denial becomes 403 with the required permission, or 404 when the target is in another tenant. The same wrapper maps domain exceptions to the JSON error shape and maps an unreachable or saturated database to 503 with `Retry-After`.
4. **The handler** calls services. Services always take the tenant from the request context, never from the body.
5. **Post-routing** captures the request into request history (bounded bodies, scrubbed, skipped for secret reveals, and no response body for reads of request history itself) and records metrics.

### The tool invocation pipeline

`ToolInvocationService.InvokeAsync` is the only way a tool runs. Chat turns, MCP tool calls, A2A tasks, OpenAI-compatible turns, REST invokes, and the dry-run route all build an `InvocationRequest` and call it. The steps are:

1. Resolve the profile, the binding (by alias, from the `resource` argument, or by resource ID), the resource, the connector, and the tool.
2. Authorize `Execute` on the profile and enforce a credential's `lockToProfile`.
3. Validate the arguments against the tool's input schema, then check binding deny patterns.
4. Classify the call as Read, Write, or Destructive using the resource's settings (no secrets yet).
5. Decide with `AccessPolicy`: tool rules, resource ceiling, tenant lockdown, and the access mode matrix.
6. If approval is required, create the approval and wait as the surface asks (wait, wait with a timeout, or return at once).
7. Resolve secrets from the vault for this call only, run the tool under its timeout, the per-resource concurrency cap, and output limits, with the profile's model available to tools that need one.
8. Scrub the output, bound it, and record the invocation, metrics, trace span, and a `tool.completed` event.

The details of steps 3 to 6 are in [SAFETY_MODEL.md](SAFETY_MODEL.md). Connector sessions (SSH clients, database connections) are pooled per resource and evicted after `Connectors.SessionIdleMinutes`.

### The agent runtime

Hugo runs its own loop for dashboard chat, A2A, the OpenAI-compatible endpoint, `ask_agent_smith`, and `POST /v1.0/api/agent-profiles/{id}/ask` (D-012). MCP callers can also skip the loop entirely and drive the raw tools with their own model.

`AgentTurnHost` prepares a turn: it resolves the profile (D-018), loads the compiled instructions for the surface, the conversation history, and the visible tools, and takes the per-conversation lock so only one turn runs at a time. `AgentRuntime` (in `Hugo.Core`) then loops up to the profile's `maxIterations`: stream a model response through PolyPrompt, strip `<think>` blocks into thinking events, run the requested tool calls through the pipeline (independent Read calls in parallel up to `Agent.MaxParallelToolCalls`, writes one at a time), wrap each result as `<tool_output>` data, and repeat. The last iteration forces an answer without tools. When history grows past `Agent.CompactionThresholdRatio` of the model's context window, older turns are compacted into a persisted summary. The loop never offers itself the `hugo_*` meta tools or `ask_agent_smith`, which rules out recursion (D-047).

Events flow out as Server-Sent Events with named types (`turn.started`, `assistant.delta`, `assistant.thinking`, `tool.proposed`, `tool.started`, `tool.completed`, `approval.required`, `approval.resolved`, `turn.completed`, `error`) for chat, are mapped onto A2A task states for A2A, and onto OpenAI chunks for the OpenAI-compatible endpoint. Conversations from every surface land in the same tables, so the dashboard shows them all.

### Instructions

The instruction compiler builds what a caller is told: persona, safety rules, tool usage, the surface preamble, one section per enabled binding (with connector instructions resolved through the Built-in, Global, and Tenant template layers, binding overrides, resource notes, and root knowledge), and the profile's appended text. Results are cached and invalidated by any change to the profile, its bindings, resources, or templates. [PROMPTS.md](PROMPTS.md) covers the layers and the template language.

## Data layer

Hugo stores its own data in SQLite or PostgreSQL (D-003). The SQL is hand written and fully parameterized against `System.Data.Common` (`Database/Ado`), with a small per-provider dialect for the places where SQLite and PostgreSQL differ (D-033). Persistence is structured: real columns and child tables (resource settings are one row per key, for example), with a short, documented list of JSON columns for things that are genuinely free-form, such as scrubbed tool arguments.

Schema changes are versioned, idempotent migrations tracked in `schema_migrations`, applied at startup when `Database.ManageSchema` is true. Health reports the schema version and answers 503 `migrating` while it is behind. SQLite serializes writes through a semaphore and lets reads run concurrently under WAL, which is plenty for a single node. PostgreSQL is the choice when the database should live outside the container, and it is what the Kubernetes manifests use.

IDs are K-sortable PrettyId strings with a type prefix (`usr_`, `ses_`, `apr_`, and so on), so they sort by creation time and say what they are.

## Connectors and plugins

A connector implements `IConnector`: a descriptor (type, family, display name, category), a settings schema the dashboard turns into a form, named secret slots, a list of tools with input schemas and effect classifiers, default instructions in full and compact form, and a connection test. The server registers the built-ins at startup: `ssh`, `s3`, `azure-blob`, `gcs`, `sqlite`, `mysql`, `sqlserver`, `postgresql`, `papertrail`, `solarwinds-observability`, `redis`, `aws`, `kubernetes`, `docker`, `http`, `prometheus`, `loki`, `elasticsearch`, `mcp`, and `a2a-agent`. Tools are named `<family>_<operation>` and take a `resource` argument, so the tool count does not grow with the number of resources (D-017).

With `Connectors.EnablePlugins` on, each subdirectory of `Connectors.PluginDirectory` is loaded as a plugin (`plugins/acme/acme.dll`) into its own collectible `AssemblyLoadContext`. Public `IConnector` types with a parameterless constructor are registered; a plugin that tries to reuse an existing connector type is refused as a whole. Loads and refusals are audited. Writing a connector is covered in [CONNECTOR_AUTHORING.md](CONNECTOR_AUTHORING.md), and every connector's settings and tools are in [CONNECTORS.md](CONNECTORS.md) and [MCP_CONNECTOR_TOOLS.md](MCP_CONNECTOR_TOOLS.md).

## Background work

A handful of jobs run inside the server process:

| Job | What it does |
|---|---|
| Retention | Prunes request history, audit records, and expired sessions by the retention settings, hourly. |
| Approval expiry | Expires pending approvals past `Approvals.ExpirySeconds`. |
| Model health | Checks each model endpoint every `Models.HealthIntervalSeconds` and records its health. |
| Session eviction | Closes idle pooled connector sessions. |
| Invocation recorder | Persists executed tool invocation records off the request path. |
| A2A recovery | At startup, marks A2A tasks left unfinished by a previous process as failed. |

## One replica

Hugo runs as a single replica on every runtime (D-045). Approvals, A2A tasks, conversations, and settings are persisted, so a restart loses nothing that matters (D-027), but some coordination is still in memory: MCP sessions (clients reinitialize after a restart, D-046), the event hub that feeds SSE, and the signals that wake a waiting approval. Some caches already invalidate across replicas through PostgreSQL notifications, which is groundwork for scaling out later; until the event hub and approval signals are shared too, run exactly one replica. The Compose stack does, and the Kubernetes manifests are written to.
