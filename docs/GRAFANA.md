# Grafana Dashboards

Hugo ships its Grafana dashboards as code. The JSON lives in `assets/grafana/`, one file per dashboard named `<uid>.json`. `docker/compose.yaml` mounts that directory read-only into the `hugo-grafana` container at `/etc/grafana/dashboards/hugo`, and the provider in `docker/observability/grafana-dashboards.yaml` loads every file into the **Hugo** folder (folder UID `hugo`). Datasources come from `docker/observability/grafana-datasources.yaml` with the fixed UIDs `prometheus`, `tempo`, and `loki`. Every dashboard references datasources by those UIDs only, so the JSON works unchanged in any Grafana that provisions the same UIDs.

The metric names the dashboards query are listed in [TELEMETRY.md](TELEMETRY.md) (generated from `HugoMetricCatalog`) and plan section 21.

## Open the dashboards

```
cd docker
docker compose up -d --build
```

Grafana is at http://127.0.0.1:3000 (admin / admin). Open **Dashboards > Hugo**, or go straight to `http://127.0.0.1:3000/d/<uid>`. Every dashboard has a **Hugo dashboards** dropdown in its header to move between them, plus **Traces in Tempo** and **Logs in Loki** links that open Explore with a query that fits the dashboard.

Dashboards are provisioned read-only (`allowUiUpdates: false`). To change one, edit the JSON in `assets/grafana/`; Grafana picks the change up within a minute (`updateIntervalSeconds: 60`) without a restart.

## Conventions

- **Folder and tags.** Every dashboard is in the Hugo folder and tagged `hugo`. Titles read `Hugo / <Domain>`.
- **Stable UIDs.** The UIDs below are part of the contract with the Hugo dashboard's deep links (`dashboard/src/utils/grafana.js`, `ObservabilityDashboards`). Do not rename them.
- **Template variables.** Every Prometheus dashboard has `job` (the scrape job, `hugo-server` in Docker Compose) and `window` (the averaging window for rates and percentiles, default 15m). Domain dashboards add low-cardinality filters such as `route`, `method`, `tenant`, `connector`, `effect`, `provider`, `kind`, `surface`, `service`, and `table`. All of them default to All.
- **Percentiles.** p50, p95, and p99 are computed in Grafana with `histogram_quantile` over the `_bucket` series. Nothing is precomputed in the server.
- **Exemplars.** Latency targets set `exemplar: true`. Prometheus runs with `--enable-feature=exemplar-storage`, and the Prometheus datasource maps the `trace_id` exemplar label to Tempo, so an exemplar dot opens the trace.
- **Empty is zero.** Stats and most time series end in `or on() vector(0)` so a family that has not been recorded yet shows 0, not "No data".
- **Descriptions.** Every panel has a description (the (i) icon in its header).
- **Logs.** Loki panels select `{app="hugo"}` with the `component`, `node`, `level`, and `tenant` labels that Alloy attaches (`docker/observability/config.alloy`).

## Dashboards

| UID | Title | Plan task | What it answers |
|---|---|---|---|
| `hugo-overview` | Hugo / Overview | P10.13 | Is Hugo healthy right now? Start here. |
| `hugo-http` | Hugo / HTTP API | P10.14 | Which route is slow or failing? |
| `hugo-agent` | Hugo / Agent and Chat | P10.15 | Where does turn time go (stages, iterations, first token)? |
| `hugo-tools` | Hugo / Tools and Connectors | P10.16 | Which connector or tool is failing, denied, slow, or waiting for approval? |
| `hugo-models` | Hugo / Models | P10.17 | Is a model provider slow, failing, or burning tokens? |
| `hugo-protocols` | Hugo / MCP and A2A | P10.18 | Are MCP and A2A clients succeeding? |
| `hugo-security` | Hugo / Security | P10.18 | Who is failing to sign in, being denied, revealing secrets, or tripping lockdown? |
| `hugo-database` | Hugo / Database | catalog | Is the database the bottleneck? |
| `hugo-integrations` | Hugo / Integrations | catalog | Is a downstream service the cause? |
| `hugo-control-plane` | Hugo / Control Plane | catalog | Process health, background services, telemetry pipeline. |
| `hugo-logs` | Hugo / Logs and Traces | catalog | Log volume, warnings and errors, failed and slow traces. |

The last four carry the catalog families whose `Dashboard` field names them (`hugo-database`, `hugo-integrations`, `hugo-control-plane`, `hugo-logs`), so every catalog metric is charted somewhere.

### Hugo / Overview (`hugo-overview`)

Variables: `job`, `window`, `search` (log text).

- **Health:** Server up, Uptime, Version, Requests per second, Error ratio, In flight.
- **Agent activity:** Agent turns, Tool calls, Tool calls denied, Approvals decided, Model requests, Tokens.
- **Trends:** REST requests by status class, REST latency (p50/p95/p99), Tool calls by connector, Agent turns by outcome, Approvals by outcome, Tokens per minute by direction.
- **Background work and telemetry:** Background job runs by outcome, Background job duration p95, Label values folded.
- **Logs:** Recent warnings and errors, Hugo logs (`{app="hugo"}`).

