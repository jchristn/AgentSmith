# Troubleshooting

Most Hugo problems announce themselves clearly if you know where to look. The server writes a plain log line at startup for every decision it makes about keys, migrations, and seeding; every HTTP error carries a stable `code` and a `requestId`; and every tool call that was refused records why. This guide is organized by what you see, and each section says what Hugo is doing and how to get past it.

Two habits make everything below faster. First, read the server log from the top of the last start (`docker logs hugo-server` in the Compose stack). Second, when a request fails, copy its `requestId` (also in the `x-request-id` response header) and look it up under Request History in the dashboard, or with `GET /v1.0/api/request-history?pathContains=...`. The captured request shows the headers and bodies Hugo saw, with secrets scrubbed.

## The server will not start

### "No secrets master key is configured"

Hugo encrypts every stored secret under a master key and will not run without one. In the Compose stack, `docker/start.sh` generates `HUGO_MASTER_KEY` into `docker/.env` on first run. Outside Compose, set `HUGO_MASTER_KEY` to a base64 value of at least 32 bytes:

```bash
openssl rand -base64 32
```

The related messages "is not valid base64" and "decodes to N bytes; at least 32 are required" mean the value is set but malformed. A stray quote or a truncated paste is the usual cause.

For local development the server will instead create `data/master.key` next to its settings file and log a loud `WARNING: the secrets master key comes from the development key file`. That is fine on a laptop and wrong anywhere else.

### "does not decrypt the stored canary for that version"

The database remembers an encrypted canary for each key version it has seen. This message means the key you configured is not the key the database was encrypted with, under the same version number. Hugo refuses to start rather than run with secrets it cannot read.