Section 21 asks for "approvals pending" on the overview. The catalog has no pending-approvals gauge, so the overview shows approvals decided by outcome; the pending list is on the Hugo dashboard's Approvals page.

### Hugo / HTTP API (`hugo-http`)

Variables: `job`, `tenant`, `route`, `method`, `window`.

- **Traffic:** Requests, Requests per second, Server errors (5xx), Error ratio, In flight, Latency p95, Requests by status class, Latency percentiles, Requests by route, Top routes by p95, Client errors (4xx) by route, Server errors (5xx) and aborts by route, Requests by tenant, Latency p95 by tenant.
- **Payloads:** Request body size p95 by route, Response body size p95 by route, Routes (table: requests, 5xx share, p50, p95, p99).
- **Watson listener:** Watson request duration p95 by route (`http_server_request_duration_seconds`), Active requests and connections, Listener throughput.

Requests that fail authentication are counted under the route `unauthenticated`.

### Hugo / Agent and Chat (`hugo-agent`)

Variables: `job`, `surface`, `window`.

- **Turns:** Agent turns, Failed turns, Completion rate, Turn time p95, Time to first token p95, Iterations p95, Turns by surface, Turns by outcome, Turn duration (p50/p95/p99), Turn duration p95 by surface.
- **Stages:** Stage duration p95 (context_build, compaction, tool_retrieval, inference, tool, approval_wait, persist), Time spent by stage, Iterations per turn, Time to first token by provider, Instructions compile time.
- **Streaming and traces:** Chat streams open, Slow agent turns (TraceQL `{ name = "agent.turn" && duration > 10s }`).

### Hugo / Tools and Connectors (`hugo-tools`)

Variables: `job`, `connector`, `effect`, `window`.

- **Calls:** Tool calls, Failed calls, Denied, Approval required, Tool latency p95, Sessions open, Calls by connector, Calls by effect, Policy decisions, Calls by status, Calls by surface.
- **Latency and output:** Tool latency (p50/p95/p99), Tool latency p95 by connector, Output size p95 by connector, Failures by connector and tool, Tools (table: calls, failure share, p50, p95).
- **Approvals and resources:** Approvals by outcome, Approval wait (p50/p95 by effect), Sessions open by connector, Resource health, Failed tool spans.
- **Wave 2 connectors:** Infrastructure calls by connector and effect (`redis`, `aws`, `kubernetes`, `docker`), Observability and HTTP latency p95 by connector (`prometheus`, `loki`, `elasticsearch`, `http`), Remote agent and MCP calls by tool and status (`mcp`, `a2a-agent`), Wave 2 policy decisions by connector, Wave 2 failures by connector and tool.

The `connector` label carries the connector type exactly as the resource names it (`postgresql`, `ssh`, `s3`, `redis`, `aws`, `kubernetes`, `docker`, `http`, `prometheus`, `loki`, `elasticsearch`, `mcp`, `a2a-agent`, and so on), not the shorter tool-prefix family (`k8s`, `es`, `prom`). The `connector` variable is built from the label values Prometheus has seen, so a connector appears in it after its first call. The wave 2 panels add a fixed connector list on top of the variable, so they stay scoped to their group even when the variable is All.

Label values are matched case-insensitively (`decision=~"(?i)denied"`), and a call counts as failed when its `status` is not ok, success, succeeded, or completed.

### Hugo / Models (`hugo-models`)

Variables: `job`, `provider`, `kind`, `window`.

- **Requests:** Model requests, Failed requests, Error ratio, Latency p95, Time to first token p95, Tokens, Requests by provider, Requests by outcome, Request latency (p50/p95/p99), Latency p95 by provider, Time to first token (p50/p95/p99 by provider), Errors by provider.
- **Tokens:** Tokens per minute by provider, Output to input ratio, Providers (table).

### Hugo / MCP and A2A (`hugo-protocols`)

Variables: `job`, `window`.

- **MCP** and **A2A** (one row each): requests, errors, error ratio, HTTP p95, HTTP 5xx, and HTTP request rate of the endpoint (`/mcp`, `/v1.0/a2a`), requests by method, requests by outcome, endpoint latency (p50/p95/p99).
- **Server-sent events:** Streams open, Events per second, Failed protocol spans.

### Hugo / Security (`hugo-security`)

Variables: `job`, `window`, `search`.

- **Authentication:** Sign-in failures, Authorization denials, Rate limited, Tokens issued, Secret reveals, Redactions, Authentication by scheme and result, Authentication failures by reason, Tokens issued by principal, Rate limited by scope, Auth cache hit ratio.
- **Authorization:** Authorization decisions, Denials by resource and operation.
- **Secrets and audit:** Secret operations, Redactions by kind, Audit events by type, Lockdown and policy audit events, Authentication and authorization log lines.