You almost certainly replaced `docker/.env` (or lost it) or pointed the server at a database from another installation. Restore the original `HUGO_MASTER_KEY`. If you meant to move to a new key, that is a rotation, not a replacement; follow the steps in [`UPGRADING.md`](UPGRADING.md#rotating-the-master-key).

### "is new, and no configured key decrypts an existing canary"

You raised `HUGO_MASTER_KEY_VERSION` and supplied a new key, but did not give Hugo the old key as `HUGO_MASTER_KEY_PREVIOUS`, and encrypted rows exist. Set the previous key (and its version, if it was not the current version minus one) and start again. If the database has no encrypted rows yet, Hugo accepts the new key and logs that the old canaries are superseded.

### "HUGO_MASTER_KEY_PREVIOUS is set but there is no previous key version"

The previous key needs a version number different from the current one. Either raise `HUGO_MASTER_KEY_VERSION` to 2 or more, or set `HUGO_MASTER_KEY_PREVIOUS_VERSION` explicitly.

### Settings validation errors

Startup stops with a message naming the setting when the configuration cannot work: `Auth.SigningKey must be at least 16 characters`, `Auth.AdminApiKey must be empty (disabled) or at least 16 characters`, `Rest.ExternalBaseUrl must be an absolute URL`, `Database.Host is required when Database.Type is Postgresql`, or a port collision between `Mcp.InternalPort` and `Rest.Port`. An environment variable holding the wrong type (for example `HUGO_REST_PORT=abc`) fails the same way and names the variable. Every setting and its variable is listed in [`CONFIGURATION.md`](CONFIGURATION.md).

### Warnings that are not errors

The warning about `Auth.SigningKey` being the built-in legacy-token compatibility default means the old validation key remains publicly known. Set `HUGO_AUTH_SIGNING_KEY` to a private value. Compose generates one on first start. Changing it invalidates pre-upgrade CBC/HMAC tokens; new sessions use the master key and remain valid. Keep the existing private signing key during an upgrade if existing sessions must survive.

## The health check is failing

`GET /v1.0/api/health` answers 200 only when the database responds, the schema is current, and the server is not shutting down. Otherwise it answers 503 with a body that says which part is unhappy:

```json
{"status":"healthy","node":"hugo-server","version":"0.1.0-alpha","database":"ok","schemaVersion":8,"expectedSchemaVersion":8,"background":"ok","utc":"2026-10-15T04:49:34.145382Z"}
```

| `status` | Meaning |
|---|---|
| `degraded` | `database` is `unreachable`. Check the PostgreSQL container, credentials, or the SQLite volume. |
| `migrating` | `schemaVersion` is below `expectedSchemaVersion`. Migrations run at startup; if this persists, the log shows the failing migration. |
| `draining` | A restart was requested and in-flight requests are finishing. |

`background: "stalled"` means the retention and housekeeping loop has stopped making progress. Requests still work, but approvals may not expire and history may not be pruned; restart the server and read the log for the exception.

`/v1.0/api/health/live` answers 200 whenever the process runs, which is what you want for a liveness probe. Use `/v1.0/api/health` for readiness.

## Signing in and authentication

### 401, 403, and 404 mean different things

Hugo is deliberate about these, and the difference tells you where to look.

| Status | Body `code` | What it means |
|---|---|---|
| 401 | `AuthenticationRequired` | No credentials, or credentials Hugo could not verify. |
| 403 | `AuthorizationDenied` | You are who you say you are, and the target exists in your tenant, but your roles do not grant the operation. The body names `requiredPermission` and `resourceType`. |
| 404 | `NotFound` | The object does not exist, or it belongs to another tenant. Hugo never confirms that another tenant's object exists. |
| 404 | `RouteNotFound` | No route matches the method and path. Check for a typo or a missing `/v1.0`. |

A 401 also happens when more than one scheme is presented at once (for example a bearer token and an access key), when `Authorization` and `x-token` carry different tokens, or when an `x-tenant-id` header disagrees with the session's tenant. The server log records the precise reason as an `AuthFailure` audit event; it is kept out of the response on purpose.

A 403 on a route you expect to work usually means the user is not a tenant administrator and has no role that grants the operation. `GET /v1.0/api/tenants/{tenantId}/users/{id}/permissions` shows the effective grants. [`SECURITY.md`](SECURITY.md) explains roles and bypass rules.

### The dashboard keeps asking for a new password

The seeded accounts `admin@hugo.local` and `anderson@hugo.local` start with the password `password` and the `mustChangePassword` flag, and an administrator's password reset sets the same flag unless it passes `mustChangePassword=false`. The flag is advisory (decision D-048): the dashboard asks for a new password at sign-in, you may skip it, and the API answers every route as usual. The prompt comes back until the password changes:

```bash
curl -s -X POST http://127.0.0.1:8800/v1.0/api/users/me/password \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"currentPassword":"password","newPassword":"something-long-and-yours"}'
```

### 429 RateLimited on sign-in

Token issuance, tenant lookup, and re-authentication share a fixed one-minute window, `Auth.RateLimitPerMinute` (default 10), counted per source address and per identity (the email or access key). The response has `Retry-After: 60`. Secret reveals have their own limit, `Secrets.RevealRateLimitPerMinute`.

The source address is the TCP peer. In the Compose stack, logins made through the dashboard reach the server from the dashboard container, so everyone signing in through the dashboard shares one address bucket. On a busy team, or with a script hammering the login, raise the limit rather than waiting it out.

### Sessions end sooner than expected

User sessions last `Auth.UserSessionMinutes` (60) and administrator sessions `Auth.AdminSessionMinutes` (30). `POST /v1.0/token/refresh` extends a session, but never past `Auth.SessionAbsoluteMaxHours` (12) after sign-in. Changing `HUGO_AUTH_SIGNING_KEY` invalidates every session.

### Revealing a secret fails

Reveal requires the `Reveal` permission on `Secret`, a `reason`, and a password re-entry within `Secrets.RevealReauthMinutes` (5) through `POST /v1.0/token/reauthenticate`. A credential cannot re-authenticate with a password, so reveals are for people.

## The database is busy or unreachable

Any route can answer 503 with code `DatabaseBusy` (the pool has no free connections) or `DatabaseUnavailable` (the database cannot be reached), plus a `Retry-After` header (`Rest.DatabaseRetryAfterSeconds`, default 2). Both are safe to retry for reads. For a create, check whether the object exists before retrying, because Hugo does not deduplicate repeated creations. Persistent `DatabaseBusy` on PostgreSQL means `Database.MaxPoolSize` (100) is too small for the load or a query is holding connections; persistent `DatabaseUnavailable` is a network or credentials problem, and the health route will say `degraded`.

## MCP clients

### "Session not found" after a restart

MCP sessions live in the server's memory (D-046). After a restart, a client that still holds an old `Mcp-Session-Id` gets HTTP 404 with:

```json
{"jsonrpc":"2.0","error":{"code":-32001,"message":"Session not found. Send an initialize request without an MCP-Session-Id header to start a new session."},"id":1}
```

Well-behaved clients reinitialize on their own. If yours does not, restart or reconnect it (in Claude Code, `/mcp` and reconnect).

### Errors from the MCP endpoint itself

Problems with the caller or the profile are HTTP errors with a JSON-RPC body, not tool errors, so the client shows them as a failed connection.

| HTTP | Message prefix | Fix |
|---|---|---|
| 401 | `Unauthorized` | Send `Authorization: Bearer <token>`, or `x-access-key` with `x-secret-key`. The response carries `WWW-Authenticate: Bearer realm="hugo"`. |
| 400 | `TenantRequired` | A system administrator must name the tenant with `x-tenant-guid`. |
| 404 | `ProfileNotFound` | The profile in `/mcp/{profile}` or `x-hugo-profile` does not exist or is inactive. |
| 403 | `ProfileLocked` | The credential has `lockToProfile` and the request asked for another profile. Drop the profile from the URL or bind the credential to the profile you want. |
| 403 | `Forbidden` | The caller lacks `Execute` on that profile. |
| 403 | `McpNotExposed` | The profile has `exposeMcp` turned off. |
| 404 | `McpDisabled` | `Mcp.Enable` is false on this server. |
| 502 | `McpUnavailable` | The internal MCP listener did not answer. Look for an exception in the log. |

Client configuration for Claude Code and others is in [`MCP_CLIENTS.md`](MCP_CLIENTS.md), and the protocol details are in [`MCP_API.md`](../MCP_API.md).

### A tool is missing from `tools/list`

Tools are filtered per caller. A family's tools appear only when the profile has a binding of that family; Write and Destructive tools are hidden when every binding of the family is ReadOnly; a tool denied by a tool rule on every binding is hidden; tools that need a model (such as `sql_ask`) are hidden when the profile has no inference endpoint; and `ask_agent_smith` needs an inference endpoint too. `GET /v1.0/api/agent-profiles/{id}/tools` shows exactly what a caller would see and why.

## Tool calls are refused or never finish

Every refused call is recorded under Tool Invocations with a `decisionCode`:

| `decisionCode` | Meaning |
|---|---|
| `readonly_binding` | The binding (or the resource's access mode ceiling) is ReadOnly and the call is Write or Destructive. |
| `tool_denied` | A tool rule on the binding denies this tool. |
| `tenant_lockdown` | Lockdown is on, so every non-Read call is denied. |
| `classifier_rejected` | The connector's safety rules refused the call outright (for example `sudo` without `-n`, or a statement the SQL classifier cannot parse). |
| `approval_required` | The call is waiting for a human. |

Lockdown is the one people forget. A tenant administrator turns it on from the dashboard topbar or `POST /v1.0/api/tenant/lockdown`; it cancels pending approvals and denies every Write and Destructive call until someone turns it off. `GET /v1.0/api/tenant/lockdown` tells you whether it is on.

Approvals expire after `Approvals.ExpirySeconds` (900). An expired approval never runs, and the original caller sees the call as expired. Over MCP, a call waits up to `Approvals.McpWaitSeconds` (90) and then returns `approval_pending`; the caller continues with `hugo_await_approval`. If nobody approves within the expiry window, ask again. Whether you can approve your own requests is controlled by `Approvals.AllowSelfApproval` (default true). [`SAFETY_MODEL.md`](SAFETY_MODEL.md) covers the whole flow.

A tool that runs too long ends with a `timeout` error result. The profile's `toolTimeoutSeconds` and `Connectors.DefaultToolTimeoutSeconds` (120) bound it.

## Connectors

### SSH: host key mismatch

Hugo pins each SSH host's key (D-028). With no fingerprint configured, the first successful connection records it and audits `HostKeyPinned`. If the host later presents a different key, every call fails closed with `host_key_mismatch` and a message showing the expected and observed fingerprints. If the host was rebuilt on purpose, update `hostKeyFingerprint` on the resource (or clear it to pin again on the next connection). If it was not, treat it as an incident.

### SSH: the host stops answering after a few failures

OpenSSH 9.8 and later enable `PerSourcePenalties` by default. Hugo connects from a single address, so a run of authentication failures (a rotated password, a wrong key) can make the host refuse Hugo for a while, for every resource that points at it. Fix the credential first. If lockouts keep happening, add Hugo's address to `PerSourcePenaltyExemptList` in `sshd_config`. [`CONNECTORS.md`](CONNECTORS.md) has the rest of the per-connector notes.

### Test Connection fails but the system is up

Test Connection runs from inside the Hugo container, not from your browser. A database on your laptop is not at `127.0.0.1` from Hugo's point of view; use `host.docker.internal` (on Linux, add the `host-gateway` mapping). Systems behind a VPN are reachable only if the Docker host is on that VPN. [`DOCKER.md`](DOCKER.md) shows the override files for both. Tests are cut off after `Connectors.TestTimeoutSeconds` (30).

## Model endpoints and chat

`POST /v1.0/api/model-endpoints/{id}/validate` sends a tiny prompt and reports what happened. An unreachable provider answers 200 with `success: false`:

```json
{"success":false,"kind":"Inference","latencyMs":12,"model":"llama3","error":"Connection refused (127.0.0.1:1)","problems":[]}
```

Read `error` and `problems`; they name the base URL, model, or key problem. The same container networking rules apply: an Ollama on the Docker host is `http://host.docker.internal:11434`, not `127.0.0.1`.

`GET /v1.0/api/model-endpoints/{id}/models` currently answers 500 `InternalError` when the provider is unreachable, instead of a 502 or 503. It is a known bug; validate the endpoint first, and the model list will work once the provider answers.

Chat, A2A, and the OpenAI-compatible endpoint all need the profile to have an inference endpoint. MCP raw tools do not, because the caller's model drives. A turn that hits the profile's `maxIterations` (12) is forced to answer without more tool calls.

## Telemetry and ports

The server listens on `Rest.Port` (8800). Prometheus metrics come from a separate listener on `Telemetry.PrometheusHostname:PrometheusPort` (127.0.0.1:9464 by default), relayed at `/metrics` on the main port while `Telemetry.ServeMetricsOnServicePort` is true; `/metrics` answers 503 `MetricsUnavailable` when that listener is off. Traces go to `Telemetry.OtlpEndpoint` only when `Telemetry.OtlpEnable` is true (setting `HUGO_OTLP_ENDPOINT` turns it on). If two Hugo processes run on one machine, give each its own `PrometheusPort` or the second one cannot start its listener. [`TELEMETRY.md`](TELEMETRY.md) and [`GRAFANA.md`](GRAFANA.md) cover the metrics and dashboards.

In the Compose stack every host port binds to `127.0.0.1` and can be moved in `docker/.env` (`HUGO_REST_PORT`, `HUGO_DASHBOARD_PORT`, `HUGO_GRAFANA_PORT`, and so on) when something else already owns 8800 or 3000.

## When none of this matches

Collect the server log from the last start, the `requestId` of the failing request, and the output of `GET /v1.0/api/info` (version, database type, features). Those three almost always contain the answer, and they are what anyone helping you will ask for first.