### Hugo / Database (`hugo-database`)

Variables: `job`, `provider`, `table`, `window`.

- **Statements:** Statements per second, Statement p95, Errors, Connections in use, Transaction retries, Write lock wait p95, Statements by operation, Statement latency (p50/p95/p99), Slowest tables (p95), Errors by table.
- **Connections and maintenance:** Connections in use, Write lock wait, Retention and request history.

### Hugo / Integrations (`hugo-integrations`)

Variables: `job`, `service`, `window`.

- **Outbound calls:** Outbound calls, Failed calls, Error ratio, Latency p95, Observability services reachable and unreachable, Calls by service, Calls by result, Outbound latency (p50/p95/p99), Latency p95 by service and operation, Observability reachability, Operations (table).

### Hugo / Control Plane (`hugo-control-plane`)

Variables: `job`, `service`, `window`.

- **Process:** Server up, Uptime, Memory, CPU, Threads, Exceptions per minute, Memory (process, working set, GC heap), CPU and GC, Exceptions by type.
- **Background services:** Iterations by service and result, Iteration duration p95 by service, Background errors, Job duration by job.
- **Telemetry pipeline:** Telemetry diagnostics, Label values folded.

### Hugo / Logs and Traces (`hugo-logs`)

Variables: `component`, `level`, `search`.

- **Log volume:** Log lines by level, by component, errors and warnings per minute, by tenant.
- **Logs:** Warnings and errors, Filtered logs.
- **Traces:** Failed and rejected spans, Slow requests (Tempo TraceQL tables).
- **Telemetry pipeline:** Telemetry diagnostics, Scrape targets up.

## Families charted before they are emitted

Some plan section 21 families are charted ahead of the code that records them. Until they appear, their panels show 0:

`hugo_tool_invocations_total`, `hugo_tool_duration_seconds`, `hugo_tool_output_bytes`, `hugo_connector_sessions_active`, `hugo_approvals_total`, `hugo_approval_wait_seconds`, `hugo_mcp_requests_total`, `hugo_a2a_requests_total`, `hugo_instructions_compile_duration_seconds`, and `hugo_resource_health`.

The agent and model families (`hugo_agent_*`, `hugo_model_*`) are recorded by `AgentTelemetry` and appear after the first agent turn against a configured model. `hugo_authz_requests_total` is in the catalog but nothing records it yet.

## Checks

### Dashboard JSON (`scripts/check-grafana-mirror.sh`)

```
scripts/check-grafana-mirror.sh
```

Checks that every file in `assets/grafana/` parses, has a UID matching its file name and unique across the directory, references only the datasource UIDs `prometheus`, `tempo`, `loki` (or Grafana's built-in ones), and gives every panel a description. When a mirror directory exists (`docker/grafana/` or `docker/factory/grafana/`, plan task P10.19), its files must be byte-identical to `assets/grafana/`. Exit code 0 on success, 1 on any failure, so the script can run in CI.

### Telemetry verifier (`scripts/verify-telemetry.sh`, `.bat`)

```
scripts/verify-telemetry.sh --new-password 'Choose-A-New-Password-1'
scripts\verify-telemetry.bat --new-password Choose-A-New-Password-1
```

Drives a small workload through the REST API (health routes, a rejected sign-in, unauthenticated and badly authenticated requests, a sign-in as `anderson@hugo.local`, authenticated reads including two that a tenant administrator is denied, a short server-sent events stream, and a token refresh), waits until Prometheus has scraped the server after the workload, then checks the metric families:

- **required:** every `hugo_*` family in [TELEMETRY.md](TELEMETRY.md) that the workload always produces, plus the Watson and runtime families the dashboards query (`http_server_request_duration_seconds`, `watson_server_up`, `process_uptime_seconds`, and so on). A missing one fails the run (exit 1).
- **conditional:** catalog families that need an event the workload does not cause (a database error, a secret reveal, an agent turn). Missing ones are warnings.
- **unemitted:** catalog families nothing records yet (`hugo_authz_requests_total`). Warnings; the script says when one starts appearing.
- **pending:** section 21 families that are not in the catalog. Warnings. When one is added to the catalog it becomes required automatically unless it is listed as conditional.

The seeded users start with the password `password` and are only prompted to change it. Pass `--new-password` (or `HUGO_NEW_PASSWORD`) if you want the script to change it through `POST /v1.0/api/users/me/password`; on later runs it signs in with the new password directly.

Options: `--base-url` (default `http://127.0.0.1:8800`), `--prometheus-url` (default `http://127.0.0.1:9090`), `--job` (default `hugo-server`), `--tenant`, `--email`, `--password`, `--new-password`, `--iterations`, `--scrape-timeout`, `--catalog`. Each has an environment variable (`HUGO_BASE_URL`, `HUGO_PROMETHEUS_URL`, ...); run with `--help` for the list. Exit code 2 means the server or Prometheus could not be reached.
