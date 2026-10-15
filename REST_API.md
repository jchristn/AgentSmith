# Hugo REST API

Hugo serves everything on one port (8800 in the Docker stack): the management API under `/v1.0/api`, tokens under `/v1.0/token`, MCP at `/mcp`, A2A at `/v1.0/a2a`, and an OpenAI-compatible chat surface at `/v1.0/openai/v1`. This document covers every HTTP route the server registers, 204 of them in `0.1.0-alpha`, grouped by area. MCP tools and A2A methods have their own references in [MCP_API.md](MCP_API.md) and [A2A_API.md](A2A_API.md); here they appear only as HTTP endpoints.

I wrote the descriptions by hand, but most of the mechanical detail is generated: field lists and status codes come from the server's OpenAPI document (`GET /openapi.json`), permissions come from the server's operation scope map, and every example is a real request and response recorded by running the Postman collection (`assets/postman/Hugo.postman_collection.json`) against a fresh server. When a route behaves differently from what its OpenAPI entry claims, the text says so; the known differences are collected in [Known differences from the OpenAPI document](#known-differences-from-the-openapi-document) at the end.

The fastest way to try any of this is the collection: import it with `Hugo.postman_environment.json`, or run `scripts/newman.sh`.

## Contents

- [Health and server information](#health-and-server-information)
- [Tokens and sign-in](#tokens-and-sign-in)
- [Users](#users)
- [Credentials](#credentials)
- [Roles and permissions](#roles-and-permissions)
- [Assignments](#assignments)
- [Sessions](#sessions)
- [Tenants and lockdown](#tenants-and-lockdown)
- [Secrets](#secrets)
- [Master key](#master-key)
- [Connectors](#connectors)
- [Resources](#resources)
- [Resource knowledge](#resource-knowledge)
- [Agent profiles](#agent-profiles)
- [Bindings](#bindings)
- [Prompt templates](#prompt-templates)
- [Model endpoints](#model-endpoints)
- [Tool invocations](#tool-invocations)
- [Approvals](#approvals)
- [Conversations and chat](#conversations-and-chat)
- [Events](#events)
- [Analytics](#analytics)
- [Audit](#audit)
- [Request history](#request-history)
- [Settings and system](#settings-and-system)
- [OpenAI-compatible surface](#openai-compatible-surface)
- [MCP endpoint](#mcp-endpoint)
- [A2A endpoint](#a2a-endpoint)

## Conventions

### Base URL and examples

Examples use three shell variables. `HUGO` is the base URL, `TOKEN` is a session token for the tenant administrator `anderson@hugo.local`, and `ADMIN_TOKEN` is one for the system administrator `admin@hugo.local`:

```bash
export HUGO=http://127.0.0.1:8800
export TOKEN=$(curl -s -X POST "$HUGO/v1.0/token" -H "x-email: anderson@hugo.local" \
  -H "x-password: $PASSWORD" -H "x-tenant-id: default" | python3 -c 'import json,sys; print(json.load(sys.stdin)["token"])')
```

IDs in the examples (`res_...`, `agp_...`, `apr_...`) come from the recorded run and will differ on your server. Long arrays and strings in the recorded responses are shortened and marked with `…`; tokens and one-time secret keys are replaced with placeholders. Nothing else is edited. The chat, A2A, and OpenAI-compatible examples ran against a scripted OpenAI-compatible stub model (the model endpoint at `http://127.0.0.1:63806` named `stub-model`), so the assistant's words are canned; everything around them, including the tool calls, is what Hugo actually did.

### Authentication

Every route except health, the OpenAPI document, the token and tenant lookup routes, `/metrics`, and the A2A agent card needs a principal. Hugo accepts one scheme per request; sending two (for example a bearer token and an access key) is rejected with 401.

| Scheme | Headers | Notes |
|---|---|---|
| Bearer session | `Authorization: Bearer <token>` or `x-token: <token>` | Tokens come from `POST /v1.0/token`. They last `Auth.UserSessionMinutes` (60) for users and `Auth.AdminSessionMinutes` (30) for administrators, and can be refreshed until `Auth.SessionAbsoluteMaxHours` (12) after sign-in. |
| User login | `x-email`, `x-password`, `x-tenant-id` (or `x-tenant-guid`) | Only on `POST /v1.0/token`. The same fields can be sent as a JSON body. |
| Credential keys | `x-access-key`, `x-secret-key` | On any route, for credentials whose auth mode is DirectHeader, SessionExchange, or Hybrid. |
| Signed request | `x-access-key`, `x-date`, `x-nonce`, `x-signature`, optional `x-tenant-guid` | `x-signature` is the lowercase hex HMAC-SHA256, keyed with the secret key, of `METHOD\nPATH\nQUERY\nTENANT\nDATE\nNONCE\nSHA256(BODY)`. The date must be within `Auth.SignedRequestSkewSeconds` (300) of the server clock and a nonce works once. For SignedRequest and Hybrid credentials. |
| Administrator API key | `x-api-key` | Disabled unless `HUGO_ADMIN_API_KEY` is set. Acts as a system administrator; send `x-tenant-guid` to pick a tenant. |

A user whose `mustChangePassword` flag is set can reach only the token routes, `GET /v1.0/api/users/me`, `POST /v1.0/api/users/me/password`, and the server-info routes until the password changes; everything else answers 403 `PasswordChangeRequired`. Seeded accounts start that way.

Token routes (`POST /v1.0/token`, `POST /v1.0/tenants/lookup`, `POST /v1.0/token/reauthenticate`) are rate limited per source IP and per identity, `Auth.RateLimitPerMinute` (10) in a fixed one-minute window, and answer 429 with `Retry-After: 60`. Secret reveal has its own limit.

### Tenancy

Routes come in two shapes, a consequence of Hugo's foundation being forked from Garrison (decision D-039). Identity and administration routes carry the tenant in the path, `/v1.0/api/tenants/{tenantId}/...`. Hugo's own domain routes are flat, `/v1.0/api/resources`, and take the tenant from the caller's token. System administrators may add `?tenantId=` to a flat route to act on another tenant; that bypass is audited. Anyone else's `tenantId` query parameter is ignored, so a caller can never choose its tenant. A record in another tenant answers 404, never 403, so tenants cannot probe each other.

### Authorization

Each route is mapped to a resource type and an operation (for example Read on Resource). The **Authentication** line under every route names it. Tenant administrators and system administrators pass these checks through bypass rules (audited); everyone else needs a role that grants the pair, see [docs/SECURITY.md](docs/SECURITY.md). A denial in your own tenant answers 403 with the permission that was missing:

```json
{
  "error": "Forbidden",
  "code": "AuthorizationDenied",
  "message": "No grant covers Read on Resource.",
  "requestId": "78dd4262bb7b4d2283c76bd7ae2b7d3a",
  "requiredPermission": "Read",
  "resourceType": "Resource"
}
```

### Errors

Every error from the management API has the same shape. `error` is the HTTP status name, `code` is a stable machine code the dashboard localizes, `message` is for people, and `requestId` matches the `x-request-id` response header and the server logs. Validation failures add `fields` (and its duplicate `errors`) with a message per field, and `fieldCodes` with a code per field:

```json
{
  "error": "BadRequest",
  "code": "SettingsInvalid",
  "message": "The resource settings are invalid: API token is required.",
  "requestId": "cfbc7dcc26e04ed2827911c4e65df0bb",
  "fields": { "apiToken": "API token is required." },
  "errors": { "apiToken": "API token is required." },
  "fieldCodes": { "apiToken": "required" }
}
```

| Status | When | Typical codes |
|---|---|---|
| 400 | Invalid JSON, a field of the wrong type or an unknown enum value, a failed validation | `ValidationFailed`, `SettingsInvalid`, `InvalidJson`, `InvalidBody`, `BadRequest` |
| 401 | No principal, or an invalid or expired one | `AuthenticationRequired`, `InvalidCredentials` |
| 403 | Authenticated but not allowed | `AuthorizationDenied`, `PasswordChangeRequired`, `RouteNotMapped` |
| 404 | No such record in your tenant, or no such route | `NotFound`, `TenantNotFound`, `RouteNotFound` |
| 409 | Name taken, record in use, wrong state | `NameConflict`, `SelfApprovalNotAllowed`, `NoBuiltInDefault`, and others |
| 429 | Too many token-route attempts | `RateLimited` |
| 500 | Unexpected failure (details in the log under the request ID) | `InternalError`, `DecryptionFailed` |
| 503 | Database busy or unreachable (with `Retry-After`), master key missing, server draining | `DatabaseBusy`, `DatabaseUnavailable`, `MasterKeyUnavailable` |

The OpenAI-compatible surface uses OpenAI's error shape instead (`{ "error": { "message", "type", "param", "code" } }`), and the MCP and A2A endpoints answer with JSON-RPC errors.

Tool calls are a special case worth calling out. A call that policy denies is not an HTTP error: `POST /v1.0/api/tool-invocations` answers 200 with `decision: Denied` and a `decisionCode` such as `readonly_binding`, `tool_denied`, `binding_deny_pattern`, or `tenant_lockdown`. HTTP errors are reserved for requests Hugo cannot evaluate at all.

### Lists and paging

Most list routes return an enumeration envelope and accept `maxResults` (1 to 1000, default 25), `skip`, `ordering` (`CreatedDescending` by default, `CreatedAscending`, `NameAscending`, `NameDescending`), `search`, and `activeOnly`:

```json
{ "success": true, "maxResults": 25, "skip": 0, "totalRecords": 3, "recordsRemaining": 0, "endOfResults": true, "timestampUtc": "2026-10-15T05:07:47.975055Z", "objects": [ ] }
```

Audit and request history are the exceptions: they page with `pageNumber` (1-based) and `pageSize` and answer `{ "pageNumber", "pageSize", "totalCount", "totalPages", "objects" }`. A handful of small collections (bindings, knowledge entries, template versions, referrers) return a plain JSON array.

### Bodies, status codes, and tracing

Bodies are JSON with camelCase names and enums as strings; unknown enum values are rejected. Creates answer 201 with the created record, deletes of a single record answer 204 with no body, and calls that start work in the background or wait for a human answer 202. Updates (`PUT`) are partial for most records: omitted fields keep their values. Secrets and stored prompt templates are the exceptions noted on their routes.

Every response carries `x-request-id`; send your own (up to 64 characters) to correlate. An inbound W3C `traceparent` header is honored, so a caller's trace continues through Hugo into Tempo.

## Health and server information

Health routes are anonymous so container runtimes and load balancers can use them. `/v1.0/api/health/live` only says the process runs; `/v1.0/api/health` (and `/ready`) also checks that the database answers, that the schema version matches the binary, and that the server is not draining for a restart, and returns 503 when any of those fails. Use the second one for routing and the first one for restart decisions, or a short database outage turns into a restart loop.

`GET /openapi.json` and `GET /swagger` serve the generated OpenAPI 3 document and a Swagger UI over it, anonymously. `/metrics` is Prometheus text relayed from the telemetry listener; it is unauthenticated, which is fine on the Compose network and something to firewall anywhere else.

| Method | Path | Summary |
|---|---|---|
| GET | `/openapi.json` | GET /openapi.json |
| GET | `/swagger` | GET /swagger |
| GET | `/v1.0/api/health` | Health: 200 when the database answers, the schema is current, and the server is not draining; 503 otherwise |
| GET | `/v1.0/api/health/ready` | Readiness (same as /v1.0/api/health) |
| GET | `/v1.0/api/health/live` | Liveness: 200 while the process runs |
| GET | `/metrics` | Prometheus metrics, relayed from the metrics listener (404 when Telemetry.ServeMetricsOnServicePort is false) |
| GET | `/v1.0/api/server-info` | Server version, endpoints, and feature flags |
| GET | `/v1.0/api/info` | Server version, endpoints, and feature flags (same as /v1.0/api/server-info) |
| GET | `/v1.0/api/observability/status` | Reachability and version of the configured observability services (cached for 30 seconds) |

### GET `/openapi.json`

The generated OpenAPI 3 document for every route. No authentication.

**Authentication:** Not in the scope map.

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden.

Example:

```bash
curl -s "$HUGO/openapi.json"
```

Response (200):

```text
{
  "openapi": "3.0.3",
  "info": {
    "title": "Hugo API",
    "version": "0.1.0-alpha",
    "description": "Hugo, a multi-tenant super agent server: identity, access control, secrets, prompt templates, and administration."
  },
  "paths": {
    "/openapi.json": {
      "get": {
        "summary": "GET /openapi.json",
        "responses": {
          "200": {
            "description": "Successful response"
          }
        }
      }
    },
    "/swagger": {
      "get": {
        "summary": "GET /swagger",
        "responses": {
          "200": {
            "description": "Successful response"
          }
        }
      }
    },
    "/v1.0/api/health": {
      "get": {
        "summary": "Health: 200 when the database answers, the schema is current, and the server is not draining; 503 otherwise",
        "tags": [
          "Health"
        ],
        "responses": {
          "200": {
            "description": "Success.",
            "content": {
              "application/json": {
                "schema": {
                  "type": "object",
                  "properties": {
                    "status": {
                      "type": "string"
                    },
                    "node": {
                      "type": "string"
                    },
                    "version": {
                      "type": "string"
                    },
                    "database": {
                      "type": "string"
                    },
                    "schemaVersion": {
…
```

### GET `/swagger`

Swagger UI over `/openapi.json` (HTML). No authentication.

**Authentication:** Not in the scope map.

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden.

Example:

```bash
curl -s "$HUGO/swagger"
```

Response (200):

```text
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Hugo API</title>
    <link rel="stylesheet" href="https://unpkg.com/swagger-ui-dist@5.17.14/swagger-ui.css" />
    <style>
        html {
            box-sizing: border-box;
            overflow: -moz-scrollbars-vertical;
            overflow-y: scroll;
        }
        *, *:before, *:after {
            box-sizing: inherit;
        }
        body {
            margin: 0;
            background: #fafafa;
        }
        .swagger-ui .topbar {
            background-color: #1b1b1b;
        }
        .swagger-ui .topbar .download-url-wrapper .select-label {
            display: flex;
            align-items: center;
            width: 100%;
            max-width: 600px;
        }
        .swagger-ui .topbar .download-url-wrapper .select-label select {
            flex: 2;
        }
        .swagger-ui .topbar .download-url-wrapper .select-label .download-url-button {
            background: #547f00;
        }
        #swagger-ui {
            max-width: 1460px;
            margin: 0 auto;
            padding: 20px;
        }
    </style>
</head>
<body>
    <div id="swagger-ui"></div>
    <script src="https://unpkg.com/swagger-ui-dist@5.17.14/swagger-ui-bundle.js"></script>
    <script src="https://unpkg.com/swagger-ui-dist@5.17.14/swagger-ui-standalone-preset.js"></script>
    <sc
…
```

### GET `/v1.0/api/health`

Readiness check. Returns 200 with `status: healthy` when the database answers, the schema version matches the binary, and the server is not draining; 503 otherwise. No authentication.

**Authentication:** None (anonymous).

**Status codes:** 200 OK.

**Response fields:** `status`, `node`, `version`, `database`, `schemaVersion`, `expectedSchemaVersion`, `background`, `utc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/health"
```

Response (200):

```json
{
  "status": "healthy",
  "node": "joels-mbp",
  "version": "0.1.0-alpha",
  "database": "ok",
  "schemaVersion": 8,
  "expectedSchemaVersion": 8,
  "background": "ok",
  "utc": "2026-10-15T05:17:45.743345Z"
}
```

### GET `/v1.0/api/health/ready`

Same check as `/v1.0/api/health`, under the conventional readiness path.

**Authentication:** None (anonymous).

**Status codes:** 200 OK.

**Response fields:** `status`, `node`, `version`, `database`, `schemaVersion`, `expectedSchemaVersion`, `background`, `utc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/health/ready"
```

Response (200):

```json
{
  "status": "healthy",
  "node": "joels-mbp",
  "version": "0.1.0-alpha",
  "database": "ok",
  "schemaVersion": 8,
  "expectedSchemaVersion": 8,
  "background": "ok",
  "utc": "2026-10-15T05:17:45.758864Z"
}
```

### GET `/v1.0/api/health/live`

Liveness: 200 for as long as the process runs, even when the database is unreachable. Use it for restart decisions, not for routing.

**Authentication:** None (anonymous).

**Status codes:** 200 OK.

**Response fields:** `status`, `node`, `version`, `database`, `schemaVersion`, `expectedSchemaVersion`, `background`, `utc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/health/live"
```

Response (200):

```json
{
  "status": "alive",
  "node": "joels-mbp",
  "version": "0.1.0-alpha",
  "database": "unchecked",
  "schemaVersion": 0,
  "expectedSchemaVersion": 8,
  "background": "unknown",
  "utc": "2026-10-15T05:17:45.771163Z"
}
```

### GET `/metrics`

Prometheus exposition text relayed from the metrics listener. Returns 404 when `Telemetry.ServeMetricsOnServicePort` is false. No authentication: keep the port on a private network.

**Authentication:** None (anonymous).

**Status codes:** 200 OK.

Example:

```bash
curl -s "$HUGO/metrics"
```

Response (200):

```text
# TYPE target_info gauge
# HELP target_info Target metadata
target_info{service_name="hugo-server",service_instance_id="joels-mbp",telemetry_sdk_name="opentelemetry",telemetry_sdk_language="dotnet",telemetry_sdk_version="1.19.1"} 1
# TYPE dotnet_assembly_count gauge
# HELP dotnet_assembly_count The number of .NET assemblies that are currently loaded.
dotnet_assembly_count{otel_scope_name="System.Runtime"} 103
# TYPE dotnet_exceptions_total counter
# HELP dotnet_exceptions_total The number of exceptions that have been thrown in managed code.
dotnet_exceptions_total{otel_scope_name="System.Runtime",error_type="TaskCanceledException"} 1314
dotnet_exceptions_total{otel_scope_name="System.Runtime",error_type="ArgumentException"} 26
dotnet_exceptions_total{otel_scope_name="System.Runtime",error_type="ValidationException"} 43
dotnet_exceptions_total{otel_scope_name="System.Runtime",error_type="ConnectorException"} 1
dotnet_exceptions_total{otel_scope_name="System.Runtime",error_type="SocketException"} 14
dotnet_exceptions_total{otel_scope_name="System.Runtime",error_type="HttpRequestException"} 16
dotnet_exceptions_total{otel_scope_name="System.Runtime",error_type="IOException"} 14
dotnet_exceptions_total{otel_scope_name="System.Runtime",error_type="TemplateException"} 19
dotnet_exceptions_total{otel_scope_name="System.Runtime",error_type="ConflictException"} 12
dotnet_exceptions_total{otel_scope_name="System.Runtime",error_type="A2AAccessException"} 6
dotnet_exceptions_total{otel_s
…
```

### GET `/v1.0/api/server-info`

Product version, node ID, the public REST, MCP, and A2A URLs, the database type, feature flags, and the observability endpoints. Any authenticated principal may call it, including one that still has to change its password.

**Authentication:** Any authenticated principal.

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden.

**Response fields:** `product`, `version`, `nodeId`, `restUrl`, `mcpUrl`, `a2AUrl`, `openApiUrl`, `databaseType`, `telemetryEnabled`, `requestHistoryEnabled`, `routeCount`, `repositoryUrl`, `startedUtc`, `features`, `observability`.

Example:

```bash
curl -s "$HUGO/v1.0/api/server-info" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "product": "Hugo",
  "version": "0.1.0-alpha",
  "nodeId": "joels-mbp",
  "restUrl": "http://127.0.0.1:8800",
  "mcpUrl": "http://127.0.0.1:8800/mcp",
  "a2aUrl": "http://127.0.0.1:8800/v1.0/a2a",
  "openApiUrl": "http://127.0.0.1:8800/openapi.json",
  "databaseType": "Sqlite",
  "telemetryEnabled": true,
  "requestHistoryEnabled": true,
  "routeCount": 204,
  "repositoryUrl": "https://github.com/jchristn/AgentSmith",
  "startedUtc": "2026-10-15T05:07:42.055536Z",
  "features": [
    "mcp",
    "a2a",
    "… 4 more"
  ],
  "observability": {
    "grafanaUrl": null,
    "prometheusUrl": null,
    "lokiUrl": null,
    "tempoUrl": null,
    "datasources": null,
    "dashboards": null
  }
}
```

### GET `/v1.0/api/info`

Alias of `/v1.0/api/server-info`.

**Authentication:** Any authenticated principal.

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden.

**Response fields:** `product`, `version`, `nodeId`, `restUrl`, `mcpUrl`, `a2AUrl`, `openApiUrl`, `databaseType`, `telemetryEnabled`, `requestHistoryEnabled`, `routeCount`, `repositoryUrl`, `startedUtc`, `features`, `observability`.

Example:

```bash
curl -s "$HUGO/v1.0/api/info" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "product": "Hugo",
  "version": "0.1.0-alpha",
  "nodeId": "joels-mbp",
  "restUrl": "http://127.0.0.1:8800",
  "mcpUrl": "http://127.0.0.1:8800/mcp",
  "a2aUrl": "http://127.0.0.1:8800/v1.0/a2a",
  "openApiUrl": "http://127.0.0.1:8800/openapi.json",
  "databaseType": "Sqlite",
  "telemetryEnabled": true,
  "requestHistoryEnabled": true,
  "routeCount": 204,
  "repositoryUrl": "https://github.com/jchristn/AgentSmith",
  "startedUtc": "2026-10-15T05:07:42.055536Z",
  "features": [
    "mcp",
    "a2a",
    "… 4 more"
  ],
  "observability": {
    "grafanaUrl": null,
    "prometheusUrl": null,
    "lokiUrl": null,
    "tempoUrl": null,
    "datasources": null,
    "dashboards": null
  }
}
```

### GET `/v1.0/api/observability/status`

Reachability of the configured observability services (Prometheus, Grafana, Tempo, Loki). Results are cached for 30 seconds. Needs Read on Settings.

**Authentication:** Any authenticated principal.

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden.

**Response fields:** `[]service`, `[]configured`, `[]reachable`, `[]checkedUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/observability/status" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
[
  {
    "service": "grafana",
    "configured": false,
    "reachable": false,
    "version": null,
    "checkedUtc": "2026-10-15T05:17:45.92586Z"
  },
  {
    "service": "prometheus",
    "configured": false,
    "reachable": false,
    "version": null,
    "checkedUtc": "2026-10-15T05:17:45.925861Z"
  },
  "… 2 more"
]
```

## Tokens and sign-in

A session token is opaque: AES-encrypted, random per issue, and meaningless outside the server that issued it. Get one from `POST /v1.0/token` with user login headers or credential keys, send it as a bearer token, refresh it before it expires, and revoke it with `DELETE /v1.0/token` when you are done. `POST /v1.0/token/reauthenticate` re-checks the password on the current session; secret reveal requires it within the last five minutes, so a stolen token alone cannot read the vault.

`POST /v1.0/tenants/lookup` exists for login forms: given an email it lists the tenants that email can sign in to. It is anonymous and rate limited, and an unknown email gets an empty list.

| Method | Path | Summary |
|---|---|---|
| POST | `/v1.0/token` | Issue a session token from user login (x-email, x-password, x-tenant-id) or credential keys (x-access-key, x-secret-key) |
| GET | `/v1.0/token` | Validate the presented token |
| DELETE | `/v1.0/token` | Revoke the presented token (logout) |
| POST | `/v1.0/tenants/lookup` | Find the tenants an email can sign in to (login form tenant discovery) |
| GET | `/v1.0/token/details` | Describe the authenticated principal |
| POST | `/v1.0/token/refresh` | Issue a fresh token for the authenticated principal (until Auth.SessionAbsoluteMaxHours after sign-in) |
| POST | `/v1.0/token/reauthenticate` | Re-enter the password on the current session (required before revealing secrets) |

### POST `/v1.0/token`

Issue a session token. Send user login headers (`x-email`, `x-password`, `x-tenant-id`), credential keys (`x-access-key`, `x-secret-key`), or the same fields as a JSON body; body fields win and headers fill in the rest, and the default tenant applies when none is given. On first sign-in `mustChangePassword` is true and only a few routes answer until the password changes. Rate limited.

**Authentication:** None (anonymous).

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `tenantId` | string | Tenant to sign in to. Defaults to the default tenant. Header twin: `x-tenant-id` or `x-tenant-guid`. |
| `email` | string | User email (header twin `x-email`). |
| `password` | string | User password (header twin `x-password`). |
| `accessKey` | string | Credential access key (header twin `x-access-key`). When present the request is a credential sign-in. |
| `secretKey` | string | Credential secret key (header twin `x-secret-key`). |

**Status codes:** 200 OK, 400 Validation failed, 429 Rate limited.

**Response fields:** `token`, `expiresUtc`, `tenantId`, `sessionId`, `principalType`, `mustChangePassword`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/token" \
  -H "x-email: anderson@hugo.local" \
  -H "x-password: $PASSWORD" \
  -H "x-tenant-id: default"
```

Response (200):

```json
{
  "token": "<token>",
  "expiresUtc": "2026-10-15T06:17:46.026703Z",
  "tenantId": "default",
  "sessionId": "ses_mv937wru_U2mWgTmj04DaN1enN6I",
  "principalType": "User",
  "mustChangePassword": false
}
```

### GET `/v1.0/token`

Check the presented token. Returns `{ "valid": true }` or 401.

**Authentication:** Any authenticated principal.

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 429 Rate limited.

**Response fields:** `valid`.

Example:

```bash
curl -s "$HUGO/v1.0/token" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "valid": true
}
```

### DELETE `/v1.0/token`

Revoke the presented token. Later requests with it get 401.

**Authentication:** Any authenticated principal.

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 429 Rate limited.

**Response fields:** `valid`.

Example:

```bash
curl -s -X DELETE "$HUGO/v1.0/token" \
  -H "Authorization: Bearer $SESSION_TOKEN"
```

Response (200):

```json
{
  "valid": false
}
```

### POST `/v1.0/tenants/lookup`

Tenant discovery for login forms: the tenants an email can sign in to. Anonymous and rate limited. Returns an empty array for unknown emails, so it does not confirm whether an account exists elsewhere.

**Authentication:** None (anonymous).

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `email` | string | **Required.** Email to look up. |

**Status codes:** 200 OK, 400 Validation failed, 429 Rate limited.

**Response fields:** `[]tenantId`, `[]name`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/tenants/lookup" \
  -H "Content-Type: application/json" \
  -d '{"email": "anderson@hugo.local"}'
```

Response (200):

```json
[
  {
    "tenantId": "default",
    "name": "default"
  }
]
```

### GET `/v1.0/token/details`

Who the token belongs to: principal type and ID, tenant, auth scheme, administrator flags, names, and expiry.

**Authentication:** Any authenticated principal.

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden.

**Response fields:** `principalType`, `principalId`, `principalName`, `tenantId`, `userId`, `credentialId`, `mustChangePassword`, `reauthenticatedUtc`, `sessionId`, `authScheme`, `isAdmin`, `isTenantAdmin`, `email`, `firstName`, `lastName`, `expiresUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/token/details" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "principalType": "User",
  "principalId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "principalName": "anderson@hugo.local",
  "tenantId": "default",
  "userId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "mustChangePassword": false,
  "sessionId": "ses_mv937whh_oAAtDPcjMqbuP8c6clO",
  "authScheme": "BearerToken",
  "isAdmin": false,
  "isTenantAdmin": true,
  "email": "anderson@hugo.local",
  "firstName": "Thomas",
  "lastName": "Anderson",
  "expiresUtc": "2026-10-15T06:17:45.653372Z"
}
```

### POST `/v1.0/token/refresh`

Issue a fresh token for the same principal, up to `Auth.SessionAbsoluteMaxHours` after the original sign-in. Refreshing after a forced password change also clears `mustChangePassword` from the new session.

**Authentication:** Any authenticated principal.

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden.

**Response fields:** `token`, `expiresUtc`, `tenantId`, `sessionId`, `principalType`, `mustChangePassword`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/token/refresh" \
  -H "Authorization: Bearer $SESSION_TOKEN"
```

Response (200):

```json
{
  "token": "<token>",
  "expiresUtc": "2026-10-15T06:17:46.068659Z",
  "tenantId": "default",
  "sessionId": "ses_mv937wt0_KNkwtFhfsLcHlLoEmoP",
  "principalType": "User",
  "mustChangePassword": false
}
```

### POST `/v1.0/token/reauthenticate`

Re-enter the password on the current session. Required within `Secrets.RevealReauthMinutes` before revealing a secret. Rate limited like the other token routes.

**Authentication:** Any authenticated principal.

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `password` | string | **Required.** The current password of the signed-in user. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 429 Rate limited.

**Response fields:** `reauthenticatedUtc`, `revealAllowedUntilUtc`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/token/reauthenticate" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{\"password\": \"$PASSWORD\"}"
```

Response (200):

```json
{
  "reauthenticatedUtc": "2026-10-15T05:17:49.526132Z",
  "revealAllowedUntilUtc": "2026-10-15T05:22:49.526132Z"
}
```

## Users

Users are people. Each belongs to one tenant, has a PBKDF2-hashed password, and may be a tenant administrator (`isTenantAdmin`) or a system administrator (`isAdmin`). Administrators manage users under `/v1.0/api/tenants/{tenantId}/users`; everyone manages themselves through `/v1.0/api/users/me`, which is also the one place a user with a pending forced password change can reach.

The effective-permission routes are the troubleshooting tool for RBAC. They show every grant that applies to a user, where it came from (a role assignment, a bypass), and a full matrix of resource types and operations with the resolved answer. Both paths exist because AUTHENTICATION.md mandates `/v1.0/tenants/...` while the rest of the API lives under `/v1.0/api`.

| Method | Path | Summary |
|---|---|---|
| GET | `/v1.0/api/users/me` | The signed-in user |
| PUT | `/v1.0/api/users/me` | Update the signed-in user's name, locale, and default agent profile |
| POST | `/v1.0/api/users/me/password` | Change the signed-in user's password (clears must-change-password) |
| GET | `/v1.0/api/tenants/{tenantId}/users` | List users |
| POST | `/v1.0/api/tenants/{tenantId}/users` | Create a user |
| GET | `/v1.0/api/tenants/{tenantId}/users/{id}` | Read a user (self or with User Read) |
| PUT | `/v1.0/api/tenants/{tenantId}/users/{id}` | Update a user |
| DELETE | `/v1.0/api/tenants/{tenantId}/users/{id}` | Delete a user with credentials, sessions, and assignments |
| GET | `/v1.0/api/tenants/{tenantId}/users/{id}/permissions` | Effective permissions of a user (self or with User Admin) |
| GET | `/v1.0/tenants/{tenantId}/users/{id}/permissions` | Effective permissions of a user (AUTHENTICATION.md path; same as /v1.0/api/tenants/{tenantId}/users/{id}/permissions) |
| POST | `/v1.0/api/tenants/{tenantId}/users/{id}/password` | Set a user's password (an administrator reset; the user must change it at next sign-in unless mustChangePassword=false is passed in the query) |

### GET `/v1.0/api/users/me`

The signed-in user. Allowed while a password change is pending.

**Authentication:** Any authenticated principal.

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden.

**Response fields:** `id`, `tenantId`, `firstName`, `lastName`, `email`, `isAdmin`, `isTenantAdmin`, `mustChangePassword`, `defaultAgentProfileId`, `preferredLocale`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/users/me" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "id": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "tenantId": "default",
  "firstName": "Thomas",
  "lastName": "Anderson",
  "email": "anderson@hugo.local",
  "isAdmin": false,
  "isTenantAdmin": true,
  "mustChangePassword": false,
  "preferredLocale": "en",
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:07:41.925246Z",
  "lastUpdateUtc": "2026-10-15T05:12:03.567206Z"
}
```

### PUT `/v1.0/api/users/me`

Change your own first and last name, preferred locale, and default agent profile.

**Authentication:** Any authenticated principal.

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `email` | string | **Required.** Unique within the tenant (on create). |
| `password` | string | **Required.** Initial password (on create). On update, an administrator reset. |
| `firstName` | string | Given name. |
| `lastName` | string | Family name. |
| `isTenantAdmin` | boolean | Tenant administrator bypass. Default false. |
| `isAdmin` | boolean | System administrator. Only system administrators may set it. |
| `active` | boolean | Inactive users cannot sign in. Default true. |
| `mustChangePassword` | boolean | Force a password change at next sign-in. Default true on create. |
| `preferredLocale` | string | Dashboard locale, for example `en` or `de`. |
| `defaultAgentProfileId` | string | Profile used when this user names none (D-018). |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden.

**Response fields:** `id`, `tenantId`, `firstName`, `lastName`, `email`, `isAdmin`, `isTenantAdmin`, `mustChangePassword`, `defaultAgentProfileId`, `preferredLocale`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s -X PUT "$HUGO/v1.0/api/users/me" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"firstName": "Thomas", "lastName": "Anderson", "preferredLocale": "en"}'
```

Response (200):

```json
{
  "id": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "tenantId": "default",
  "firstName": "Thomas",
  "lastName": "Anderson",
  "email": "anderson@hugo.local",
  "isAdmin": false,
  "isTenantAdmin": true,
  "mustChangePassword": false,
  "preferredLocale": "en",
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:07:41.925246Z",
  "lastUpdateUtc": "2026-10-15T05:17:46.196143Z"
}
```

### POST `/v1.0/api/users/me/password`

Change your own password and clear `mustChangePassword`. The new password must differ from the current one. Sessions stay valid, but a session issued while the flag was set keeps it until you refresh the token or sign in again. This run changes the password to a temporary value and the next request changes it back.

**Authentication:** Any authenticated principal.

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `currentPassword` | string | Current password (required on `users/me/password`; ignored on the administrator reset). |
| `newPassword` | string | **Required.** New password. Must differ from the current one. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden.

**Response fields:** `id`, `tenantId`, `firstName`, `lastName`, `email`, `isAdmin`, `isTenantAdmin`, `mustChangePassword`, `defaultAgentProfileId`, `preferredLocale`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/users/me/password" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{\"currentPassword\": \"$PASSWORD\", \"newPassword\": \"$NEW_PASSWORD\"}"
```

Response (200):

```json
{
  "id": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "tenantId": "default",
  "firstName": "Thomas",
  "lastName": "Anderson",
  "email": "anderson@hugo.local",
  "isAdmin": false,
  "isTenantAdmin": true,
  "mustChangePassword": false,
  "preferredLocale": "en",
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:07:41.925246Z",
  "lastUpdateUtc": "2026-10-15T05:17:46.790585Z"
}
```

### GET `/v1.0/api/tenants/{tenantId}/users`

Users in the tenant, newest first. Supports `maxResults` (1 to 1000, default 25), `skip`, `ordering`, `search`, and `activeOnly`.

**Authentication:** Any REST scheme. Permission: Read on User.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `maxResults` | query | integer | 1 to 1000. Default 25. |
| `skip` | query | integer | Records to skip. Default 0. |
| `ordering` | query | enum | CreatedDescending (default), CreatedAscending, NameAscending, NameDescending. |
| `search` | query | string | Text to match. |
| `activeOnly` | query | boolean | Only active records. Default false. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `success`, `maxResults`, `skip`, `totalRecords`, `recordsRemaining`, `endOfResults`, `timestampUtc`, `objects`, `objects[].id`, `objects[].tenantId`, `objects[].firstName`, `objects[].lastName`, `objects[].email`, `objects[].isAdmin`, `objects[].isTenantAdmin`, `objects[].mustChangePassword`, `objects[].defaultAgentProfileId`, `objects[].preferredLocale`, `objects[].active`, `objects[].isProtected`, `objects[].createdUtc`, `objects[].lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/tenants/default/users?maxResults=25&skip=0" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "success": true,
  "maxResults": 25,
  "skip": 0,
  "totalRecords": 2,
  "recordsRemaining": 0,
  "endOfResults": true,
  "timestampUtc": "2026-10-15T05:17:46.214048Z",
  "objects": [
    {
      "id": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
      "tenantId": "default",
      "firstName": "Thomas",
      "lastName": "Anderson",
      "email": "anderson@hugo.local",
      "isAdmin": false,
      "isTenantAdmin": true,
      "mustChangePassword": false,
      "preferredLocale": "en",
      "active": true,
      "isProtected": false,
      "createdUtc": "2026-10-15T05:07:41.925246Z",
      "lastUpdateUtc": "2026-10-15T05:17:46.196143Z"
    },
    {
      "id": "usr_mv92uyjg_bbPSzjzpKyKcf0z80f2",
      "tenantId": "default",
      "firstName": "System",
      "lastName": "Administrator",
      "email": "admin@hugo.local",
      "isAdmin": true,
      "isTenantAdmin": true,
      "mustChangePassword": false,
      "active": true,
      "isProtected": true,
      "createdUtc": "2026-10-15T05:07:41.856084Z",
      "lastUpdateUtc": "2026-10-15T05:07:43.633548Z"
    }
  ]
}
```

### POST `/v1.0/api/tenants/{tenantId}/users`

Create a user. `mustChangePassword` defaults to true, so the person picks their own password at first sign-in. Needs Create on User.

**Authentication:** Any REST scheme. Permission: Create on User.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `email` | string | **Required.** Unique within the tenant (on create). |
| `password` | string | **Required.** Initial password (on create). On update, an administrator reset. |
| `firstName` | string | Given name. |
| `lastName` | string | Family name. |
| `isTenantAdmin` | boolean | Tenant administrator bypass. Default false. |
| `isAdmin` | boolean | System administrator. Only system administrators may set it. |
| `active` | boolean | Inactive users cannot sign in. Default true. |
| `mustChangePassword` | boolean | Force a password change at next sign-in. Default true on create. |
| `preferredLocale` | string | Dashboard locale, for example `en` or `de`. |
| `defaultAgentProfileId` | string | Profile used when this user names none (D-018). |

**Status codes:** 201 Created, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found, 409 Conflict.

**Response fields:** `id`, `tenantId`, `firstName`, `lastName`, `email`, `isAdmin`, `isTenantAdmin`, `mustChangePassword`, `defaultAgentProfileId`, `preferredLocale`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/tenants/default/users" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"email": "niobe@hugo.local", "password": "Logos-Captain-1!", "firstName": "Niobe", "lastName": "Logos", "isTenantAdmin": false, "mustChangePassword": true}'
```

Response (201):

```json
{
  "id": "usr_mv937wxj_GEGzecwcebaxlLEgBuE",
  "tenantId": "default",
  "firstName": "Niobe",
  "lastName": "Logos",
  "email": "niobe@hugo.local",
  "isAdmin": false,
  "isTenantAdmin": false,
  "mustChangePassword": true,
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:46.320248Z",
  "lastUpdateUtc": "2026-10-15T05:17:46.320248Z"
}
```

### GET `/v1.0/api/tenants/{tenantId}/users/{id}`

One user. Callers may always read themselves; reading others needs Read on User.

**Authentication:** Any authenticated principal.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `firstName`, `lastName`, `email`, `isAdmin`, `isTenantAdmin`, `mustChangePassword`, `defaultAgentProfileId`, `preferredLocale`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/tenants/default/users/usr_mv937wxj_GEGzecwcebaxlLEgBuE" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "id": "usr_mv937wxj_GEGzecwcebaxlLEgBuE",
  "tenantId": "default",
  "firstName": "Niobe",
  "lastName": "Logos",
  "email": "niobe@hugo.local",
  "isAdmin": false,
  "isTenantAdmin": false,
  "mustChangePassword": true,
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:46.320248Z",
  "lastUpdateUtc": "2026-10-15T05:17:46.320248Z"
}
```

### PUT `/v1.0/api/tenants/{tenantId}/users/{id}`

Update a user. Omitted fields keep their values.

**Authentication:** Any REST scheme. Permission: Update on User.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `email` | string | **Required.** Unique within the tenant (on create). |
| `password` | string | **Required.** Initial password (on create). On update, an administrator reset. |
| `firstName` | string | Given name. |
| `lastName` | string | Family name. |
| `isTenantAdmin` | boolean | Tenant administrator bypass. Default false. |
| `isAdmin` | boolean | System administrator. Only system administrators may set it. |
| `active` | boolean | Inactive users cannot sign in. Default true. |
| `mustChangePassword` | boolean | Force a password change at next sign-in. Default true on create. |
| `preferredLocale` | string | Dashboard locale, for example `en` or `de`. |
| `defaultAgentProfileId` | string | Profile used when this user names none (D-018). |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `firstName`, `lastName`, `email`, `isAdmin`, `isTenantAdmin`, `mustChangePassword`, `defaultAgentProfileId`, `preferredLocale`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s -X PUT "$HUGO/v1.0/api/tenants/default/users/usr_mv937wxj_GEGzecwcebaxlLEgBuE" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"firstName": "Niobe", "lastName": "Logos", "active": true}'
```

Response (200):

```json
{
  "id": "usr_mv937wxj_GEGzecwcebaxlLEgBuE",
  "tenantId": "default",
  "firstName": "Niobe",
  "lastName": "Logos",
  "email": "niobe@hugo.local",
  "isAdmin": false,
  "isTenantAdmin": false,
  "mustChangePassword": true,
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:46.320248Z",
  "lastUpdateUtc": "2026-10-15T05:17:46.351789Z"
}
```

### DELETE `/v1.0/api/tenants/{tenantId}/users/{id}`

Delete a user with their credentials, sessions, and assignments.

**Authentication:** Any REST scheme. Permission: Delete on User.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Status codes:** 204 No Content, 401 Not authenticated, 403 Forbidden, 404 Not found.

Example:

```bash
curl -s -X DELETE "$HUGO/v1.0/api/tenants/default/users/usr_mv937wxj_GEGzecwcebaxlLEgBuE" \
  -H "Authorization: Bearer $TOKEN"
```

Response: 204 with an empty body.

### GET `/v1.0/api/tenants/{tenantId}/users/{id}/permissions`

The grants RBAC resolves for the user (roles, scopes, deny entries) and a `hasPermission` matrix for every resource type and operation. Callers may inspect themselves; others need Admin on User.

**Authentication:** Any authenticated principal.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `principalType`, `principalId`, `tenantId`, `isAdmin`, `isTenantAdmin`, `grants`, `hasPermission`.

Example:

```bash
curl -s "$HUGO/v1.0/api/tenants/default/users/usr_mv937wxj_GEGzecwcebaxlLEgBuE/permissions" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "principalType": "User",
  "principalId": "usr_mv937wxj_GEGzecwcebaxlLEgBuE",
  "tenantId": "default",
  "isAdmin": false,
  "isTenantAdmin": false,
  "grants": [
    {
      "source": "TenantMember",
      "permissionType": "Permit",
      "resourceTypes": [
        "Tenant",
        "AgentProfile"
      ],
      "operations": [
        "Read"
      ],
      "scope": "Tenant",
      "inheritsToChildren": true
    }
  ],
  "hasPermission": [
    {
      "resourceType": "Tenant",
      "operation": "Read",
      "permitted": true
    },
    {
      "resourceType": "Tenant",
      "operation": "Create",
      "permitted": false
    },
    "… 142 more"
  ]
}
```

### GET `/v1.0/tenants/{tenantId}/users/{id}/permissions`

The same document under the path AUTHENTICATION.md mandates.

**Authentication:** Any authenticated principal.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `principalType`, `principalId`, `tenantId`, `isAdmin`, `isTenantAdmin`, `grants`, `hasPermission`.

Example:

```bash
curl -s "$HUGO/v1.0/tenants/default/users/usr_mv937wxj_GEGzecwcebaxlLEgBuE/permissions" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "principalType": "User",
  "principalId": "usr_mv937wxj_GEGzecwcebaxlLEgBuE",
  "tenantId": "default",
  "isAdmin": false,
  "isTenantAdmin": false,
  "grants": [
    {
      "source": "TenantMember",
      "permissionType": "Permit",
      "resourceTypes": [
        "Tenant",
        "AgentProfile"
      ],
      "operations": [
        "Read"
      ],
      "scope": "Tenant",
      "inheritsToChildren": true
    }
  ],
  "hasPermission": [
    {
      "resourceType": "Tenant",
      "operation": "Read",
      "permitted": true
    },
    {
      "resourceType": "Tenant",
      "operation": "Create",
      "permitted": false
    },
    "… 142 more"
  ]
}
```

### POST `/v1.0/api/tenants/{tenantId}/users/{id}/password`

Administrator password reset. The user must change it at next sign-in unless `?mustChangePassword=false` is passed.

**Authentication:** Any REST scheme. Permission: Update on User.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |
| `mustChangePassword` | query | boolean | Default true. false lets the user keep the new password. |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `currentPassword` | string | Current password (required on `users/me/password`; ignored on the administrator reset). |
| `newPassword` | string | **Required.** New password. Must differ from the current one. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `firstName`, `lastName`, `email`, `isAdmin`, `isTenantAdmin`, `mustChangePassword`, `defaultAgentProfileId`, `preferredLocale`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/tenants/default/users/usr_mv937wxj_GEGzecwcebaxlLEgBuE/password" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"newPassword": "Temporary-Pass-2!"}'
```

Response (200):

```json
{
  "id": "usr_mv937wxj_GEGzecwcebaxlLEgBuE",
  "tenantId": "default",
  "firstName": "Niobe",
  "lastName": "Logos",
  "email": "niobe@hugo.local",
  "isAdmin": false,
  "isTenantAdmin": false,
  "mustChangePassword": true,
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:46.320248Z",
  "lastUpdateUtc": "2026-10-15T05:17:46.517832Z"
}
```

## Credentials

A credential is how software signs in: an access key (`access_...`) and a secret key, owned by a user and never more powerful than that user. Bind a credential to an agent profile with `agentProfileId`, and every MCP, A2A, or OpenAI-compatible request made with it acts as that profile unless the request names another; set `lockToProfile` to forbid naming another. That is the configuration to give a coding agent: one credential, one profile, the tools and instructions of that profile and nothing else.

The secret key is returned exactly twice in a credential's life, at creation and at rotation, and Hugo stores it encrypted (signed requests need the plaintext to verify an HMAC). Lose it and rotate.

| Method | Path | Summary |
|---|---|---|
| GET | `/v1.0/api/tenants/{tenantId}/credentials` | List credentials |
| POST | `/v1.0/api/tenants/{tenantId}/credentials` | Create a credential (the secret is returned once) |
| GET | `/v1.0/api/tenants/{tenantId}/credentials/{id}` | Read a credential |
| PUT | `/v1.0/api/tenants/{tenantId}/credentials/{id}` | Update a credential |
| DELETE | `/v1.0/api/tenants/{tenantId}/credentials/{id}` | Delete a credential |
| POST | `/v1.0/api/tenants/{tenantId}/credentials/{id}/rotate` | Rotate a credential secret (the new secret is returned once) |
| GET | `/v1.0/api/tenants/{tenantId}/credentials/{id}/permissions` | Effective permissions of a credential (self or with Credential Admin) |
| GET | `/v1.0/tenants/{tenantId}/credentials/{id}/permissions` | Effective permissions of a credential (self or with Credential Admin) (AUTHENTICATION.md path) |

### GET `/v1.0/api/tenants/{tenantId}/credentials`

Credentials in the tenant. Only the last four characters of each secret are shown.

**Authentication:** Any REST scheme. Permission: Read on Credential.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `maxResults` | query | integer | 1 to 1000. Default 25. |
| `skip` | query | integer | Records to skip. Default 0. |
| `ordering` | query | enum | CreatedDescending (default), CreatedAscending, NameAscending, NameDescending. |
| `search` | query | string | Text to match. |
| `activeOnly` | query | boolean | Only active records. Default false. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `success`, `maxResults`, `skip`, `totalRecords`, `recordsRemaining`, `endOfResults`, `timestampUtc`, `objects`, `objects[].id`, `objects[].tenantId`, `objects[].userId`, `objects[].name`, `objects[].accessKey`, `objects[].secretKeyLast4`, `objects[].authMode`, `objects[].agentProfileId`, `objects[].lockToProfile`, `objects[].lastUsedUtc`, `objects[].expiresUtc`, `objects[].active`, `objects[].isProtected`, `objects[].createdUtc`, `objects[].lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/tenants/default/credentials" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "success": true,
  "maxResults": 25,
  "skip": 0,
  "totalRecords": 1,
  "recordsRemaining": 0,
  "endOfResults": true,
  "timestampUtc": "2026-10-15T05:17:49.676549Z",
  "objects": [
    {
      "id": "crd_mv937zkw_coIvDxQfarwIRkctwpQ",
      "tenantId": "default",
      "userId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
      "name": "claude-code",
      "accessKey": "access_TeRfnOxKRXOhWQTiuyyScxHCsx92tb5Z",
      "secretKeyLast4": "N6gY",
      "authMode": "Hybrid",
      "agentProfileId": "agp_mv92uyqb_LM9EdE7i2KwfKe6GfaO",
      "lockToProfile": false,
      "active": true,
      "isProtected": false,
      "createdUtc": "2026-10-15T05:17:49.664732Z",
      "lastUpdateUtc": "2026-10-15T05:17:49.664732Z"
    }
  ]
}
```

### POST `/v1.0/api/tenants/{tenantId}/credentials`

Create a credential for a user (the caller when `userId` is omitted). The secret key appears in this response and nowhere else. `roleName` narrows the credential with a scope assignment, and `agentProfileId` with `lockToProfile` pins it to one profile. The example creates the credential a coding agent would use, bound to `Hugo` and narrowed to Operator.

**Authentication:** Any REST scheme. Permission: Create on Credential.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `name` | string | **Required.** Display name (on create). |
| `userId` | string | Owner. Defaults to the caller; creating one for someone else needs Admin on Credential. |
| `authMode` | enum | DirectHeader, SignedRequest, SessionExchange, or Hybrid (default). Values: DirectHeader, SignedRequest, SessionExchange, Hybrid. |
| `expiresUtc` | date-time | Optional expiry. |
| `active` | boolean | Default true. |
| `roleName` | string | Built-in or custom role that narrows the credential (adds a credential scope assignment). |
| `agentProfileId` | string | Profile the credential acts as by default (D-018). |
| `lockToProfile` | boolean | When true the credential can only act as `agentProfileId`. |

**Status codes:** 201 Created, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found, 409 Conflict.

**Response fields:** `credential`, `accessKey`, `secretKey`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/tenants/default/credentials" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name": "claude-code", "authMode": "Hybrid", "agentProfileId": "agp_mv92uyqb_LM9EdE7i2KwfKe6GfaO", "lockToProfile": false, "roleName": "Operator"}'
```

Response (201):

```json
{
  "credential": {
    "id": "crd_mv937zkw_coIvDxQfarwIRkctwpQ",
    "tenantId": "default",
    "userId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
    "name": "claude-code",
    "accessKey": "access_TeRfnOxKRXOhWQTiuyyScxHCsx92tb5Z",
    "secretKeyLast4": "N6gY",
    "authMode": "Hybrid",
    "agentProfileId": "agp_mv92uyqb_LM9EdE7i2KwfKe6GfaO",
    "lockToProfile": false,
    "active": true,
    "isProtected": false,
    "createdUtc": "2026-10-15T05:17:49.664732Z",
    "lastUpdateUtc": "2026-10-15T05:17:49.664732Z"
  },
  "accessKey": "access_TeRfnOxKRXOhWQTiuyyScxHCsx92tb5Z",
  "secretKey": "<secret key, shown once>"
}
```

### GET `/v1.0/api/tenants/{tenantId}/credentials/{id}`

One credential.

**Authentication:** Any REST scheme. Permission: Read on Credential.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `userId`, `name`, `accessKey`, `secretKeyLast4`, `authMode`, `agentProfileId`, `lockToProfile`, `lastUsedUtc`, `expiresUtc`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/tenants/default/credentials/crd_mv937zkw_coIvDxQfarwIRkctwpQ" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "id": "crd_mv937zkw_coIvDxQfarwIRkctwpQ",
  "tenantId": "default",
  "userId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "name": "claude-code",
  "accessKey": "access_TeRfnOxKRXOhWQTiuyyScxHCsx92tb5Z",
  "secretKeyLast4": "N6gY",
  "authMode": "Hybrid",
  "agentProfileId": "agp_mv92uyqb_LM9EdE7i2KwfKe6GfaO",
  "lockToProfile": false,
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.664732Z",
  "lastUpdateUtc": "2026-10-15T05:17:49.664732Z"
}
```

### PUT `/v1.0/api/tenants/{tenantId}/credentials/{id}`

Change name, profile binding, lock, expiry, or active flag.

**Authentication:** Any REST scheme. Permission: Update on Credential.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `name` | string | **Required.** Display name (on create). |
| `userId` | string | Owner. Defaults to the caller; creating one for someone else needs Admin on Credential. |
| `authMode` | enum | DirectHeader, SignedRequest, SessionExchange, or Hybrid (default). Values: DirectHeader, SignedRequest, SessionExchange, Hybrid. |
| `expiresUtc` | date-time | Optional expiry. |
| `active` | boolean | Default true. |
| `roleName` | string | Built-in or custom role that narrows the credential (adds a credential scope assignment). |
| `agentProfileId` | string | Profile the credential acts as by default (D-018). |
| `lockToProfile` | boolean | When true the credential can only act as `agentProfileId`. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `userId`, `name`, `accessKey`, `secretKeyLast4`, `authMode`, `agentProfileId`, `lockToProfile`, `lastUsedUtc`, `expiresUtc`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s -X PUT "$HUGO/v1.0/api/tenants/default/credentials/crd_mv937zkw_coIvDxQfarwIRkctwpQ" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name": "claude-code-laptop", "lockToProfile": true}'
```

Response (200):

```json
{
  "id": "crd_mv937zkw_coIvDxQfarwIRkctwpQ",
  "tenantId": "default",
  "userId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "name": "claude-code-laptop",
  "accessKey": "access_TeRfnOxKRXOhWQTiuyyScxHCsx92tb5Z",
  "secretKeyLast4": "N6gY",
  "authMode": "Hybrid",
  "agentProfileId": "agp_mv92uyqb_LM9EdE7i2KwfKe6GfaO",
  "lockToProfile": true,
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.664732Z",
  "lastUpdateUtc": "2026-10-15T05:17:49.701867Z"
}
```

### DELETE `/v1.0/api/tenants/{tenantId}/credentials/{id}`

Delete a credential and its sessions.

**Authentication:** Any REST scheme. Permission: Delete on Credential.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Status codes:** 204 No Content, 401 Not authenticated, 403 Forbidden, 404 Not found.

Example:

```bash
curl -s -X DELETE "$HUGO/v1.0/api/tenants/default/credentials/crd_mv937zkw_coIvDxQfarwIRkctwpQ" \
  -H "Authorization: Bearer $TOKEN"
```

Response: 204 with an empty body.

### POST `/v1.0/api/tenants/{tenantId}/credentials/{id}/rotate`

Issue a new secret key (returned once). The access key stays; the old secret stops working immediately.

**Authentication:** Any REST scheme. Permission: Update on Credential.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `credential`, `accessKey`, `secretKey`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/tenants/default/credentials/crd_mv937zkw_coIvDxQfarwIRkctwpQ/rotate" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "credential": {
    "id": "crd_mv937zkw_coIvDxQfarwIRkctwpQ",
    "tenantId": "default",
    "userId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
    "name": "claude-code-laptop",
    "accessKey": "access_TeRfnOxKRXOhWQTiuyyScxHCsx92tb5Z",
    "secretKeyLast4": "2Mb9",
    "authMode": "Hybrid",
    "agentProfileId": "agp_mv92uyqb_LM9EdE7i2KwfKe6GfaO",
    "lockToProfile": true,
    "lastUsedUtc": "2026-10-15T05:17:49.714417Z",
    "active": true,
    "isProtected": false,
    "createdUtc": "2026-10-15T05:17:49.664732Z",
    "lastUpdateUtc": "2026-10-15T05:17:50.533715Z"
  },
  "accessKey": "access_TeRfnOxKRXOhWQTiuyyScxHCsx92tb5Z",
  "secretKey": "<secret key, shown once>"
}
```

### GET `/v1.0/api/tenants/{tenantId}/credentials/{id}/permissions`

What RBAC resolves for the credential after the owner ceiling and its scope assignments.

**Authentication:** Any authenticated principal.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `principalType`, `principalId`, `tenantId`, `isAdmin`, `isTenantAdmin`, `grants`, `hasPermission`.

Example:

```bash
curl -s "$HUGO/v1.0/api/tenants/default/credentials/crd_mv937zkw_coIvDxQfarwIRkctwpQ/permissions" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "principalType": "Credential",
  "principalId": "crd_mv937zkw_coIvDxQfarwIRkctwpQ",
  "tenantId": "default",
  "isAdmin": false,
  "isTenantAdmin": true,
  "grants": [
    {
      "source": "Operator",
      "permissionType": "Permit",
      "resourceTypes": [
        "Resource",
        "Connector",
        "… 5 more"
      ],
      "operations": [
        "Read"
      ],
      "scope": "Tenant",
      "inheritsToChildren": true
    },
    {
      "source": "Operator",
      "permissionType": "Permit",
      "resourceTypes": [
        "AgentProfile"
      ],
      "operations": [
        "Execute"
      ],
      "scope": "Tenant",
      "inheritsToChildren": true
    },
    "… 2 more"
  ],
  "hasPermission": [
    {
      "resourceType": "Tenant",
      "operation": "Read",
      "permitted": false
    },
    {
      "resourceType": "Tenant",
      "operation": "Create",
      "permitted": false
    },
    "… 142 more"
  ]
}
```

### GET `/v1.0/tenants/{tenantId}/credentials/{id}/permissions`

The same document under the mandated path.

**Authentication:** Any authenticated principal.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `principalType`, `principalId`, `tenantId`, `isAdmin`, `isTenantAdmin`, `grants`, `hasPermission`.

Example:

```bash
curl -s "$HUGO/v1.0/tenants/default/credentials/crd_mv937zkw_coIvDxQfarwIRkctwpQ/permissions" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "principalType": "Credential",
  "principalId": "crd_mv937zkw_coIvDxQfarwIRkctwpQ",
  "tenantId": "default",
  "isAdmin": false,
  "isTenantAdmin": true,
  "grants": [
    {
      "source": "Operator",
      "permissionType": "Permit",
      "resourceTypes": [
        "Resource",
        "Connector",
        "… 5 more"
      ],
      "operations": [
        "Read"
      ],
      "scope": "Tenant",
      "inheritsToChildren": true
    },
    {
      "source": "Operator",
      "permissionType": "Permit",
      "resourceTypes": [
        "AgentProfile"
      ],
      "operations": [
        "Execute"
      ],
      "scope": "Tenant",
      "inheritsToChildren": true
    },
    "… 2 more"
  ],
  "hasPermission": [
    {
      "resourceType": "Tenant",
      "operation": "Read",
      "permitted": false
    },
    {
      "resourceType": "Tenant",
      "operation": "Create",
      "permitted": false
    },
    "… 142 more"
  ]
}
```

## Roles and permissions

Permissions are Permit or Deny statements over resource types and operations. Roles bundle permissions. Hugo ships protected built-in roles (TenantAdmin, SecurityAdmin, Auditor, Operator, Builder, ResourceAdmin, Editor, Viewer, ProfileUser, TenantMember) with stable IDs such as `rol_builtin_viewer`; you cannot edit or delete them, but you can clone one and edit the copy. Custom permissions are mapped to custom roles through `role-permissions`.

Deny wins over Permit wherever both apply. The resource types and operations, and what each built-in role grants, are listed in [docs/SECURITY.md](docs/SECURITY.md).

| Method | Path | Summary |
|---|---|---|
| GET | `/v1.0/api/tenants/{tenantId}/roles` | List roles, including built-in roles |
| POST | `/v1.0/api/tenants/{tenantId}/roles` | Create a custom role |
| GET | `/v1.0/api/tenants/{tenantId}/roles/{id}` | Read a role |
| PUT | `/v1.0/api/tenants/{tenantId}/roles/{id}` | Update a custom role |
| DELETE | `/v1.0/api/tenants/{tenantId}/roles/{id}` | Delete a custom role |
| POST | `/v1.0/api/tenants/{tenantId}/roles/{id}/clone` | Clone a role (including built-ins) into a custom role |
| GET | `/v1.0/api/tenants/{tenantId}/role-permissions` | List role-permission mappings (filter with roleId) |
| POST | `/v1.0/api/tenants/{tenantId}/role-permissions` | Map a permission to a custom role |
| DELETE | `/v1.0/api/tenants/{tenantId}/role-permissions/{id}` | Remove a role-permission mapping |
| GET | `/v1.0/api/tenants/{tenantId}/permissions` | List permissions, including built-ins |
| POST | `/v1.0/api/tenants/{tenantId}/permissions` | Create a permission |
| GET | `/v1.0/api/tenants/{tenantId}/permissions/{id}` | Read a permission |
| PUT | `/v1.0/api/tenants/{tenantId}/permissions/{id}` | Update a permission |
| DELETE | `/v1.0/api/tenants/{tenantId}/permissions/{id}` | Delete a permission |

### GET `/v1.0/api/tenants/{tenantId}/roles`

Roles, including the built-ins.

**Authentication:** Any REST scheme. Permission: Read on Role.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `maxResults` | query | integer | 1 to 1000. Default 25. |
| `skip` | query | integer | Records to skip. Default 0. |
| `ordering` | query | enum | CreatedDescending (default), CreatedAscending, NameAscending, NameDescending. |
| `search` | query | string | Text to match. |
| `activeOnly` | query | boolean | Only active records. Default false. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `success`, `maxResults`, `skip`, `totalRecords`, `recordsRemaining`, `endOfResults`, `timestampUtc`, `objects`, `objects[].id`, `objects[].tenantId`, `objects[].name`, `objects[].description`, `objects[].isBuiltIn`, `objects[].active`, `objects[].isProtected`, `objects[].createdUtc`, `objects[].lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/tenants/default/roles?maxResults=50" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "success": true,
  "maxResults": 50,
  "skip": 0,
  "totalRecords": 11,
  "recordsRemaining": 0,
  "endOfResults": true,
  "timestampUtc": "2026-10-15T05:17:49.778594Z",
  "objects": [
    {
      "id": "rol_builtin_custom",
      "name": "Custom",
      "description": "Template marker for tenant-defined roles. Carries no permissions.",
      "isBuiltIn": true,
      "active": true,
      "isProtected": true,
      "createdUtc": "2026-10-15T05:07:41.778381Z",
      "lastUpdateUtc": "2026-10-15T05:07:41.778381Z"
    },
    {
      "id": "rol_builtin_tenantmember",
      "name": "TenantMember",
      "description": "Minimal tenant presence: read the tenant and agent profiles.",
      "isBuiltIn": true,
      "active": true,
      "isProtected": true,
      "createdUtc": "2026-10-15T05:07:41.778199Z",
      "lastUpdateUtc": "2026-10-15T05:07:41.778199Z"
    },
    "… 9 more"
  ]
}
```

### POST `/v1.0/api/tenants/{tenantId}/roles`

Create a custom role.

**Authentication:** Any REST scheme. Permission: Create on Role.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `name` | string | **Required.** Role name (on create and clone). |
| `description` | string | Free text. |
| `active` | boolean | Default true. |

**Status codes:** 201 Created, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found, 409 Conflict.

**Response fields:** `id`, `tenantId`, `name`, `description`, `isBuiltIn`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/tenants/default/roles" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name": "Night shift", "description": "Execute on the on-call profile, read everything else"}'
```

Response (201):

```json
{
  "id": "rol_mv937zof_AD3nbQ1lnvEQJsb6ovw",
  "tenantId": "default",
  "name": "Night shift",
  "description": "Execute on the on-call profile, read everything else",
  "isBuiltIn": false,
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.791781Z",
  "lastUpdateUtc": "2026-10-15T05:17:49.791781Z"
}
```

### GET `/v1.0/api/tenants/{tenantId}/roles/{id}`

One role.

**Authentication:** Any REST scheme. Permission: Read on Role.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `name`, `description`, `isBuiltIn`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/tenants/default/roles/rol_mv937zof_AD3nbQ1lnvEQJsb6ovw" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "id": "rol_mv937zof_AD3nbQ1lnvEQJsb6ovw",
  "tenantId": "default",
  "name": "Night shift",
  "description": "Execute on the on-call profile, read everything else",
  "isBuiltIn": false,
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.791781Z",
  "lastUpdateUtc": "2026-10-15T05:17:49.791781Z"
}
```

### PUT `/v1.0/api/tenants/{tenantId}/roles/{id}`

Update a custom role. Built-in roles answer 403.

**Authentication:** Any REST scheme. Permission: Update on Role.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `name` | string | **Required.** Role name (on create and clone). |
| `description` | string | Free text. |
| `active` | boolean | Default true. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `name`, `description`, `isBuiltIn`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s -X PUT "$HUGO/v1.0/api/tenants/default/roles/rol_mv937zof_AD3nbQ1lnvEQJsb6ovw" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"description": "Execute on the on-call profile; read-only elsewhere"}'
```

Response (200):

```json
{
  "id": "rol_mv937zof_AD3nbQ1lnvEQJsb6ovw",
  "tenantId": "default",
  "name": "Night shift",
  "description": "Execute on the on-call profile; read-only elsewhere",
  "isBuiltIn": false,
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.791781Z",
  "lastUpdateUtc": "2026-10-15T05:17:49.820556Z"
}
```

### DELETE `/v1.0/api/tenants/{tenantId}/roles/{id}`

Delete a custom role. Built-in roles cannot be deleted.

**Authentication:** Any REST scheme. Permission: Delete on Role.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Status codes:** 204 No Content, 401 Not authenticated, 403 Forbidden, 404 Not found.

Example:

```bash
curl -s -X DELETE "$HUGO/v1.0/api/tenants/default/roles/rol_mv937zof_AD3nbQ1lnvEQJsb6ovw" \
  -H "Authorization: Bearer $TOKEN"
```

Response: 204 with an empty body.

### POST `/v1.0/api/tenants/{tenantId}/roles/{id}/clone`

Copy a role (built-ins included) with its permissions into a new custom role.

**Authentication:** Any REST scheme. Permission: Create on Role.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `name` | string | **Required.** Role name (on create and clone). |
| `description` | string | Free text. |
| `active` | boolean | Default true. |

**Status codes:** 201 Created, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found, 409 Conflict.

**Response fields:** `id`, `tenantId`, `name`, `description`, `isBuiltIn`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/tenants/default/roles/rol_builtin_operator/clone" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name": "Operator (custom)"}'
```

Response (201):

```json
{
  "id": "rol_mv937zpn_PKCbvCjdlq9pO6cWCvV",
  "tenantId": "default",
  "name": "Operator (custom)",
  "description": "Runs agent profiles, works with conversations, decides approvals, and reads resources and activity.",
  "isBuiltIn": false,
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.835284Z",
  "lastUpdateUtc": "2026-10-15T05:17:49.835284Z"
}
```

### GET `/v1.0/api/tenants/{tenantId}/role-permissions`

Mappings, filtered by `roleId`.

**Authentication:** Any REST scheme. Permission: Read on Role.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `roleId` | query | string | Only this role. |
| `maxResults` | query | integer | 1 to 1000. Default 25. |
| `skip` | query | integer | Records to skip. Default 0. |
| `ordering` | query | enum | CreatedDescending (default), CreatedAscending, NameAscending, NameDescending. |
| `search` | query | string | Text to match. |
| `activeOnly` | query | boolean | Only active records. Default false. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `success`, `maxResults`, `skip`, `totalRecords`, `recordsRemaining`, `endOfResults`, `timestampUtc`, `objects`, `objects[].id`, `objects[].tenantId`, `objects[].roleId`, `objects[].permissionId`, `objects[].active`, `objects[].isProtected`, `objects[].createdUtc`, `objects[].lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/tenants/default/role-permissions?roleId=rol_mv937zof_AD3nbQ1lnvEQJsb6ovw" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "success": true,
  "maxResults": 25,
  "skip": 0,
  "totalRecords": 1,
  "recordsRemaining": 0,
  "endOfResults": true,
  "timestampUtc": "2026-10-15T05:17:49.913426Z",
  "objects": [
    {
      "id": "rpm_mv937zrh_ae4glflr1PquWMoQtgR",
      "tenantId": "default",
      "roleId": "rol_mv937zof_AD3nbQ1lnvEQJsb6ovw",
      "permissionId": "prm_mv937zqf_wkihonnhDQFOrnWKlv6",
      "active": true,
      "isProtected": false,
      "createdUtc": "2026-10-15T05:17:49.901124Z",
      "lastUpdateUtc": "2026-10-15T05:17:49.901124Z"
    }
  ]
}
```

### POST `/v1.0/api/tenants/{tenantId}/role-permissions`

Add a permission to a custom role.

**Authentication:** Any REST scheme. Permission: Update on Role.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `roleId` | string | **Required.** A custom role. |
| `permissionId` | string | **Required.** The permission to add. |

**Status codes:** 201 Created, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found, 409 Conflict.

**Response fields:** `id`, `tenantId`, `roleId`, `permissionId`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/tenants/default/role-permissions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"roleId": "rol_mv937zof_AD3nbQ1lnvEQJsb6ovw", "permissionId": "prm_mv937zqf_wkihonnhDQFOrnWKlv6"}'
```

Response (201):

```json
{
  "id": "rpm_mv937zrh_ae4glflr1PquWMoQtgR",
  "tenantId": "default",
  "roleId": "rol_mv937zof_AD3nbQ1lnvEQJsb6ovw",
  "permissionId": "prm_mv937zqf_wkihonnhDQFOrnWKlv6",
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.901124Z",
  "lastUpdateUtc": "2026-10-15T05:17:49.901124Z"
}
```

### DELETE `/v1.0/api/tenants/{tenantId}/role-permissions/{id}`

Remove a permission from a role.

**Authentication:** Any REST scheme. Permission: Update on Role.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Status codes:** 204 No Content, 401 Not authenticated, 403 Forbidden, 404 Not found.

Example:

```bash
curl -s -X DELETE "$HUGO/v1.0/api/tenants/default/role-permissions/rpm_mv937zrh_ae4glflr1PquWMoQtgR" \
  -H "Authorization: Bearer $TOKEN"
```

Response: 204 with an empty body.

### GET `/v1.0/api/tenants/{tenantId}/permissions`

Permissions, including the built-ins.

**Authentication:** Any REST scheme. Permission: Read on Permission.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `maxResults` | query | integer | 1 to 1000. Default 25. |
| `skip` | query | integer | Records to skip. Default 0. |
| `ordering` | query | enum | CreatedDescending (default), CreatedAscending, NameAscending, NameDescending. |
| `search` | query | string | Text to match. |
| `activeOnly` | query | boolean | Only active records. Default false. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `success`, `maxResults`, `skip`, `totalRecords`, `recordsRemaining`, `endOfResults`, `timestampUtc`, `objects`, `objects[].id`, `objects[].tenantId`, `objects[].name`, `objects[].permissionType`, `objects[].isBuiltIn`, `objects[].active`, `objects[].isProtected`, `objects[].createdUtc`, `objects[].lastUpdateUtc`, `objects[].resourceTypes`, `objects[].operationTypes`.

Example:

```bash
curl -s "$HUGO/v1.0/api/tenants/default/permissions" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "success": true,
  "maxResults": 25,
  "skip": 0,
  "totalRecords": 40,
  "recordsRemaining": 15,
  "endOfResults": false,
  "timestampUtc": "2026-10-15T05:17:49.850336Z",
  "objects": [
    {
      "id": "prm_mv937zpn_b37j8vai1y8YIiy205U",
      "tenantId": "default",
      "name": "Operator (custom): Operator.Approval",
      "permissionType": "Permit",
      "isBuiltIn": false,
      "active": true,
      "isProtected": false,
      "createdUtc": "2026-10-15T05:17:49.835947Z",
      "lastUpdateUtc": "2026-10-15T05:17:49.835947Z",
      "resourceTypes": [
        "Approval"
      ],
      "operationTypes": [
        "Update"
      ]
    },
    {
      "id": "prm_mv937zpn_kymZ870iJ30wxSbhUJe",
      "tenantId": "default",
      "name": "Operator (custom): Operator.Conversation",
      "permissionType": "Permit",
      "isBuiltIn": false,
      "active": true,
      "isProtected": false,
      "createdUtc": "2026-10-15T05:17:49.835798Z",
      "lastUpdateUtc": "2026-10-15T05:17:49.835798Z",
      "resourceTypes": [
        "Conversation"
      ],
      "operationTypes": [
        "Create",
        "Read",
        "Update"
      ]
    },
    "… 23 more"
  ]
}
```

### POST `/v1.0/api/tenants/{tenantId}/permissions`

Create a permission tuple.

**Authentication:** Any REST scheme. Permission: Create on Permission.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `name` | string | **Required.** Permission name. |
| `permissionType` | enum | Permit (default) or Deny. Deny always wins. Values: Permit, Deny. |
| `resourceTypes` | string[] | **Required.** Resource types it covers (see docs/SECURITY.md). |
| `operationTypes` | string[] | **Required.** Operations it covers: All, Create, Read, Write, Update, Delete, Execute, Admin, Reveal. |
| `active` | boolean | Default true. |

**Status codes:** 201 Created, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found, 409 Conflict.

**Response fields:** `id`, `tenantId`, `name`, `permissionType`, `isBuiltIn`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`, `resourceTypes`, `operationTypes`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/tenants/default/permissions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name": "Execute profiles", "permissionType": "Permit", "resourceTypes": ["AgentProfile"], "operationTypes": ["Execute", "Read"]}'
```

Response (201):

```json
{
  "id": "prm_mv937zqf_wkihonnhDQFOrnWKlv6",
  "tenantId": "default",
  "name": "Execute profiles",
  "permissionType": "Permit",
  "isBuiltIn": false,
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.863477Z",
  "lastUpdateUtc": "2026-10-15T05:17:49.863477Z",
  "resourceTypes": [
    "AgentProfile"
  ],
  "operationTypes": [
    "Execute",
    "Read"
  ]
}
```

### GET `/v1.0/api/tenants/{tenantId}/permissions/{id}`

One permission.

**Authentication:** Any REST scheme. Permission: Read on Permission.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `name`, `permissionType`, `isBuiltIn`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`, `resourceTypes`, `operationTypes`.

Example:

```bash
curl -s "$HUGO/v1.0/api/tenants/default/permissions/prm_mv937zqf_wkihonnhDQFOrnWKlv6" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "id": "prm_mv937zqf_wkihonnhDQFOrnWKlv6",
  "tenantId": "default",
  "name": "Execute profiles",
  "permissionType": "Permit",
  "isBuiltIn": false,
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.863477Z",
  "lastUpdateUtc": "2026-10-15T05:17:49.863477Z",
  "resourceTypes": [
    "AgentProfile"
  ],
  "operationTypes": [
    "Execute",
    "Read"
  ]
}
```

### PUT `/v1.0/api/tenants/{tenantId}/permissions/{id}`

Update a permission.

**Authentication:** Any REST scheme. Permission: Update on Permission.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `name` | string | **Required.** Permission name. |
| `permissionType` | enum | Permit (default) or Deny. Deny always wins. Values: Permit, Deny. |
| `resourceTypes` | string[] | **Required.** Resource types it covers (see docs/SECURITY.md). |
| `operationTypes` | string[] | **Required.** Operations it covers: All, Create, Read, Write, Update, Delete, Execute, Admin, Reveal. |
| `active` | boolean | Default true. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `name`, `permissionType`, `isBuiltIn`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`, `resourceTypes`, `operationTypes`.

Example:

```bash
curl -s -X PUT "$HUGO/v1.0/api/tenants/default/permissions/prm_mv937zqf_wkihonnhDQFOrnWKlv6" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name": "Execute and read profiles", "permissionType": "Permit", "resourceTypes": ["AgentProfile"], "operationTypes": ["Execute", "Read"]}'
```

Response (200):

```json
{
  "id": "prm_mv937zqf_wkihonnhDQFOrnWKlv6",
  "tenantId": "default",
  "name": "Execute and read profiles",
  "permissionType": "Permit",
  "isBuiltIn": false,
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.863477Z",
  "lastUpdateUtc": "2026-10-15T05:17:49.888673Z",
  "resourceTypes": [
    "AgentProfile"
  ],
  "operationTypes": [
    "Execute",
    "Read"
  ]
}
```

### DELETE `/v1.0/api/tenants/{tenantId}/permissions/{id}`

Delete a custom permission.

**Authentication:** Any REST scheme. Permission: Delete on Permission.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Status codes:** 204 No Content, 401 Not authenticated, 403 Forbidden, 404 Not found.

Example:

```bash
curl -s -X DELETE "$HUGO/v1.0/api/tenants/default/permissions/prm_mv937zqf_wkihonnhDQFOrnWKlv6" \
  -H "Authorization: Bearer $TOKEN"
```

Response: 204 with an empty body.

## Assignments

Assignments connect roles to principals. A user role assignment gives a user a role, either across the tenant (`resourceScope: Tenant`) or on one object (`resourceScope: Resource` with `resourceId`), which is how you let someone chat with one agent profile and nothing else. A credential scope assignment narrows a credential: the credential's effective permissions are what its scope allows and its owner holds. Creating a credential with `roleName` adds one of these for you.

| Method | Path | Summary |
|---|---|---|
| GET | `/v1.0/api/tenants/{tenantId}/user-role-assignments` | List user role assignments (filter with userId) |
| POST | `/v1.0/api/tenants/{tenantId}/user-role-assignments` | Assign a role to a user |
| GET | `/v1.0/api/tenants/{tenantId}/user-role-assignments/{id}` | Read a user role assignment |
| PUT | `/v1.0/api/tenants/{tenantId}/user-role-assignments/{id}` | Update a user role assignment |
| DELETE | `/v1.0/api/tenants/{tenantId}/user-role-assignments/{id}` | Remove a user role assignment |
| GET | `/v1.0/api/tenants/{tenantId}/credential-scope-assignments` | List credential scope assignments (filter with credentialId) |
| POST | `/v1.0/api/tenants/{tenantId}/credential-scope-assignments` | Assign a role to a credential |
| GET | `/v1.0/api/tenants/{tenantId}/credential-scope-assignments/{id}` | Read a credential scope assignment |
| PUT | `/v1.0/api/tenants/{tenantId}/credential-scope-assignments/{id}` | Update a credential scope assignment |
| DELETE | `/v1.0/api/tenants/{tenantId}/credential-scope-assignments/{id}` | Remove a credential scope assignment |

### GET `/v1.0/api/tenants/{tenantId}/user-role-assignments`

Assignments, filtered by `userId`.

**Authentication:** Any REST scheme. Permission: Read on Assignment.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `userId` | query | string | Only this user. |
| `maxResults` | query | integer | 1 to 1000. Default 25. |
| `skip` | query | integer | Records to skip. Default 0. |
| `ordering` | query | enum | CreatedDescending (default), CreatedAscending, NameAscending, NameDescending. |
| `search` | query | string | Text to match. |
| `activeOnly` | query | boolean | Only active records. Default false. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `success`, `maxResults`, `skip`, `totalRecords`, `recordsRemaining`, `endOfResults`, `timestampUtc`, `objects`, `objects[].id`, `objects[].tenantId`, `objects[].userId`, `objects[].roleId`, `objects[].roleName`, `objects[].resourceScope`, `objects[].resourceId`, `objects[].inheritsToChildren`, `objects[].active`, `objects[].isProtected`, `objects[].createdUtc`, `objects[].lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/tenants/default/user-role-assignments?userId=usr_mv937wxj_GEGzecwcebaxlLEgBuE" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "success": true,
  "maxResults": 25,
  "skip": 0,
  "totalRecords": 2,
  "recordsRemaining": 0,
  "endOfResults": true,
  "timestampUtc": "2026-10-15T05:17:49.937847Z",
  "objects": [
    {
      "id": "ura_mv937x00_PZ6XPcnlIrLYUKOyBUc",
      "tenantId": "default",
      "userId": "usr_mv937wxj_GEGzecwcebaxlLEgBuE",
      "roleId": "rol_builtin_tenantmember",
      "roleName": "TenantMember",
      "resourceScope": "Tenant",
      "inheritsToChildren": true,
      "active": true,
      "isProtected": false,
      "createdUtc": "2026-10-15T05:17:46.32044Z",
      "lastUpdateUtc": "2026-10-15T05:17:46.32044Z"
    },
    {
      "id": "ura_mv937zs5_hOaUygjovDH7CO1piZk",
      "tenantId": "default",
      "userId": "usr_mv937wxj_GEGzecwcebaxlLEgBuE",
      "roleId": "rol_mv937zof_AD3nbQ1lnvEQJsb6ovw",
      "roleName": "Night shift",
      "resourceScope": "Resource",
      "resourceId": "agp_mv937xml_XArgAHQfG5v6uUkrkbl",
      "inheritsToChildren": false,
      "active": true,
      "isProtected": false,
      "createdUtc": "2026-10-15T05:17:49.925817Z",
      "lastUpdateUtc": "2026-10-15T05:17:49.925817Z"
    }
  ]
}
```

### POST `/v1.0/api/tenants/{tenantId}/user-role-assignments`

Give a user a role, across the tenant (`resourceScope: Tenant`) or on one object (`resourceScope: Resource` with `resourceId`). The example grants a custom role on the `Oncall` profile only, which is how you let someone use one agent and nothing else.

**Authentication:** Any REST scheme. Permission: Create on Assignment.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `userId` | string | User (user role assignments). |
| `credentialId` | string | Credential (credential scope assignments). |
| `roleId` | string | Role by ID. |
| `roleName` | string | Role by name (alternative to `roleId`). |
| `resourceScope` | enum | Tenant (default) or Resource. Values: Tenant, Resource. |
| `resourceId` | string | Target when `resourceScope` is Resource (a resource, agent profile, secret, or conversation ID). |
| `inheritsToChildren` | boolean | Whether the grant covers child objects. |
| `active` | boolean | Default true. |

**Status codes:** 201 Created, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found, 409 Conflict.

**Response fields:** `id`, `tenantId`, `userId`, `roleId`, `roleName`, `resourceScope`, `resourceId`, `inheritsToChildren`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/tenants/default/user-role-assignments" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"userId": "usr_mv937wxj_GEGzecwcebaxlLEgBuE", "roleId": "rol_mv937zof_AD3nbQ1lnvEQJsb6ovw", "resourceScope": "Resource", "resourceId": "agp_mv937xml_XArgAHQfG5v6uUkrkbl", "inheritsToChildren": false}'
```

Response (201):

```json
{
  "id": "ura_mv937zs5_hOaUygjovDH7CO1piZk",
  "tenantId": "default",
  "userId": "usr_mv937wxj_GEGzecwcebaxlLEgBuE",
  "roleId": "rol_mv937zof_AD3nbQ1lnvEQJsb6ovw",
  "roleName": "Night shift",
  "resourceScope": "Resource",
  "resourceId": "agp_mv937xml_XArgAHQfG5v6uUkrkbl",
  "inheritsToChildren": false,
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.925817Z",
  "lastUpdateUtc": "2026-10-15T05:17:49.925817Z"
}
```

### GET `/v1.0/api/tenants/{tenantId}/user-role-assignments/{id}`

One assignment.

**Authentication:** Any REST scheme. Permission: Read on Assignment.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `userId`, `roleId`, `roleName`, `resourceScope`, `resourceId`, `inheritsToChildren`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/tenants/default/user-role-assignments/ura_mv937zs5_hOaUygjovDH7CO1piZk" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "id": "ura_mv937zs5_hOaUygjovDH7CO1piZk",
  "tenantId": "default",
  "userId": "usr_mv937wxj_GEGzecwcebaxlLEgBuE",
  "roleId": "rol_mv937zof_AD3nbQ1lnvEQJsb6ovw",
  "roleName": "Night shift",
  "resourceScope": "Resource",
  "resourceId": "agp_mv937xml_XArgAHQfG5v6uUkrkbl",
  "inheritsToChildren": false,
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.925817Z",
  "lastUpdateUtc": "2026-10-15T05:17:49.925817Z"
}
```

### PUT `/v1.0/api/tenants/{tenantId}/user-role-assignments/{id}`

Update an assignment.

**Authentication:** Any REST scheme. Permission: Update on Assignment.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `userId` | string | User (user role assignments). |
| `credentialId` | string | Credential (credential scope assignments). |
| `roleId` | string | Role by ID. |
| `roleName` | string | Role by name (alternative to `roleId`). |
| `resourceScope` | enum | Tenant (default) or Resource. Values: Tenant, Resource. |
| `resourceId` | string | Target when `resourceScope` is Resource (a resource, agent profile, secret, or conversation ID). |
| `inheritsToChildren` | boolean | Whether the grant covers child objects. |
| `active` | boolean | Default true. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `userId`, `roleId`, `roleName`, `resourceScope`, `resourceId`, `inheritsToChildren`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s -X PUT "$HUGO/v1.0/api/tenants/default/user-role-assignments/ura_mv937zs5_hOaUygjovDH7CO1piZk" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"userId": "usr_mv937wxj_GEGzecwcebaxlLEgBuE", "roleId": "rol_mv937zof_AD3nbQ1lnvEQJsb6ovw", "resourceScope": "Resource", "resourceId": "agp_mv937xml_XArgAHQfG5v6uUkrkbl", "inheritsToChildren": true}'
```

Response (200):

```json
{
  "id": "ura_mv937zs5_hOaUygjovDH7CO1piZk",
  "tenantId": "default",
  "userId": "usr_mv937wxj_GEGzecwcebaxlLEgBuE",
  "roleId": "rol_mv937zof_AD3nbQ1lnvEQJsb6ovw",
  "roleName": "Night shift",
  "resourceScope": "Resource",
  "resourceId": "agp_mv937xml_XArgAHQfG5v6uUkrkbl",
  "inheritsToChildren": true,
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.925817Z",
  "lastUpdateUtc": "2026-10-15T05:17:49.962149Z"
}
```

### DELETE `/v1.0/api/tenants/{tenantId}/user-role-assignments/{id}`

Remove a role assignment.

**Authentication:** Any REST scheme. Permission: Delete on Assignment.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Status codes:** 204 No Content, 401 Not authenticated, 403 Forbidden, 404 Not found.

Example:

```bash
curl -s -X DELETE "$HUGO/v1.0/api/tenants/default/user-role-assignments/ura_mv937zs5_hOaUygjovDH7CO1piZk" \
  -H "Authorization: Bearer $TOKEN"
```

Response: 204 with an empty body.

### GET `/v1.0/api/tenants/{tenantId}/credential-scope-assignments`

Assignments, filtered by `credentialId`.

**Authentication:** Any REST scheme. Permission: Read on Assignment.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `credentialId` | query | string | Only this credential. |
| `maxResults` | query | integer | 1 to 1000. Default 25. |
| `skip` | query | integer | Records to skip. Default 0. |
| `ordering` | query | enum | CreatedDescending (default), CreatedAscending, NameAscending, NameDescending. |
| `search` | query | string | Text to match. |
| `activeOnly` | query | boolean | Only active records. Default false. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `success`, `maxResults`, `skip`, `totalRecords`, `recordsRemaining`, `endOfResults`, `timestampUtc`, `objects`, `objects[].id`, `objects[].tenantId`, `objects[].credentialId`, `objects[].roleId`, `objects[].roleName`, `objects[].resourceScope`, `objects[].resourceId`, `objects[].inheritsToChildren`, `objects[].active`, `objects[].isProtected`, `objects[].createdUtc`, `objects[].lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/tenants/default/credential-scope-assignments?credentialId=crd_mv937zkw_coIvDxQfarwIRkctwpQ" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "success": true,
  "maxResults": 25,
  "skip": 0,
  "totalRecords": 2,
  "recordsRemaining": 0,
  "endOfResults": true,
  "timestampUtc": "2026-10-15T05:17:49.985857Z",
  "objects": [
    {
      "id": "csa_mv937zkw_CyxkSyXrbpqipvnvfHZ",
      "tenantId": "default",
      "credentialId": "crd_mv937zkw_coIvDxQfarwIRkctwpQ",
      "roleId": "rol_builtin_operator",
      "roleName": "Operator",
      "resourceScope": "Tenant",
      "inheritsToChildren": true,
      "active": true,
      "isProtected": false,
      "createdUtc": "2026-10-15T05:17:49.664777Z",
      "lastUpdateUtc": "2026-10-15T05:17:49.664777Z"
    },
    {
      "id": "csa_mv937zti_nLStAhNfmi6UZKi59iH",
      "tenantId": "default",
      "credentialId": "crd_mv937zkw_coIvDxQfarwIRkctwpQ",
      "roleId": "rol_builtin_viewer",
      "roleName": "Viewer",
      "resourceScope": "Resource",
      "resourceId": "res_mv937xm6_qFsDalpjfsqe3DAHONT",
      "inheritsToChildren": true,
      "active": true,
      "isProtected": false,
      "createdUtc": "2026-10-15T05:17:49.974324Z",
      "lastUpdateUtc": "2026-10-15T05:17:49.974324Z"
    }
  ]
}
```

### POST `/v1.0/api/tenants/{tenantId}/credential-scope-assignments`

Add a role scope to a credential. The credential's effective permissions are the intersection of its scope and its owner's permissions.

**Authentication:** Any REST scheme. Permission: Create on Assignment.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `userId` | string | User (user role assignments). |
| `credentialId` | string | Credential (credential scope assignments). |
| `roleId` | string | Role by ID. |
| `roleName` | string | Role by name (alternative to `roleId`). |
| `resourceScope` | enum | Tenant (default) or Resource. Values: Tenant, Resource. |
| `resourceId` | string | Target when `resourceScope` is Resource (a resource, agent profile, secret, or conversation ID). |
| `inheritsToChildren` | boolean | Whether the grant covers child objects. |
| `active` | boolean | Default true. |

**Status codes:** 201 Created, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found, 409 Conflict.

**Response fields:** `id`, `tenantId`, `credentialId`, `roleId`, `roleName`, `resourceScope`, `resourceId`, `inheritsToChildren`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/tenants/default/credential-scope-assignments" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"credentialId": "crd_mv937zkw_coIvDxQfarwIRkctwpQ", "roleName": "Viewer", "resourceScope": "Resource", "resourceId": "res_mv937xm6_qFsDalpjfsqe3DAHONT"}'
```

Response (201):

```json
{
  "id": "csa_mv937zti_nLStAhNfmi6UZKi59iH",
  "tenantId": "default",
  "credentialId": "crd_mv937zkw_coIvDxQfarwIRkctwpQ",
  "roleId": "rol_builtin_viewer",
  "roleName": "Viewer",
  "resourceScope": "Resource",
  "resourceId": "res_mv937xm6_qFsDalpjfsqe3DAHONT",
  "inheritsToChildren": true,
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.974324Z",
  "lastUpdateUtc": "2026-10-15T05:17:49.974324Z"
}
```

### GET `/v1.0/api/tenants/{tenantId}/credential-scope-assignments/{id}`

One assignment.

**Authentication:** Any REST scheme. Permission: Read on Assignment.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `credentialId`, `roleId`, `roleName`, `resourceScope`, `resourceId`, `inheritsToChildren`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/tenants/default/credential-scope-assignments/csa_mv937zti_nLStAhNfmi6UZKi59iH" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "id": "csa_mv937zti_nLStAhNfmi6UZKi59iH",
  "tenantId": "default",
  "credentialId": "crd_mv937zkw_coIvDxQfarwIRkctwpQ",
  "roleId": "rol_builtin_viewer",
  "roleName": "Viewer",
  "resourceScope": "Resource",
  "resourceId": "res_mv937xm6_qFsDalpjfsqe3DAHONT",
  "inheritsToChildren": true,
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.974324Z",
  "lastUpdateUtc": "2026-10-15T05:17:49.974324Z"
}
```

### PUT `/v1.0/api/tenants/{tenantId}/credential-scope-assignments/{id}`

Update an assignment.

**Authentication:** Any REST scheme. Permission: Update on Assignment.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `userId` | string | User (user role assignments). |
| `credentialId` | string | Credential (credential scope assignments). |
| `roleId` | string | Role by ID. |
| `roleName` | string | Role by name (alternative to `roleId`). |
| `resourceScope` | enum | Tenant (default) or Resource. Values: Tenant, Resource. |
| `resourceId` | string | Target when `resourceScope` is Resource (a resource, agent profile, secret, or conversation ID). |
| `inheritsToChildren` | boolean | Whether the grant covers child objects. |
| `active` | boolean | Default true. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `credentialId`, `roleId`, `roleName`, `resourceScope`, `resourceId`, `inheritsToChildren`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s -X PUT "$HUGO/v1.0/api/tenants/default/credential-scope-assignments/csa_mv937zti_nLStAhNfmi6UZKi59iH" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"credentialId": "crd_mv937zkw_coIvDxQfarwIRkctwpQ", "roleName": "Viewer", "resourceScope": "Tenant"}'
```

Response (200):

```json
{
  "id": "csa_mv937zti_nLStAhNfmi6UZKi59iH",
  "tenantId": "default",
  "credentialId": "crd_mv937zkw_coIvDxQfarwIRkctwpQ",
  "roleId": "rol_builtin_viewer",
  "roleName": "Viewer",
  "resourceScope": "Tenant",
  "inheritsToChildren": true,
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.974324Z",
  "lastUpdateUtc": "2026-10-15T05:17:50.008496Z"
}
```

### DELETE `/v1.0/api/tenants/{tenantId}/credential-scope-assignments/{id}`

Remove a credential scope.

**Authentication:** Any REST scheme. Permission: Delete on Assignment.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Status codes:** 204 No Content, 401 Not authenticated, 403 Forbidden, 404 Not found.

Example:

```bash
curl -s -X DELETE "$HUGO/v1.0/api/tenants/default/credential-scope-assignments/csa_mv937zti_nLStAhNfmi6UZKi59iH" \
  -H "Authorization: Bearer $TOKEN"
```

Response: 204 with an empty body.

## Sessions

Sign-ins create session records with the scheme, source IP, user agent, expiry, and last use. Revoking a session kills its token at once. Both the flat and the tenant-scoped paths exist.

| Method | Path | Summary |
|---|---|---|
| GET | `/v1.0/api/sessions` | List authentication sessions in the tenant |
| GET | `/v1.0/api/tenants/{tenantId}/sessions` | List authentication sessions in the tenant |
| GET | `/v1.0/api/tenants/{tenantId}/sessions/{id}` | Read an authentication session |
| DELETE | `/v1.0/api/tenants/{tenantId}/sessions/{id}` | Revoke an authentication session |
| GET | `/v1.0/api/sessions/{id}` | Read an authentication session |
| DELETE | `/v1.0/api/sessions/{id}` | Revoke an authentication session |

### GET `/v1.0/api/sessions`

Sessions in the caller's tenant, newest first. Needs Read on Session.

**Authentication:** Any REST scheme. Permission: Read on Session.

| Parameter | In | Type | Description |
|---|---|---|---|
| `maxResults` | query | integer | 1 to 1000. Default 25. |
| `skip` | query | integer | Records to skip. Default 0. |
| `ordering` | query | enum | CreatedDescending (default), CreatedAscending, NameAscending, NameDescending. |
| `search` | query | string | Text to match. |
| `activeOnly` | query | boolean | Only active records. Default false. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden.

**Response fields:** `success`, `maxResults`, `skip`, `totalRecords`, `recordsRemaining`, `endOfResults`, `timestampUtc`, `objects`, `objects[].id`, `objects[].tenantId`, `objects[].userId`, `objects[].credentialId`, `objects[].principalType`, `objects[].authScheme`, `objects[].sourceIp`, `objects[].userAgent`, `objects[].expiresUtc`, `objects[].lastUsedUtc`, `objects[].revokedUtc`, `objects[].revocationReason`, `objects[].reauthenticatedUtc`, `objects[].active`, `objects[].isProtected`, `objects[].createdUtc`, `objects[].lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/sessions" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "success": true,
  "maxResults": 25,
  "skip": 0,
  "totalRecords": 48,
  "recordsRemaining": 23,
  "endOfResults": false,
  "timestampUtc": "2026-10-15T05:17:50.021545Z",
  "objects": [
    {
      "id": "ses_mv937zmm_lyDbcvfnztT6MGWdzfo",
      "tenantId": "default",
      "credentialId": "crd_mv937zkw_coIvDxQfarwIRkctwpQ",
      "principalType": "Credential",
      "authScheme": "AccessKeySecret",
      "sourceIp": "127.0.0.1",
      "userAgent": "PostmanRuntime/7.39.1",
      "expiresUtc": "2026-10-15T06:17:49.726551Z",
      "active": true,
      "isProtected": false,
      "createdUtc": "2026-10-15T05:17:49.726558Z",
      "lastUpdateUtc": "2026-10-15T05:17:49.726558Z"
    },
    {
      "id": "ses_mv937wvt_ExWweqvfGKP4ygR6e9E",
      "tenantId": "default",
      "userId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
      "principalType": "User",
      "authScheme": "PasswordHeaders",
      "sourceIp": "127.0.0.1",
      "userAgent": "PostmanRuntime/7.39.1",
      "expiresUtc": "2026-10-15T06:17:46.169362Z",
      "revokedUtc": "2026-10-15T05:17:46.791133Z",
      "revocationReason": "Password changed.",
      "active": false,
      "isProtected": false,
      "createdUtc": "2026-10-15T05:17:46.169376Z",
      "lastUpdateUtc": "2026-10-15T05:17:46.791133Z"
    },
    "… 23 more"
  ]
}
```

### GET `/v1.0/api/tenants/{tenantId}/sessions`

The tenant-scoped twin.

**Authentication:** Any REST scheme. Permission: Read on Session.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `maxResults` | query | integer | 1 to 1000. Default 25. |
| `skip` | query | integer | Records to skip. Default 0. |
| `ordering` | query | enum | CreatedDescending (default), CreatedAscending, NameAscending, NameDescending. |
| `search` | query | string | Text to match. |
| `activeOnly` | query | boolean | Only active records. Default false. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `success`, `maxResults`, `skip`, `totalRecords`, `recordsRemaining`, `endOfResults`, `timestampUtc`, `objects`, `objects[].id`, `objects[].tenantId`, `objects[].userId`, `objects[].credentialId`, `objects[].principalType`, `objects[].authScheme`, `objects[].sourceIp`, `objects[].userAgent`, `objects[].expiresUtc`, `objects[].lastUsedUtc`, `objects[].revokedUtc`, `objects[].revocationReason`, `objects[].reauthenticatedUtc`, `objects[].active`, `objects[].isProtected`, `objects[].createdUtc`, `objects[].lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/tenants/default/sessions" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "success": true,
  "maxResults": 25,
  "skip": 0,
  "totalRecords": 48,
  "recordsRemaining": 23,
  "endOfResults": false,
  "timestampUtc": "2026-10-15T05:17:50.034529Z",
  "objects": [
    {
      "id": "ses_mv937zmm_lyDbcvfnztT6MGWdzfo",
      "tenantId": "default",
      "credentialId": "crd_mv937zkw_coIvDxQfarwIRkctwpQ",
      "principalType": "Credential",
      "authScheme": "AccessKeySecret",
      "sourceIp": "127.0.0.1",
      "userAgent": "PostmanRuntime/7.39.1",
      "expiresUtc": "2026-10-15T06:17:49.726551Z",
      "active": true,
      "isProtected": false,
      "createdUtc": "2026-10-15T05:17:49.726558Z",
      "lastUpdateUtc": "2026-10-15T05:17:49.726558Z"
    },
    {
      "id": "ses_mv937wvt_ExWweqvfGKP4ygR6e9E",
      "tenantId": "default",
      "userId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
      "principalType": "User",
      "authScheme": "PasswordHeaders",
      "sourceIp": "127.0.0.1",
      "userAgent": "PostmanRuntime/7.39.1",
      "expiresUtc": "2026-10-15T06:17:46.169362Z",
      "revokedUtc": "2026-10-15T05:17:46.791133Z",
      "revocationReason": "Password changed.",
      "active": false,
      "isProtected": false,
      "createdUtc": "2026-10-15T05:17:46.169376Z",
      "lastUpdateUtc": "2026-10-15T05:17:46.791133Z"
    },
    "… 23 more"
  ]
}
```

### GET `/v1.0/api/tenants/{tenantId}/sessions/{id}`

Tenant-scoped read.

**Authentication:** Any REST scheme. Permission: Read on Session.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `userId`, `credentialId`, `principalType`, `authScheme`, `sourceIp`, `userAgent`, `expiresUtc`, `lastUsedUtc`, `revokedUtc`, `revocationReason`, `reauthenticatedUtc`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/tenants/default/sessions/ses_mv937zmm_lyDbcvfnztT6MGWdzfo" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "id": "ses_mv937zmm_lyDbcvfnztT6MGWdzfo",
  "tenantId": "default",
  "credentialId": "crd_mv937zkw_coIvDxQfarwIRkctwpQ",
  "principalType": "Credential",
  "authScheme": "AccessKeySecret",
  "sourceIp": "127.0.0.1",
  "userAgent": "PostmanRuntime/7.39.1",
  "expiresUtc": "2026-10-15T06:17:49.726551Z",
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.726558Z",
  "lastUpdateUtc": "2026-10-15T05:17:49.726558Z"
}
```

### DELETE `/v1.0/api/tenants/{tenantId}/sessions/{id}`

Tenant-scoped revoke. Revoking an already revoked session is still 204.

**Authentication:** Any REST scheme. Permission: Delete on Session.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Status codes:** 204 No Content, 401 Not authenticated, 403 Forbidden, 404 Not found.

Example:

```bash
curl -s -X DELETE "$HUGO/v1.0/api/tenants/default/sessions/ses_mv937wru_U2mWgTmj04DaN1enN6I" \
  -H "Authorization: Bearer $TOKEN"
```

Response: 204 with an empty body.

### GET `/v1.0/api/sessions/{id}`

One session: principal, scheme, source IP, user agent, expiry, last use, and revocation.

**Authentication:** Any REST scheme. Permission: Read on Session.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `userId`, `credentialId`, `principalType`, `authScheme`, `sourceIp`, `userAgent`, `expiresUtc`, `lastUsedUtc`, `revokedUtc`, `revocationReason`, `reauthenticatedUtc`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/sessions/ses_mv937zmm_lyDbcvfnztT6MGWdzfo" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "id": "ses_mv937zmm_lyDbcvfnztT6MGWdzfo",
  "tenantId": "default",
  "credentialId": "crd_mv937zkw_coIvDxQfarwIRkctwpQ",
  "principalType": "Credential",
  "authScheme": "AccessKeySecret",
  "sourceIp": "127.0.0.1",
  "userAgent": "PostmanRuntime/7.39.1",
  "expiresUtc": "2026-10-15T06:17:49.726551Z",
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.726558Z",
  "lastUpdateUtc": "2026-10-15T05:17:49.726558Z"
}
```

### DELETE `/v1.0/api/sessions/{id}`

Revoke a session (the credential sign-in from the Credentials folder).

**Authentication:** Any REST scheme. Permission: Delete on Session.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Status codes:** 204 No Content, 401 Not authenticated, 403 Forbidden, 404 Not found.

Example:

```bash
curl -s -X DELETE "$HUGO/v1.0/api/sessions/ses_mv937zmm_lyDbcvfnztT6MGWdzfo" \
  -H "Authorization: Bearer $TOKEN"
```

Response: 204 with an empty body.

## Tenants and lockdown

Tenants are the hard isolation boundary. Only system administrators create, update, or delete them; deleting one removes everything it owns, and the seeded `default` tenant is protected. A new tenant is provisioned with its own `Hugo` profile.

Lockdown is per tenant and meant for bad moments: while it is on, every Write and Destructive tool call in the tenant is denied with `tenant_lockdown`, pending approvals are cancelled, and the dashboard shows a red banner. Reads keep working so you can still investigate. Turning it on and off is audited.

| Method | Path | Summary |
|---|---|---|
| GET | `/v1.0/api/tenants` | List tenants visible to the caller |
| POST | `/v1.0/api/tenants` | Create a tenant |
| GET | `/v1.0/api/tenant/lockdown` | Read the tenant's lockdown state |
| POST | `/v1.0/api/tenant/lockdown` | Turn tenant lockdown on: deny every non-Read call and cancel pending approvals |
| DELETE | `/v1.0/api/tenant/lockdown` | Turn tenant lockdown off |
| GET | `/v1.0/api/tenants/{tenantId}` | Read a tenant |
| PUT | `/v1.0/api/tenants/{tenantId}` | Update a tenant |
| DELETE | `/v1.0/api/tenants/{tenantId}` | Delete a tenant and everything it owns |

### GET `/v1.0/api/tenants`

Tenants visible to the caller: all of them for system administrators, the caller's own otherwise.

**Authentication:** Any authenticated principal.

| Parameter | In | Type | Description |
|---|---|---|---|
| `maxResults` | query | integer | 1 to 1000. Default 25. |
| `skip` | query | integer | Records to skip. Default 0. |
| `ordering` | query | enum | CreatedDescending (default), CreatedAscending, NameAscending, NameDescending. |
| `search` | query | string | Text to match. |
| `activeOnly` | query | boolean | Only active records. Default false. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden.

**Response fields:** `success`, `maxResults`, `skip`, `totalRecords`, `recordsRemaining`, `endOfResults`, `timestampUtc`, `objects`, `objects[].id`, `objects[].name`, `objects[].description`, `objects[].active`, `objects[].isProtected`, `objects[].parentTenantId`, `objects[].region`, `objects[].defaultAgentProfileId`, `objects[].lockdown`, `objects[].createdUtc`, `objects[].lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/tenants" \
  -H "Authorization: Bearer $ADMIN_TOKEN"
```

Response (200):

```json
{
  "success": true,
  "maxResults": 25,
  "skip": 0,
  "totalRecords": 1,
  "recordsRemaining": 0,
  "endOfResults": true,
  "timestampUtc": "2026-10-15T05:17:50.090808Z",
  "objects": [
    {
      "id": "default",
      "name": "default",
      "description": "Default tenant created on first boot.",
      "active": true,
      "isProtected": true,
      "defaultAgentProfileId": "agp_mv92uyqb_LM9EdE7i2KwfKe6GfaO",
      "lockdown": false,
      "createdUtc": "2026-10-15T05:07:41.787395Z",
      "lastUpdateUtc": "2026-10-15T05:12:07.208919Z"
    }
  ]
}
```

### POST `/v1.0/api/tenants`

Create a tenant (system administrators). It is provisioned with its own `Hugo` profile.

**Authentication:** Any REST scheme. Permission: Create on Tenant.

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `name` | string | **Required.** Tenant name (on create). |
| `description` | string | Free text. |
| `active` | boolean | Inactive tenants cannot sign in. Default true. |
| `region` | string | Free-text region label. |
| `parentTenantId` | string | Optional parent tenant. |
| `defaultAgentProfileId` | string | Profile used when a caller names none (D-018). |

**Status codes:** 201 Created, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 409 Conflict.

**Response fields:** `id`, `name`, `description`, `active`, `isProtected`, `parentTenantId`, `region`, `defaultAgentProfileId`, `lockdown`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/tenants" \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name": "zion", "description": "Staging tenant for the night shift", "region": "us-east-1"}'
```

Response (201):

```json
{
  "id": "ten_mv937zx2_thDJOZklJfc90aRx5p0",
  "name": "zion",
  "description": "Staging tenant for the night shift",
  "active": true,
  "isProtected": false,
  "region": "us-east-1",
  "defaultAgentProfileId": "agp_mv937zx2_8H0jkEUmT8oCQpZYYHm",
  "lockdown": false,
  "createdUtc": "2026-10-15T05:17:50.102619Z",
  "lastUpdateUtc": "2026-10-15T05:17:50.102619Z"
}
```

### GET `/v1.0/api/tenant/lockdown`

Whether the caller's tenant is locked down.

**Authentication:** Any REST scheme. Permission: Read on Tenant.

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden.

**Response fields:** `tenantId`, `lockdown`, `approvalsCancelled`.

Example:

```bash
curl -s "$HUGO/v1.0/api/tenant/lockdown" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "tenantId": "default",
  "lockdown": false,
  "approvalsCancelled": 0
}
```

### POST `/v1.0/api/tenant/lockdown`

Deny every non-Read call in the tenant and cancel pending approvals. Audited as LockdownChanged.

**Authentication:** Any REST scheme. Permission: Update on Tenant.

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `reason` | string | Why lockdown is on. Recorded in the audit log. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden.

**Response fields:** `tenantId`, `lockdown`, `approvalsCancelled`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/tenant/lockdown" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"reason": "Suspicious writes on prod-db; stopping everything while we look."}'
```

Response (200):

```json
{
  "tenantId": "default",
  "lockdown": true,
  "approvalsCancelled": 0
}
```

### DELETE `/v1.0/api/tenant/lockdown`

Lift lockdown.

**Authentication:** Any REST scheme. Permission: Update on Tenant.

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden.

**Response fields:** `tenantId`, `lockdown`, `approvalsCancelled`.

Example:

```bash
curl -s -X DELETE "$HUGO/v1.0/api/tenant/lockdown" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "tenantId": "default",
  "lockdown": false,
  "approvalsCancelled": 0
}
```

### GET `/v1.0/api/tenants/{tenantId}`

One tenant.

**Authentication:** Any REST scheme. Permission: Read on Tenant.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `name`, `description`, `active`, `isProtected`, `parentTenantId`, `region`, `defaultAgentProfileId`, `lockdown`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/tenants/ten_mv937zx2_thDJOZklJfc90aRx5p0" \
  -H "Authorization: Bearer $ADMIN_TOKEN"
```

Response (200):

```json
{
  "id": "ten_mv937zx2_thDJOZklJfc90aRx5p0",
  "name": "zion",
  "description": "Staging tenant for the night shift",
  "active": true,
  "isProtected": false,
  "region": "us-east-1",
  "defaultAgentProfileId": "agp_mv937zx2_8H0jkEUmT8oCQpZYYHm",
  "lockdown": false,
  "createdUtc": "2026-10-15T05:17:50.102619Z",
  "lastUpdateUtc": "2026-10-15T05:17:50.102619Z"
}
```

### PUT `/v1.0/api/tenants/{tenantId}`

Update name, description, region, active flag, or default profile.

**Authentication:** Any REST scheme. Permission: Update on Tenant.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `name` | string | **Required.** Tenant name (on create). |
| `description` | string | Free text. |
| `active` | boolean | Inactive tenants cannot sign in. Default true. |
| `region` | string | Free-text region label. |
| `parentTenantId` | string | Optional parent tenant. |
| `defaultAgentProfileId` | string | Profile used when a caller names none (D-018). |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `name`, `description`, `active`, `isProtected`, `parentTenantId`, `region`, `defaultAgentProfileId`, `lockdown`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s -X PUT "$HUGO/v1.0/api/tenants/ten_mv937zx2_thDJOZklJfc90aRx5p0" \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"description": "The last human city"}'
```

Response (200):

```json
{
  "id": "ten_mv937zx2_thDJOZklJfc90aRx5p0",
  "name": "zion",
  "description": "The last human city",
  "active": true,
  "isProtected": false,
  "region": "us-east-1",
  "defaultAgentProfileId": "agp_mv937zx2_8H0jkEUmT8oCQpZYYHm",
  "lockdown": false,
  "createdUtc": "2026-10-15T05:17:50.102619Z",
  "lastUpdateUtc": "2026-10-15T05:17:50.125961Z"
}
```

### DELETE `/v1.0/api/tenants/{tenantId}`

Delete a tenant and everything it owns (system administrators). The default tenant is protected.

**Authentication:** Any REST scheme. Permission: Delete on Tenant.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |

**Status codes:** 204 No Content, 401 Not authenticated, 403 Forbidden, 404 Not found.

Example:

```bash
curl -s -X DELETE "$HUGO/v1.0/api/tenants/ten_mv937zx2_thDJOZklJfc90aRx5p0" \
  -H "Authorization: Bearer $ADMIN_TOKEN"
```

Response: 204 with an empty body.

## Secrets

The vault stores credential material for connectors and model endpoints. Values are encrypted with AES-256-GCM under a key derived from the master key, with the tenant, secret ID, and field name bound into each ciphertext, so a value copied to another row does not decrypt. Read routes return metadata, field names, a fingerprint, and the list of referrers, never values.

Reveal is the one exception and it is deliberately awkward: it needs the Reveal permission (tenant administrators and system administrators), a written reason, and a password re-entry on the same session within `Secrets.RevealReauthMinutes` (5). Every reveal is audited as SecretRevealed, rate limited, and kept out of request history.

Two behaviors surprise people. Updating values replaces only the fields you send, and a field sent as the mask placeholder keeps its stored value, so a form can round-trip without ever holding the plaintext. And deleting a secret that a resource or model endpoint still references answers 409 until you remove the reference.

| Method | Path | Summary |
|---|---|---|
| GET | `/v1.0/api/secrets` | List secrets (metadata and field names only) |
| POST | `/v1.0/api/secrets` | Create a secret with its field values; returns metadata only |
| POST | `/v1.0/api/secrets/parse` | Describe an uploaded SSH key, GCP service account JSON, kubeconfig, or PEM certificate without storing it |
| GET | `/v1.0/api/tenants/{tenantId}/secrets` | List secrets (metadata and field names only) |
| POST | `/v1.0/api/tenants/{tenantId}/secrets` | Create a secret with its field values; returns metadata only |
| POST | `/v1.0/api/tenants/{tenantId}/secrets/parse` | Describe an uploaded SSH key, GCP service account JSON, kubeconfig, or PEM certificate without storing it |
| GET | `/v1.0/api/tenants/{tenantId}/secrets/{id}` | Read secret metadata, field names, fingerprint, and referrers (never values) |
| PUT | `/v1.0/api/tenants/{tenantId}/secrets/{id}` | Update secret name, description, and expiry |
| DELETE | `/v1.0/api/tenants/{tenantId}/secrets/{id}` | Delete a secret (409 while referenced) |
| PUT | `/v1.0/api/tenants/{tenantId}/secrets/{id}/values` | Replace the supplied field values (omitted fields and the mask placeholder keep stored values) |
| GET | `/v1.0/api/tenants/{tenantId}/secrets/{id}/referrers` | Resources and model endpoints that use the secret |
| POST | `/v1.0/api/tenants/{tenantId}/secrets/{id}/reveal` | Reveal plaintext values (Reveal permission, reason, and re-authentication within Secrets.RevealReauthMinutes required; audited; never captured in request history) |
| GET | `/v1.0/api/secrets/{id}` | Read secret metadata, field names, fingerprint, and referrers (never values) |
| PUT | `/v1.0/api/secrets/{id}` | Update secret name, description, and expiry |
| DELETE | `/v1.0/api/secrets/{id}` | Delete a secret (409 while referenced) |
| PUT | `/v1.0/api/secrets/{id}/values` | Replace the supplied field values (omitted fields and the mask placeholder keep stored values) |
| GET | `/v1.0/api/secrets/{id}/referrers` | Resources and model endpoints that use the secret |
| POST | `/v1.0/api/secrets/{id}/reveal` | Reveal plaintext values (Reveal permission, reason, and re-authentication within Secrets.RevealReauthMinutes required; audited; never captured in request history) |

### GET `/v1.0/api/secrets`

Secret metadata and field names, never values.

**Authentication:** Any REST scheme. Permission: Read on Secret.

| Parameter | In | Type | Description |
|---|---|---|---|
| `maxResults` | query | integer | 1 to 1000. Default 25. |
| `skip` | query | integer | Records to skip. Default 0. |
| `ordering` | query | enum | CreatedDescending (default), CreatedAscending, NameAscending, NameDescending. |
| `search` | query | string | Text to match. |
| `activeOnly` | query | boolean | Only active records. Default false. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden.

**Response fields:** `success`, `maxResults`, `skip`, `totalRecords`, `recordsRemaining`, `endOfResults`, `timestampUtc`, `objects`, `objects[].id`, `objects[].tenantId`, `objects[].ownerUserId`, `objects[].name`, `objects[].kind`, `objects[].description`, `objects[].keyVersion`, `objects[].fingerprint`, `objects[].lastRotatedUtc`, `objects[].lastRevealedUtc`, `objects[].expiresUtc`, `objects[].active`, `objects[].isProtected`, `objects[].createdUtc`, `objects[].lastUpdateUtc`, `objects[].fields`, `objects[].referrers`.

Example:

```bash
curl -s "$HUGO/v1.0/api/secrets" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```text
{
  "success": true,
  "maxResults": 25,
  "skip": 0,
  "totalRecords": 3,
  "recordsRemaining": 0,
  "endOfResults": true,
  "timestampUtc": "2026-10-15T05:17:49.383006Z",
  "objects": [
    {
      "id": "sec_mv937zcp_sEmwxNsnTg5N6NBDWea",
      "tenantId": "default",
      "ownerUserId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
      "name": "demo-api-token",
      "kind": "ApiToken",
      "description": "Token for the status page API",
      "keyVersion": 1,
      "fingerprint": "****6789",
      "lastRotatedUtc": "2026-10-15T05:17:49.369892Z",
      "active": true,
      "isProtected": false,
      "createdUtc": "2026-10-15T05:17:49.369895Z",
      "lastUpdateUtc": "2026-10-15T05:17:49.369895Z",
      "fields": [
        "token"
      ]
    },
    {
      "id": "sec_mv937xyz_c0RnJRWnBkYs9qNjuxT",
      "tenantId": "default",
      "ownerUserId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
      "name": "dryrun-token",
      "kind": "ApiToken",
      "keyVersion": 1,
      "fingerprint": "****oken",
      "lastRotatedUtc": "2026-10-15T05:17:47.579861Z",
      "active": true,
      "isProtected": false,
      "createdUtc": "2026-10-15T05:17:47.579863Z",
      "lastUpdateUtc": "2026-10-15T05:17:47.579863Z",
      "fields": [
        "token"
      ]
    },
    {
      "id": "sec_mv937xyn_dfLnh9spzKfB1ZjMrUd",
      "tenantId": "default",
      "ownerUserId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
      "name": "dryrun-password",
      "kind": "Password",
      "keyVersion": 1,
      "fingerprint": "****word",
      "lastRotatedUtc": "2026-10-15T05:17:47.567193Z",
      "active": true,
      "isProtected": false,
…
```

### POST `/v1.0/api/secrets`

Create a secret with its values; the response has metadata and field names only.

**Authentication:** Any REST scheme. Permission: Create on Secret.

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `name` | string | **Required.** Unique within the tenant. Required on update too in 0.1.0-alpha. |
| `description` | string | Free text. |
| `kind` | enum | **Required.** Password, PrivateKey, ApiToken, AccessKeyPair, ServiceAccountJson, Kubeconfig, ConnectionString, Certificate, or Generic. Cannot change. Values: Password, PrivateKey, ApiToken, AccessKeyPair, ServiceAccountJson, Kubeconfig, ConnectionString, Certificate, Generic. |
| `values` | object | **Required.** Field values by name (on create). Password: `password`, optional `username`. PrivateKey: `privateKey`, optional `passphrase`. ApiToken: `token`. AccessKeyPair: `accessKeyId`, `secretAccessKey`, optional `sessionToken`. ServiceAccountJson: `json`. Kubeconfig: `kubeconfig`. ConnectionString: `connectionString`. Certificate: `certificate`, optional `privateKey`, `caCertificate`. Generic: any names. Ignored on the metadata update. |
| `expiresUtc` | date-time | Optional expiry, shown in the dashboard. |

**Status codes:** 201 Created, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 409 Conflict.

**Response fields:** `id`, `tenantId`, `ownerUserId`, `name`, `kind`, `description`, `keyVersion`, `fingerprint`, `lastRotatedUtc`, `lastRevealedUtc`, `expiresUtc`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`, `fields`, `referrers`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/secrets" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name": "demo-api-token", "kind": "ApiToken", "description": "Token for the status page API", "values": {"token": "not-a-real-token-0123456789"}}'
```

Response (201):

```json
{
  "id": "sec_mv937zcp_sEmwxNsnTg5N6NBDWea",
  "tenantId": "default",
  "ownerUserId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "name": "demo-api-token",
  "kind": "ApiToken",
  "description": "Token for the status page API",
  "keyVersion": 1,
  "fingerprint": "****6789",
  "lastRotatedUtc": "2026-10-15T05:17:49.369892Z",
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.369895Z",
  "lastUpdateUtc": "2026-10-15T05:17:49.369895Z",
  "fields": [
    "token"
  ]
}
```

### POST `/v1.0/api/secrets/parse`

Describe an SSH private key, GCP service account JSON, kubeconfig, or PEM certificate without storing it: kind, fingerprint, contexts, subject, and warnings. The dashboard uses it to show what a file contains before saving.

**Authentication:** Any REST scheme. Permission: Create on Secret.

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `content` | string | **Required.** File content (PEM, OpenSSH key, JSON, or kubeconfig YAML). |
| `passphrase` | string | Passphrase of an encrypted private key. |
| `fileName` | string | Original file name, used as a format hint. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden.

**Response fields:** `kind`, `format`, `suggestedField`, `fields`, `fingerprint`, `keyType`, `keySizeBits`, `encrypted`, `comment`, `projectId`, `clientEmail`, `privateKeyId`, `contexts`, `currentContext`, `subject`, `issuer`, `notBeforeUtc`, `notAfterUtc`, `certificateCount`, `hasPrivateKey`, `warnings`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/secrets/parse" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"content": "apiVersion: v1\nkind: Config\ncurrent-context: prod\nclusters:\n- name: prod\n  cluster:\n    server: https://10.0.0.50:6443\ncontexts:\n- name: prod\n  context:\n    cluster: prod\n    user: hugo\n    namespace: shop\nusers:\n- name: hugo\n  user:\n    token: not-a-real-token\n", "fileName": "kubeconfig"}'
```

Response (200):

```json
{
  "kind": "Kubeconfig",
  "format": "Kubeconfig",
  "suggestedField": "kubeconfig",
  "fields": [
    "kubeconfig"
  ],
  "encrypted": false,
  "contexts": [
    {
      "name": "prod",
      "cluster": "prod",
      "server": "https://10.0.0.50:6443",
      "user": "hugo",
      "namespace": "shop"
    }
  ],
  "currentContext": "prod",
  "certificateCount": 0,
  "hasPrivateKey": false,
  "warnings": []
}
```

### GET `/v1.0/api/tenants/{tenantId}/secrets`

The tenant-scoped twin of List secrets.

**Authentication:** Any REST scheme. Permission: Read on Secret.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `maxResults` | query | integer | 1 to 1000. Default 25. |
| `skip` | query | integer | Records to skip. Default 0. |
| `ordering` | query | enum | CreatedDescending (default), CreatedAscending, NameAscending, NameDescending. |
| `search` | query | string | Text to match. |
| `activeOnly` | query | boolean | Only active records. Default false. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `success`, `maxResults`, `skip`, `totalRecords`, `recordsRemaining`, `endOfResults`, `timestampUtc`, `objects`, `objects[].id`, `objects[].tenantId`, `objects[].ownerUserId`, `objects[].name`, `objects[].kind`, `objects[].description`, `objects[].keyVersion`, `objects[].fingerprint`, `objects[].lastRotatedUtc`, `objects[].lastRevealedUtc`, `objects[].expiresUtc`, `objects[].active`, `objects[].isProtected`, `objects[].createdUtc`, `objects[].lastUpdateUtc`, `objects[].fields`, `objects[].referrers`.

Example:

```bash
curl -s "$HUGO/v1.0/api/tenants/default/secrets" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```text
{
  "success": true,
  "maxResults": 25,
  "skip": 0,
  "totalRecords": 3,
  "recordsRemaining": 0,
  "endOfResults": true,
  "timestampUtc": "2026-10-15T05:17:49.553194Z",
  "objects": [
    {
      "id": "sec_mv937zcp_sEmwxNsnTg5N6NBDWea",
      "tenantId": "default",
      "ownerUserId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
      "name": "demo-api-token",
      "kind": "ApiToken",
      "description": "Example token, rotated quarterly",
      "keyVersion": 1,
      "fingerprint": "****9876",
      "lastRotatedUtc": "2026-10-15T05:17:49.420816Z",
      "lastRevealedUtc": "2026-10-15T05:17:49.539728Z",
      "expiresUtc": "2027-01-01T00:00:00Z",
      "active": true,
      "isProtected": false,
      "createdUtc": "2026-10-15T05:17:49.369895Z",
      "lastUpdateUtc": "2026-10-15T05:17:49.539729Z",
      "fields": [
        "token"
      ]
    },
    {
      "id": "sec_mv937xyz_c0RnJRWnBkYs9qNjuxT",
      "tenantId": "default",
      "ownerUserId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
      "name": "dryrun-token",
      "kind": "ApiToken",
      "keyVersion": 1,
      "fingerprint": "****oken",
      "lastRotatedUtc": "2026-10-15T05:17:47.579861Z",
      "active": true,
      "isProtected": false,
      "createdUtc": "2026-10-15T05:17:47.579863Z",
      "lastUpdateUtc": "2026-10-15T05:17:47.579863Z",
      "fields": [
        "token"
      ]
    },
    {
      "id": "sec_mv937xyn_dfLnh9spzKfB1ZjMrUd",
      "tenantId": "default",
      "ownerUserId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
      "name": "dryrun-password",
      "kind": "Password",
      "keyVersion": 1,
      "fingerprint": "****word",
      "lastRotatedUtc": "2026-10-15T05:17:47.567193Z",
…
```

### POST `/v1.0/api/tenants/{tenantId}/secrets`

Create a Password secret through the tenant-scoped path.

**Authentication:** Any REST scheme. Permission: Create on Secret.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `name` | string | **Required.** Unique within the tenant. Required on update too in 0.1.0-alpha. |
| `description` | string | Free text. |
| `kind` | enum | **Required.** Password, PrivateKey, ApiToken, AccessKeyPair, ServiceAccountJson, Kubeconfig, ConnectionString, Certificate, or Generic. Cannot change. Values: Password, PrivateKey, ApiToken, AccessKeyPair, ServiceAccountJson, Kubeconfig, ConnectionString, Certificate, Generic. |
| `values` | object | **Required.** Field values by name (on create). Password: `password`, optional `username`. PrivateKey: `privateKey`, optional `passphrase`. ApiToken: `token`. AccessKeyPair: `accessKeyId`, `secretAccessKey`, optional `sessionToken`. ServiceAccountJson: `json`. Kubeconfig: `kubeconfig`. ConnectionString: `connectionString`. Certificate: `certificate`, optional `privateKey`, `caCertificate`. Generic: any names. Ignored on the metadata update. |
| `expiresUtc` | date-time | Optional expiry, shown in the dashboard. |

**Status codes:** 201 Created, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found, 409 Conflict.

**Response fields:** `id`, `tenantId`, `ownerUserId`, `name`, `kind`, `description`, `keyVersion`, `fingerprint`, `lastRotatedUtc`, `lastRevealedUtc`, `expiresUtc`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`, `fields`, `referrers`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/tenants/default/secrets" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name": "demo-db-password", "kind": "Password", "values": {"username": "hugo_ro", "password": "not-a-real-password"}}'
```

Response (201):

```json
{
  "id": "sec_mv937zi5_VNEmIR3h9yX4e19gn0w",
  "tenantId": "default",
  "ownerUserId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "name": "demo-db-password",
  "kind": "Password",
  "keyVersion": 1,
  "fingerprint": "****word",
  "lastRotatedUtc": "2026-10-15T05:17:49.56546Z",
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.565462Z",
  "lastUpdateUtc": "2026-10-15T05:17:49.565462Z",
  "fields": [
    "password",
    "username"
  ]
}
```

### POST `/v1.0/api/tenants/{tenantId}/secrets/parse`

Tenant-scoped parse of a PEM file. Unrecognized content is reported in `warnings`.

**Authentication:** Any REST scheme. Permission: Create on Secret.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `content` | string | **Required.** File content (PEM, OpenSSH key, JSON, or kubeconfig YAML). |
| `passphrase` | string | Passphrase of an encrypted private key. |
| `fileName` | string | Original file name, used as a format hint. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `kind`, `format`, `suggestedField`, `fields`, `fingerprint`, `keyType`, `keySizeBits`, `encrypted`, `comment`, `projectId`, `clientEmail`, `privateKeyId`, `contexts`, `currentContext`, `subject`, `issuer`, `notBeforeUtc`, `notAfterUtc`, `certificateCount`, `hasPrivateKey`, `warnings`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/tenants/default/secrets/parse" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"content": "not a key", "fileName": "notes.txt"}'
```

Response (400):

```json
{
  "error": "BadRequest",
  "code": "BadRequest",
  "message": "The file is not a recognized SSH private key, PEM certificate, Google Cloud service account key, or kubeconfig. (Parameter 'request')",
  "requestId": "c8857a0ced7a4692a61ed9c36d166764"
}
```

### GET `/v1.0/api/tenants/{tenantId}/secrets/{id}`

Tenant-scoped read.

**Authentication:** Any REST scheme. Permission: Read on Secret.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `ownerUserId`, `name`, `kind`, `description`, `keyVersion`, `fingerprint`, `lastRotatedUtc`, `lastRevealedUtc`, `expiresUtc`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`, `fields`, `referrers`.

Example:

```bash
curl -s "$HUGO/v1.0/api/tenants/default/secrets/sec_mv937zi5_VNEmIR3h9yX4e19gn0w" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "id": "sec_mv937zi5_VNEmIR3h9yX4e19gn0w",
  "tenantId": "default",
  "ownerUserId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "name": "demo-db-password",
  "kind": "Password",
  "keyVersion": 1,
  "fingerprint": "****word",
  "lastRotatedUtc": "2026-10-15T05:17:49.56546Z",
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.565462Z",
  "lastUpdateUtc": "2026-10-15T05:17:49.565462Z",
  "fields": [
    "password",
    "username"
  ],
  "referrers": []
}
```

### PUT `/v1.0/api/tenants/{tenantId}/secrets/{id}`

Tenant-scoped metadata update.

**Authentication:** Any REST scheme. Permission: Update on Secret.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `name` | string | **Required.** Unique within the tenant. Required on update too in 0.1.0-alpha. |
| `description` | string | Free text. |
| `kind` | enum | **Required.** Password, PrivateKey, ApiToken, AccessKeyPair, ServiceAccountJson, Kubeconfig, ConnectionString, Certificate, or Generic. Cannot change. Values: Password, PrivateKey, ApiToken, AccessKeyPair, ServiceAccountJson, Kubeconfig, ConnectionString, Certificate, Generic. |
| `values` | object | **Required.** Field values by name (on create). Password: `password`, optional `username`. PrivateKey: `privateKey`, optional `passphrase`. ApiToken: `token`. AccessKeyPair: `accessKeyId`, `secretAccessKey`, optional `sessionToken`. ServiceAccountJson: `json`. Kubeconfig: `kubeconfig`. ConnectionString: `connectionString`. Certificate: `certificate`, optional `privateKey`, `caCertificate`. Generic: any names. Ignored on the metadata update. |
| `expiresUtc` | date-time | Optional expiry, shown in the dashboard. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `ownerUserId`, `name`, `kind`, `description`, `keyVersion`, `fingerprint`, `lastRotatedUtc`, `lastRevealedUtc`, `expiresUtc`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`, `fields`, `referrers`.

Example:

```bash
curl -s -X PUT "$HUGO/v1.0/api/tenants/default/secrets/sec_mv937zi5_VNEmIR3h9yX4e19gn0w" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name": "demo-db-password", "kind": "Password", "description": "Read-only login for the demo database"}'
```

Response (200):

```json
{
  "id": "sec_mv937zi5_VNEmIR3h9yX4e19gn0w",
  "tenantId": "default",
  "ownerUserId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "name": "demo-db-password",
  "kind": "Password",
  "description": "Read-only login for the demo database",
  "keyVersion": 1,
  "fingerprint": "****word",
  "lastRotatedUtc": "2026-10-15T05:17:49.56546Z",
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.565462Z",
  "lastUpdateUtc": "2026-10-15T05:17:49.590448Z",
  "fields": [
    "password",
    "username"
  ]
}
```

### DELETE `/v1.0/api/tenants/{tenantId}/secrets/{id}`

Delete a secret. 409 while a resource or model endpoint references it.

**Authentication:** Any REST scheme. Permission: Delete on Secret.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Status codes:** 204 No Content, 401 Not authenticated, 403 Forbidden, 404 Not found, 409 Conflict.

Example:

```bash
curl -s -X DELETE "$HUGO/v1.0/api/tenants/default/secrets/sec_mv937zi5_VNEmIR3h9yX4e19gn0w" \
  -H "Authorization: Bearer $TOKEN"
```

Response: 204 with an empty body.

### PUT `/v1.0/api/tenants/{tenantId}/secrets/{id}/values`

Tenant-scoped value update. `remove` drops an optional field.

**Authentication:** Any REST scheme. Permission: Update on Secret.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `values` | object | Fields to replace. A field sent as the mask placeholder keeps its stored value. |
| `remove` | string[] | Optional fields to delete. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `ownerUserId`, `name`, `kind`, `description`, `keyVersion`, `fingerprint`, `lastRotatedUtc`, `lastRevealedUtc`, `expiresUtc`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`, `fields`, `referrers`.

Example:

```bash
curl -s -X PUT "$HUGO/v1.0/api/tenants/default/secrets/sec_mv937zi5_VNEmIR3h9yX4e19gn0w/values" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"values": {"password": "not-a-real-password-2"}, "remove": ["username"]}'
```

Response (200):

```json
{
  "id": "sec_mv937zi5_VNEmIR3h9yX4e19gn0w",
  "tenantId": "default",
  "ownerUserId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "name": "demo-db-password",
  "kind": "Password",
  "description": "Read-only login for the demo database",
  "keyVersion": 1,
  "fingerprint": "****rd-2",
  "lastRotatedUtc": "2026-10-15T05:17:49.603195Z",
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.565462Z",
  "lastUpdateUtc": "2026-10-15T05:17:49.603197Z",
  "fields": [
    "password"
  ]
}
```

### GET `/v1.0/api/tenants/{tenantId}/secrets/{id}/referrers`

Tenant-scoped referrers.

**Authentication:** Any REST scheme. Permission: Read on Secret.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `[]referrerType`, `[]referrerId`, `[]referrerName`, `[]detail`.

Example:

```bash
curl -s "$HUGO/v1.0/api/tenants/default/secrets/sec_mv937zi5_VNEmIR3h9yX4e19gn0w/referrers" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
[]
```

### POST `/v1.0/api/tenants/{tenantId}/secrets/{id}/reveal`

Tenant-scoped reveal.

**Authentication:** Any REST scheme. Permission: Reveal on Secret.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `reason` | string | **Required.** Why the plaintext is needed. Recorded in the audit log. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found, 429 Rate limited.

**Response fields:** `secretId`, `name`, `kind`, `values`, `revealedUtc`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/tenants/default/secrets/sec_mv937zi5_VNEmIR3h9yX4e19gn0w/reveal" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"reason": "Checking the demo database password"}'
```

Response (200):

```json
{
  "secretId": "sec_mv937zi5_VNEmIR3h9yX4e19gn0w",
  "name": "demo-db-password",
  "kind": "Password",
  "values": {
    "password": "not-a-real-password-2"
  },
  "revealedUtc": "2026-10-15T05:17:49.638763Z"
}
```

### GET `/v1.0/api/secrets/{id}`

Metadata, field names, fingerprint, and referrers.

**Authentication:** Any REST scheme. Permission: Read on Secret.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `ownerUserId`, `name`, `kind`, `description`, `keyVersion`, `fingerprint`, `lastRotatedUtc`, `lastRevealedUtc`, `expiresUtc`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`, `fields`, `referrers`.

Example:

```bash
curl -s "$HUGO/v1.0/api/secrets/sec_mv937zcp_sEmwxNsnTg5N6NBDWea" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "id": "sec_mv937zcp_sEmwxNsnTg5N6NBDWea",
  "tenantId": "default",
  "ownerUserId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "name": "demo-api-token",
  "kind": "ApiToken",
  "description": "Token for the status page API",
  "keyVersion": 1,
  "fingerprint": "****6789",
  "lastRotatedUtc": "2026-10-15T05:17:49.369892Z",
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.369895Z",
  "lastUpdateUtc": "2026-10-15T05:17:49.369895Z",
  "fields": [
    "token"
  ],
  "referrers": []
}
```

### PUT `/v1.0/api/secrets/{id}`

Change name, description, or expiry.

**Authentication:** Any REST scheme. Permission: Update on Secret.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `name` | string | **Required.** Unique within the tenant. Required on update too in 0.1.0-alpha. |
| `description` | string | Free text. |
| `kind` | enum | **Required.** Password, PrivateKey, ApiToken, AccessKeyPair, ServiceAccountJson, Kubeconfig, ConnectionString, Certificate, or Generic. Cannot change. Values: Password, PrivateKey, ApiToken, AccessKeyPair, ServiceAccountJson, Kubeconfig, ConnectionString, Certificate, Generic. |
| `values` | object | **Required.** Field values by name (on create). Password: `password`, optional `username`. PrivateKey: `privateKey`, optional `passphrase`. ApiToken: `token`. AccessKeyPair: `accessKeyId`, `secretAccessKey`, optional `sessionToken`. ServiceAccountJson: `json`. Kubeconfig: `kubeconfig`. ConnectionString: `connectionString`. Certificate: `certificate`, optional `privateKey`, `caCertificate`. Generic: any names. Ignored on the metadata update. |
| `expiresUtc` | date-time | Optional expiry, shown in the dashboard. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `ownerUserId`, `name`, `kind`, `description`, `keyVersion`, `fingerprint`, `lastRotatedUtc`, `lastRevealedUtc`, `expiresUtc`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`, `fields`, `referrers`.

Example:

```bash
curl -s -X PUT "$HUGO/v1.0/api/secrets/sec_mv937zcp_sEmwxNsnTg5N6NBDWea" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name": "demo-api-token", "kind": "ApiToken", "description": "Example token, rotated quarterly", "expiresUtc": "2027-01-01T00:00:00Z"}'
```

Response (200):

```json
{
  "id": "sec_mv937zcp_sEmwxNsnTg5N6NBDWea",
  "tenantId": "default",
  "ownerUserId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "name": "demo-api-token",
  "kind": "ApiToken",
  "description": "Example token, rotated quarterly",
  "keyVersion": 1,
  "fingerprint": "****6789",
  "lastRotatedUtc": "2026-10-15T05:17:49.369892Z",
  "expiresUtc": "2027-01-01T00:00:00Z",
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.369895Z",
  "lastUpdateUtc": "2026-10-15T05:17:49.408443Z",
  "fields": [
    "token"
  ]
}
```

### DELETE `/v1.0/api/secrets/{id}`

Delete a secret. 409 while referenced.

**Authentication:** Any REST scheme. Permission: Delete on Secret.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Status codes:** 204 No Content, 401 Not authenticated, 403 Forbidden, 404 Not found, 409 Conflict.

Example:

```bash
curl -s -X DELETE "$HUGO/v1.0/api/secrets/sec_mv937zcp_sEmwxNsnTg5N6NBDWea" \
  -H "Authorization: Bearer $TOKEN"
```

Response: 204 with an empty body.

### PUT `/v1.0/api/secrets/{id}/values`

Replace the supplied fields. Omitted fields, and fields sent as the mask placeholder, keep their stored values; `remove` deletes optional fields.

**Authentication:** Any REST scheme. Permission: Update on Secret.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `values` | object | Fields to replace. A field sent as the mask placeholder keeps its stored value. |
| `remove` | string[] | Optional fields to delete. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `ownerUserId`, `name`, `kind`, `description`, `keyVersion`, `fingerprint`, `lastRotatedUtc`, `lastRevealedUtc`, `expiresUtc`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`, `fields`, `referrers`.

Example:

```bash
curl -s -X PUT "$HUGO/v1.0/api/secrets/sec_mv937zcp_sEmwxNsnTg5N6NBDWea/values" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"values": {"token": "not-a-real-token-rotated-9876"}}'
```

Response (200):

```json
{
  "id": "sec_mv937zcp_sEmwxNsnTg5N6NBDWea",
  "tenantId": "default",
  "ownerUserId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "name": "demo-api-token",
  "kind": "ApiToken",
  "description": "Example token, rotated quarterly",
  "keyVersion": 1,
  "fingerprint": "****9876",
  "lastRotatedUtc": "2026-10-15T05:17:49.420816Z",
  "expiresUtc": "2027-01-01T00:00:00Z",
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.369895Z",
  "lastUpdateUtc": "2026-10-15T05:17:49.420818Z",
  "fields": [
    "token"
  ]
}
```

### GET `/v1.0/api/secrets/{id}/referrers`

Resources and model endpoints that use the secret. Delete answers 409 while any exist.

**Authentication:** Any REST scheme. Permission: Read on Secret.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `[]referrerType`, `[]referrerId`, `[]referrerName`, `[]detail`.

Example:

```bash
curl -s "$HUGO/v1.0/api/secrets/sec_mv937zcp_sEmwxNsnTg5N6NBDWea/referrers" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
[]
```

### POST `/v1.0/api/secrets/{id}/reveal`

Return plaintext values. Needs Reveal on Secret (TenantAdmin and system administrators), a reason, and a recent re-authentication. Audited as SecretRevealed and rate limited (`Secrets.RevealRateLimitPerMinute`).

**Authentication:** Any REST scheme. Permission: Reveal on Secret.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `reason` | string | **Required.** Why the plaintext is needed. Recorded in the audit log. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found, 429 Rate limited.

**Response fields:** `secretId`, `name`, `kind`, `values`, `revealedUtc`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/secrets/sec_mv937zcp_sEmwxNsnTg5N6NBDWea/reveal" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"reason": "Confirming the rotated token before handing it to the on-call engineer"}'
```

Response (200):

```json
{
  "secretId": "sec_mv937zcp_sEmwxNsnTg5N6NBDWea",
  "name": "demo-api-token",
  "kind": "ApiToken",
  "values": {
    "token": "not-a-real-token-rotated-9876"
  },
  "revealedUtc": "2026-10-15T05:17:49.539728Z"
}
```

## Master key

System administrators can see the master key version and re-encrypt every stored secret value and credential secret under the current version. The rotation runs in batches in the background, survives restarts (call it again to resume), and reports progress on the status route. The procedure, including which environment variables to change first, is in [docs/SECURITY.md](docs/SECURITY.md) and [docs/UPGRADING.md](docs/UPGRADING.md).

| Method | Path | Summary |
|---|---|---|
| GET | `/v1.0/api/system/master-key` | Master key version and rotation progress (system administrators) |
| POST | `/v1.0/api/system/master-key/rotate` | Re-encrypt every secret value and credential secret under the current master key version, in batches; resumable (system administrators) |

### GET `/v1.0/api/system/master-key`

Current master key version and the progress of a rotation (system administrators).

**Authentication:** Any REST scheme. Permission: Read on Admin.

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden.

**Response fields:** `running`, `targetKeyVersion`, `total`, `completed`, `failed`, `remaining`, `startedUtc`, `completedUtc`, `lastError`.

Example:

```bash
curl -s "$HUGO/v1.0/api/system/master-key" \
  -H "Authorization: Bearer $ADMIN_TOKEN"
```

Response (200):

```json
{
  "running": false,
  "targetKeyVersion": 1,
  "total": 0,
  "completed": 0,
  "failed": 0,
  "remaining": 0,
  "startedUtc": "2026-10-15T05:12:07.442203Z",
  "completedUtc": "2026-10-15T05:12:07.442386Z"
}
```

### POST `/v1.0/api/system/master-key/rotate`

Re-encrypt every secret value and credential secret under the current master key version, in batches (`Secrets.RotationBatchSize`). Resumable. Set `HUGO_MASTER_KEY` to the new key, `HUGO_MASTER_KEY_PREVIOUS` to the old one, and raise `HUGO_MASTER_KEY_VERSION` before calling it; with one key it simply re-encrypts under the same version. Answers 202 and runs in the background; poll the status route.

**Authentication:** Any REST scheme. Permission: Admin on Admin.

**Status codes:** 202 Accepted (approval pending or work started), 401 Not authenticated, 403 Forbidden.

**Response fields:** `running`, `targetKeyVersion`, `total`, `completed`, `failed`, `remaining`, `startedUtc`, `completedUtc`, `lastError`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/system/master-key/rotate" \
  -H "Authorization: Bearer $ADMIN_TOKEN"
```

Response (202):

```json
{
  "running": true,
  "targetKeyVersion": 1,
  "total": 0,
  "completed": 0,
  "failed": 0,
  "remaining": 0,
  "startedUtc": "2026-10-15T05:17:50.417523Z"
}
```

## Connectors

A connector is compiled code for one service family; there are 20 connector types in this release. The descriptor is everything a client needs to build a resource form and to know what an agent can do: the settings schema (keys, kinds, defaults, ranges, allowed values, grouping), the secret slots with the secret kinds each accepts, and the tools with their input schemas and base effects. [docs/CONNECTORS.md](docs/CONNECTORS.md) explains each connector in prose, and [docs/MCP_CONNECTOR_TOOLS.md](docs/MCP_CONNECTOR_TOOLS.md) documents every tool.

| Method | Path | Summary |
|---|---|---|
| GET | `/v1.0/api/connectors` | List connectors with settings schema, secret slots, and tools |
| GET | `/v1.0/api/connectors/{type}` | Read one connector descriptor with settings schema, secret slots, and tools |

### GET `/v1.0/api/connectors`

Every loaded connector with settings schema, secret slots, and tools. Needs Read on Connector.

**Authentication:** Any REST scheme. Permission: Read on Connector.

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden.

**Response fields:** `[]type`, `[]family`, `[]displayName`, `[]description`, `[]category`, `[]iconKey`, `[]version`, `[]documentationUrl`, `[]defaultInstructionsKey`, `[]plugin`, `[]settings`, `[]secretSlots`, `[]tools`.

Example:

```bash
curl -s "$HUGO/v1.0/api/connectors" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```text
[
  {
    "type": "ssh",
    "family": "ssh",
    "displayName": "SSH",
    "description": "Linux and Unix hosts over SSH: run classified shell commands, read and write files over SFTP, inspect system health, and manage services. Supports password and key authentication, a bastion jump host, and host key pinning.",
    "category": "Compute",
    "iconKey": "terminal",
    "version": "1.0.0",
    "documentationUrl": "https://man.openbsd.org/ssh",
    "defaultInstructionsKey": "connector.ssh.instructions",
    "plugin": false,
    "settings": [
      {
        "key": "host",
        "label": "Host",
        "help": "Host name or IP address of the server.",
        "kind": "String",
        "required": true,
        "allowedValues": [],
        "group": "Connection",
        "showInSummary": true
      },
      {
        "key": "port",
        "label": "Port",
        "help": "SSH port. Default 22.",
        "kind": "Integer",
        "required": false,
        "defaultValue": "22",
        "minimum": 1,
        "maximum": 65535,
        "allowedValues": [],
        "group": "Connection",
        "showInSummary": false
      },
      "… 20 more"
    ],
    "secretSlots": [
      {
        "name": "password",
        "label": "Password",
        "help": "Login password (field 'password'). Required when authMethod is Password.",
        "acceptedKinds": [
          "Password"
        ],
        "required": false
      },
      {
        "name": "privateKey",
        "label": "Private key",
        "help": "OpenSSH or PEM private key (field 'privateKey') with an optional 'passphrase'. Required when authMethod is PrivateKey.",
        "acceptedKinds": [
          "PrivateKey"
        ],
…
```

### GET `/v1.0/api/connectors/{type}`

One connector descriptor by type.

**Authentication:** Any REST scheme. Permission: Read on Connector.

| Parameter | In | Type | Description |
|---|---|---|---|
| `type` | path | string | Path parameter. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `type`, `family`, `displayName`, `description`, `category`, `iconKey`, `version`, `documentationUrl`, `defaultInstructionsKey`, `plugin`, `settings`, `secretSlots`, `tools`.

Example:

```bash
curl -s "$HUGO/v1.0/api/connectors/sqlite" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```text
{
  "type": "sqlite",
  "family": "sql",
  "displayName": "SQLite",
  "description": "SQLite database files: schema discovery, row-capped queries, and query plans. Read-only access opens the file with Mode=ReadOnly.",
  "category": "Database",
  "iconKey": "database",
  "version": "1.0.0",
  "documentationUrl": "https://www.sqlite.org/docs.html",
  "defaultInstructionsKey": "connector.sqlite.instructions",
  "plugin": false,
  "settings": [
    {
      "key": "filename",
      "label": "Database file",
      "help": "Path to the SQLite database file as seen by the Hugo server. Required unless a connectionString secret is bound. The file is never created.",
      "kind": "String",
      "required": false,
      "allowedValues": [],
      "group": "Connection",
      "showInSummary": true
    },
    {
      "key": "schema",
      "label": "Default schema",
      "help": "Schema used by catalog tools when none is given: main, or the name of an attached database. Default main.",
      "kind": "String",
      "required": false,
      "allowedValues": [],
      "group": "Connection",
      "showInSummary": false
    },
    "… 5 more"
  ],
  "secretSlots": [
    {
      "name": "connectionString",
      "label": "Connection string",
      "help": "A complete driver connection string. When bound, it overrides the discrete connection settings; Hugo still applies the command timeout and the application name when absent, and, with readOnlyEnforceme… (268 characters)",
      "acceptedKinds": [
        "ConnectionString"
      ],
      "required": false
    }
  ],
  "tools": [
    {
      "name": "sql_list_schemas",
      "title": "List schemas",
      "description": "List the schemas in the database (on MySQL, the databases; on SQLite, main and attached databases) and name the default schema. Start here when you do not know where tables live.",
      "inputSchemaJson": "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}",
      "baseEffect": "Read",
      "classifiedPerCall": false,
      "updatesKnowledge": false,
      "requiresModel": false,
…
```

## Resources

A resource is one configured system: this database, that host, that bucket. Settings are validated against the connector's schema on every write, and credentials are attached by secret slot, so the same AWS key pair can serve two resources without being copied. `defaultAccessModeCeiling` caps what any profile can do with the resource, whoever binds it.

Test before you save with `POST /v1.0/api/resources/test`, which runs the connector's connection test on unsaved settings and stores nothing. A failed connection is a 200 with `success: false` and the reason, because the request itself succeeded. `POST /v1.0/api/resources/{id}/invoke` lets an administrator call a tool on a resource without going through a profile; it still runs through the full invocation pipeline.

| Method | Path | Summary |
|---|---|---|
| GET | `/v1.0/api/resources` | List resources (filter with connectorType) |
| POST | `/v1.0/api/resources` | Create a resource (settings validated against the connector schema; secrets by slot) |
| POST | `/v1.0/api/resources/test` | Validate and test unsaved resource settings (nothing is stored) |
| GET | `/v1.0/api/resources/{id}` | Read a resource |
| PUT | `/v1.0/api/resources/{id}` | Update a resource |
| DELETE | `/v1.0/api/resources/{id}` | Delete a resource with its bindings, settings, secret slots, schema cache, and knowledge |
| POST | `/v1.0/api/resources/{id}/test` | Test a saved resource's connection and record the result |
| POST | `/v1.0/api/resources/{id}/duplicate` | Copy a resource with its settings and secret slots under a new name |
| GET | `/v1.0/api/resources/{id}/referrers` | Profiles and bindings that use a resource |
| POST | `/v1.0/api/resources/{id}/invoke` | Invoke a tool on a resource directly, without a profile (access mode is the resource ceiling or ReadWriteWithApproval); 202 while approval is pending |

### GET `/v1.0/api/resources`

Resources visible to the caller. Filter with `connectorType`; page with `maxResults` and `skip`.

**Authentication:** Any REST scheme. Permission: Read on Resource.

| Parameter | In | Type | Description |
|---|---|---|---|
| `connectorType` | query | string | Only resources of this connector type. |
| `maxResults` | query | integer | 1 to 1000. Default 25. |
| `skip` | query | integer | Records to skip. Default 0. |
| `ordering` | query | enum | CreatedDescending (default), CreatedAscending, NameAscending, NameDescending. |
| `search` | query | string | Text to match. |
| `activeOnly` | query | boolean | Only active records. Default false. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden.

**Response fields:** `success`, `maxResults`, `skip`, `totalRecords`, `recordsRemaining`, `endOfResults`, `timestampUtc`, `objects`, `objects[].id`, `objects[].tenantId`, `objects[].ownerUserId`, `objects[].visibility`, `objects[].name`, `objects[].connectorType`, `objects[].displayName`, `objects[].description`, `objects[].endpoint`, `objects[].environment`, `objects[].notes`, `objects[].defaultAccessModeCeiling`, `objects[].lastTestUtc`, `objects[].lastTestResult`, `objects[].lastTestMessage`, `objects[].active`, `objects[].createdUtc`, `objects[].lastUpdateUtc`, `objects[].settings`, `objects[].secretBindings`.

Example:

```bash
curl -s "$HUGO/v1.0/api/resources?connectorType=sqlite" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "success": true,
  "maxResults": 25,
  "skip": 0,
  "totalRecords": 1,
  "recordsRemaining": 0,
  "endOfResults": true,
  "timestampUtc": "2026-10-15T05:17:47.391855Z",
  "objects": [
    {
      "id": "res_mv937xm6_qFsDalpjfsqe3DAHONT",
      "tenantId": "default",
      "ownerUserId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
      "visibility": "Tenant",
      "name": "demo-db",
      "connectorType": "sqlite",
      "description": "In-memory SQLite database for examples",
      "endpoint": ":memory:",
      "environment": "dev",
      "notes": "Empty on every connection.",
      "active": true,
      "createdUtc": "2026-10-15T05:17:47.118942Z",
      "lastUpdateUtc": "2026-10-15T05:17:47.118942Z",
      "settings": [
        {
          "key": "filename",
          "valueKind": "String",
          "value": ":memory:"
        }
      ],
      "secretBindings": []
    }
  ]
}
```

### POST `/v1.0/api/resources`

Create a resource. `connectorType` picks the connector; `settings` are validated against its schema (see `GET /v1.0/api/connectors/{type}`) and `secretBindings` attach vault secrets to its slots. The example creates `demo-db`, a SQLite resource on `:memory:`, which opens an empty database on every connection and needs no files or network.

**Authentication:** Any REST scheme. Permission: Create on Resource.

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `name` | string | **Required.** Unique within the tenant (on create). |
| `connectorType` | string | **Required.** A connector type from `GET /v1.0/api/connectors` (on create). |
| `displayName` | string | Friendly name. |
| `description` | string | Free text; shown to agents in the compiled instructions. |
| `endpoint` | string | Display endpoint. Derived from the settings when omitted. |
| `environment` | string | Label such as prod or dev; shown to agents. |
| `notes` | string | Operator notes rendered into the resource section of the instructions. |
| `visibility` | enum | Tenant (default) or Private (owner and tenant administrators only). Values: Private, Tenant. |
| `defaultAccessModeCeiling` | enum | Caps the access mode of every binding of this resource. Values: ReadOnly, ReadWriteWithApproval, ReadWrite. |
| `clearAccessModeCeiling` | boolean | true removes the ceiling (update only). |
| `active` | boolean | Default true. |
| `settings` | object | Connector settings by key, validated against the connector schema. Merged by key on update. |
| `secretBindings` | object[] | Secrets by slot. `slotName` and `secretId` (or `secretName`) are required; `secretFieldName` picks a field of a multi-field secret. |
| `secretBindings[].slotName` | string |  |
| `secretBindings[].secretId` | string |  |
| `secretBindings[].secretFieldName` | string |  |
| `secretBindings[].secretName` | string |  |

**Status codes:** 201 Created, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 409 Conflict.

**Response fields:** `id`, `tenantId`, `ownerUserId`, `visibility`, `name`, `connectorType`, `displayName`, `description`, `endpoint`, `environment`, `notes`, `defaultAccessModeCeiling`, `lastTestUtc`, `lastTestResult`, `lastTestMessage`, `active`, `createdUtc`, `lastUpdateUtc`, `settings`, `secretBindings`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/resources" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name": "demo-db", "connectorType": "sqlite", "description": "In-memory SQLite database for examples", "environment": "dev", "notes": "Empty on every connection.", "settings": {"filename": ":memory:"}}'
```

Response (201):

```json
{
  "id": "res_mv937xm6_qFsDalpjfsqe3DAHONT",
  "tenantId": "default",
  "ownerUserId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "visibility": "Tenant",
  "name": "demo-db",
  "connectorType": "sqlite",
  "description": "In-memory SQLite database for examples",
  "endpoint": ":memory:",
  "environment": "dev",
  "notes": "Empty on every connection.",
  "active": true,
  "createdUtc": "2026-10-15T05:17:47.118942Z",
  "lastUpdateUtc": "2026-10-15T05:17:47.118942Z",
  "settings": [
    {
      "key": "filename",
      "valueKind": "String",
      "value": ":memory:"
    }
  ],
  "secretBindings": []
}
```

### POST `/v1.0/api/resources/test`

Validate and test a resource configuration without storing it. Validation problems come back in `validationErrors`; a failed connection is still a 200 with `success: false`. The test is cancelled after `Connectors.TestTimeoutSeconds`.

**Authentication:** Any REST scheme. Permission: Create on Resource.

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `name` | string | **Required.** Unique within the tenant (on create). |
| `connectorType` | string | **Required.** A connector type from `GET /v1.0/api/connectors` (on create). |
| `displayName` | string | Friendly name. |
| `description` | string | Free text; shown to agents in the compiled instructions. |
| `endpoint` | string | Display endpoint. Derived from the settings when omitted. |
| `environment` | string | Label such as prod or dev; shown to agents. |
| `notes` | string | Operator notes rendered into the resource section of the instructions. |
| `visibility` | enum | Tenant (default) or Private (owner and tenant administrators only). Values: Private, Tenant. |
| `defaultAccessModeCeiling` | enum | Caps the access mode of every binding of this resource. Values: ReadOnly, ReadWriteWithApproval, ReadWrite. |
| `clearAccessModeCeiling` | boolean | true removes the ceiling (update only). |
| `active` | boolean | Default true. |
| `settings` | object | Connector settings by key, validated against the connector schema. Merged by key on update. |
| `secretBindings` | object[] | Secrets by slot. `slotName` and `secretId` (or `secretName`) are required; `secretFieldName` picks a field of a multi-field secret. |
| `secretBindings[].slotName` | string |  |
| `secretBindings[].secretId` | string |  |
| `secretBindings[].secretFieldName` | string |  |
| `secretBindings[].secretName` | string |  |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden.

**Response fields:** `success`, `message`, `errorCode`, `latencyMs`, `details`, `validationErrors`, `learnedSettings`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/resources/test" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name": "scratch", "connectorType": "sqlite", "settings": {"filename": ":memory:"}}'
```

Response (200):

```json
{
  "success": true,
  "message": "Connected to SQLite 3.53.3",
  "latencyMs": 0.14,
  "details": {
    "serverVersion": "3.53.3",
    "engine": "sqlite",
    "database": "main",
    "defaultSchema": "main",
    "readOnlyEnforcement": "session"
  },
  "validationErrors": [],
  "learnedSettings": {}
}
```

### GET `/v1.0/api/resources/{id}`

One resource with its settings and secret slot bindings (secret IDs and names, never values).

**Authentication:** Any REST scheme. Permission: Read on Resource.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `ownerUserId`, `visibility`, `name`, `connectorType`, `displayName`, `description`, `endpoint`, `environment`, `notes`, `defaultAccessModeCeiling`, `lastTestUtc`, `lastTestResult`, `lastTestMessage`, `active`, `createdUtc`, `lastUpdateUtc`, `settings`, `secretBindings`.

Example:

```bash
curl -s "$HUGO/v1.0/api/resources/res_mv937xm6_qFsDalpjfsqe3DAHONT" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "id": "res_mv937xm6_qFsDalpjfsqe3DAHONT",
  "tenantId": "default",
  "ownerUserId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "visibility": "Tenant",
  "name": "demo-db",
  "connectorType": "sqlite",
  "description": "In-memory SQLite database for examples",
  "endpoint": ":memory:",
  "environment": "dev",
  "notes": "Empty on every connection.",
  "active": true,
  "createdUtc": "2026-10-15T05:17:47.118942Z",
  "lastUpdateUtc": "2026-10-15T05:17:47.118942Z",
  "settings": [
    {
      "key": "filename",
      "valueKind": "String",
      "value": ":memory:"
    }
  ],
  "secretBindings": []
}
```

### PUT `/v1.0/api/resources/{id}`

Update a resource. Omitted fields keep their values; `settings` is merged by key. `defaultAccessModeCeiling` caps every binding of the resource; `clearAccessModeCeiling: true` removes it.

**Authentication:** Any REST scheme. Permission: Update on Resource.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `name` | string | **Required.** Unique within the tenant (on create). |
| `connectorType` | string | **Required.** A connector type from `GET /v1.0/api/connectors` (on create). |
| `displayName` | string | Friendly name. |
| `description` | string | Free text; shown to agents in the compiled instructions. |
| `endpoint` | string | Display endpoint. Derived from the settings when omitted. |
| `environment` | string | Label such as prod or dev; shown to agents. |
| `notes` | string | Operator notes rendered into the resource section of the instructions. |
| `visibility` | enum | Tenant (default) or Private (owner and tenant administrators only). Values: Private, Tenant. |
| `defaultAccessModeCeiling` | enum | Caps the access mode of every binding of this resource. Values: ReadOnly, ReadWriteWithApproval, ReadWrite. |
| `clearAccessModeCeiling` | boolean | true removes the ceiling (update only). |
| `active` | boolean | Default true. |
| `settings` | object | Connector settings by key, validated against the connector schema. Merged by key on update. |
| `secretBindings` | object[] | Secrets by slot. `slotName` and `secretId` (or `secretName`) are required; `secretFieldName` picks a field of a multi-field secret. |
| `secretBindings[].slotName` | string |  |
| `secretBindings[].secretId` | string |  |
| `secretBindings[].secretFieldName` | string |  |
| `secretBindings[].secretName` | string |  |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `ownerUserId`, `visibility`, `name`, `connectorType`, `displayName`, `description`, `endpoint`, `environment`, `notes`, `defaultAccessModeCeiling`, `lastTestUtc`, `lastTestResult`, `lastTestMessage`, `active`, `createdUtc`, `lastUpdateUtc`, `settings`, `secretBindings`.

Example:

```bash
curl -s -X PUT "$HUGO/v1.0/api/resources/res_mv937xm6_qFsDalpjfsqe3DAHONT" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"notes": "Empty on every connection. Use it for examples and smoke tests.", "defaultAccessModeCeiling": "ReadWriteWithApproval"}'
```

Response (200):

```json
{
  "id": "res_mv937xm6_qFsDalpjfsqe3DAHONT",
  "tenantId": "default",
  "ownerUserId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "visibility": "Tenant",
  "name": "demo-db",
  "connectorType": "sqlite",
  "description": "In-memory SQLite database for examples",
  "endpoint": ":memory:",
  "environment": "dev",
  "notes": "Empty on every connection. Use it for examples and smoke tests.",
  "defaultAccessModeCeiling": "ReadWriteWithApproval",
  "active": true,
  "createdUtc": "2026-10-15T05:17:47.118942Z",
  "lastUpdateUtc": "2026-10-15T05:17:47.418511Z",
  "settings": [
    {
      "key": "filename",
      "valueKind": "String",
      "value": ":memory:"
    }
  ],
  "secretBindings": []
}
```

### DELETE `/v1.0/api/resources/{id}`

Delete a resource with its bindings, settings, secret slots, schema cache, and knowledge.

**Authentication:** Any REST scheme. Permission: Delete on Resource.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Status codes:** 204 No Content, 401 Not authenticated, 403 Forbidden, 404 Not found.

Example:

```bash
curl -s -X DELETE "$HUGO/v1.0/api/resources/res_mv937xm6_qFsDalpjfsqe3DAHONT" \
  -H "Authorization: Bearer $TOKEN"
```

Response: 204 with an empty body.

### POST `/v1.0/api/resources/{id}/test`

Test a saved resource's connection and record the result in `lastTestUtc`, `lastTestResult`, and `lastTestMessage`. SSH resources pin the host key on first success (trust on first use).

**Authentication:** Any REST scheme. Permission: Read on Resource.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `success`, `message`, `errorCode`, `latencyMs`, `details`, `validationErrors`, `learnedSettings`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/resources/res_mv937xm6_qFsDalpjfsqe3DAHONT/test" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "success": true,
  "message": "Connected to SQLite 3.53.3",
  "latencyMs": 0.07,
  "details": {
    "serverVersion": "3.53.3",
    "engine": "sqlite",
    "database": "main",
    "defaultSchema": "main",
    "readOnlyEnforcement": "session"
  },
  "validationErrors": [],
  "learnedSettings": {}
}
```

### POST `/v1.0/api/resources/{id}/duplicate`

Copy a resource with its settings and secret slot bindings under a new name.

**Authentication:** Any REST scheme. Permission: Create on Resource.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `name` | string | **Required.** Name of the copy. |

**Status codes:** 201 Created, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found, 409 Conflict.

**Response fields:** `id`, `tenantId`, `ownerUserId`, `visibility`, `name`, `connectorType`, `displayName`, `description`, `endpoint`, `environment`, `notes`, `defaultAccessModeCeiling`, `lastTestUtc`, `lastTestResult`, `lastTestMessage`, `active`, `createdUtc`, `lastUpdateUtc`, `settings`, `secretBindings`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/resources/res_mv937xm6_qFsDalpjfsqe3DAHONT/duplicate" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name": "demo-db-copy"}'
```

Response (201):

```json
{
  "id": "res_mv937xv8_VyX9BrRrStQyt7AjzFG",
  "tenantId": "default",
  "ownerUserId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "visibility": "Tenant",
  "name": "demo-db-copy",
  "connectorType": "sqlite",
  "description": "In-memory SQLite database for examples",
  "endpoint": ":memory:",
  "environment": "dev",
  "notes": "Empty on every connection. Use it for examples and smoke tests.",
  "defaultAccessModeCeiling": "ReadWriteWithApproval",
  "active": true,
  "createdUtc": "2026-10-15T05:17:47.44436Z",
  "lastUpdateUtc": "2026-10-15T05:17:47.44436Z",
  "settings": [
    {
      "key": "filename",
      "valueKind": "String",
      "value": ":memory:"
    }
  ],
  "secretBindings": []
}
```

### GET `/v1.0/api/resources/{id}/referrers`

Profiles and bindings that use a resource. Check it before deleting or tightening a resource.

**Authentication:** Any REST scheme. Permission: Read on Resource.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `[]agentProfileId`, `[]agentProfileName`, `[]bindingId`, `[]alias`, `[]accessMode`, `[]enabled`.

Example:

```bash
curl -s "$HUGO/v1.0/api/resources/res_mv937xm6_qFsDalpjfsqe3DAHONT/referrers" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
[
  {
    "agentProfileId": "agp_mv92uyqb_LM9EdE7i2KwfKe6GfaO",
    "agentProfileName": "Hugo",
    "bindingId": "bnd_mv937xo7_7a5SmrbpdWse5AY9Bzo",
    "alias": "demo-db",
    "accessMode": "ReadOnly",
    "enabled": true
  },
  {
    "agentProfileId": "agp_mv937xml_XArgAHQfG5v6uUkrkbl",
    "agentProfileName": "Oncall",
    "bindingId": "bnd_mv937xok_kfPjNftrNgz1vLdpehn",
    "alias": "demo-db",
    "accessMode": "ReadWriteWithApproval",
    "enabled": true
  }
]
```

### POST `/v1.0/api/resources/{id}/invoke`

Administrator direct invoke without a profile. Needs Execute on Resource; the access mode is the resource ceiling (or ReadWriteWithApproval when none is set). Goes through the same pipeline as every other surface: classification, approvals, limits, scrubbing, and auditing.

**Authentication:** Any REST scheme. Permission: Execute on Resource.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `profileId` | string | Profile to act as, by ID. |
| `profile` | string | Profile to act as, by name. With neither, the caller's default profile applies (D-018). Ignored by `/resources/{id}/invoke`. |
| `resource` | string | Binding alias. May also be passed as `arguments.resource`. |
| `resourceId` | string | Resource ID instead of the alias. |
| `toolName` | string | **Required.** Tool name, for example `sql_query`. |
| `arguments` | object | Tool arguments as a JSON object, validated against the tool input schema. Any JSON value. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `invocationId`, `dryRun`, `toolName`, `profileId`, `bindingAlias`, `resourceId`, `connectorType`, `decision`, `decisionCode`, `reason`, `effect`, `effectReason`, `accessMode`, `approvalId`, `approvalStatus`, `approvalSummary`, `approvalExpiresUtc`, `status`, `result`, `durationMs`, `approvalWaitMs`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/resources/res_mv937xm6_qFsDalpjfsqe3DAHONT/invoke" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"toolName": "sql_query", "arguments": {"sql": "SELECT '\''red pill'\'' AS choice"}}'
```

Response (200):

```json
{
  "invocationId": "tiv_mv937xvk_hbd6F3Ck6ghvBqYeNoa",
  "dryRun": false,
  "toolName": "sql_query",
  "bindingAlias": "demo-db",
  "resourceId": "res_mv937xm6_qFsDalpjfsqe3DAHONT",
  "connectorType": "sqlite",
  "decision": "Allowed",
  "decisionCode": "allowed",
  "reason": "Read call.",
  "effect": "Read",
  "effectReason": "statement type SELECT",
  "accessMode": "ReadWriteWithApproval",
  "approvalSummary": "Run SQL on demo-db: SELECT 'red pill' AS choice",
  "status": "Succeeded",
  "result": {
    "isError": false,
    "text": "1 row in 0.03 ms.\n| choice |\n| --- |\n| red pill |\n",
    "structuredJson": "{\"statementType\":\"SELECT\",\"columns\":[{\"name\":\"choice\",\"type\":\"TEXT\"}],\"rows\":[[\"red pill\"]],\"rowCount\":1,\"rowLimit\":500,\"truncated\":false,\"elapsedMs\":0.03}",
    "mimeType": "text/plain",
    "truncated": false,
    "originalBytes": 50,
    "metrics": {
      "rows": 1,
      "upstreamMs": 0.1
    }
  },
  "durationMs": 0,
  "approvalWaitMs": 0
}
```

## Resource knowledge

Knowledge is durable, tenant-scoped text attached to a resource, organized as scopes and keys. The SQL connectors keep the database context in the `database` scope and table context in `tables`; other connectors may keep host or bucket notes. The root entry of a scope (key `_root`, or the scope route itself) is what the instruction compiler includes in the resource's section, bounded by `Instructions.MaxKnowledgeCharsPerResource`. Every write is scrubbed for secrets, versioned, and audited, and operators, models, and agents are recorded as distinct sources.

| Method | Path | Summary |
|---|---|---|
| GET | `/v1.0/api/resources/{id}/knowledge` | List a resource's knowledge entries |
| GET | `/v1.0/api/resources/{id}/knowledge/{scope}` | List the knowledge entries of one scope |
| PUT | `/v1.0/api/resources/{id}/knowledge/{scope}` | Write a scope's root knowledge entry (operator edit; scrubbed and audited) |
| DELETE | `/v1.0/api/resources/{id}/knowledge/{scope}` | Delete a scope's root knowledge entry |
| GET | `/v1.0/api/resources/{id}/knowledge/{scope}/{key}` | Read one knowledge entry (key _root for the scope root) |
| PUT | `/v1.0/api/resources/{id}/knowledge/{scope}/{key}` | Write one knowledge entry (operator edit; scrubbed and audited) |
| DELETE | `/v1.0/api/resources/{id}/knowledge/{scope}/{key}` | Delete one knowledge entry |
| GET | `/v1.0/api/resources/{id}/knowledge/{scope}/{key}/versions` | Version history of one knowledge entry, newest first |

### GET `/v1.0/api/resources/{id}/knowledge`

Every knowledge entry of a resource.

**Authentication:** Any REST scheme. Permission: Read on Knowledge.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `[]id`, `[]tenantId`, `[]resourceId`, `[]scope`, `[]key`, `[]content`, `[]source`, `[]updatedBy`, `[]version`, `[]createdUtc`, `[]lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/resources/res_mv937xm6_qFsDalpjfsqe3DAHONT/knowledge" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
[
  {
    "id": "rkn_mv937xvx_gPkTb0ppkYF89DGfbsF",
    "tenantId": "default",
    "resourceId": "res_mv937xm6_qFsDalpjfsqe3DAHONT",
    "scope": "database",
    "key": "",
    "content": "demo-db is an in-memory SQLite database used for examples. It holds no business data.",
    "source": "Operator",
    "updatedBy": "anderson@hugo.local",
    "version": 1,
    "createdUtc": "2026-10-15T05:17:47.469923Z",
    "lastUpdateUtc": "2026-10-15T05:17:47.469923Z"
  },
  {
    "id": "rkn_mv937xwb_cI9M7R8irgHc2YPncWu",
    "tenantId": "default",
    "resourceId": "res_mv937xm6_qFsDalpjfsqe3DAHONT",
    "scope": "tables",
    "key": "main.orders",
    "content": "orders: one row per checkout. status is pending, paid, or refunded.",
    "source": "Operator",
    "updatedBy": "anderson@hugo.local",
    "version": 1,
    "createdUtc": "2026-10-15T05:17:47.483522Z",
    "lastUpdateUtc": "2026-10-15T05:17:47.483522Z"
  }
]
```

### GET `/v1.0/api/resources/{id}/knowledge/{scope}`

Entries of one scope.

**Authentication:** Any REST scheme. Permission: Read on Knowledge.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |
| `scope` | path | string | Path parameter. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `[]id`, `[]tenantId`, `[]resourceId`, `[]scope`, `[]key`, `[]content`, `[]source`, `[]updatedBy`, `[]version`, `[]createdUtc`, `[]lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/resources/res_mv937xm6_qFsDalpjfsqe3DAHONT/knowledge/tables" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
[
  {
    "id": "rkn_mv937xwb_cI9M7R8irgHc2YPncWu",
    "tenantId": "default",
    "resourceId": "res_mv937xm6_qFsDalpjfsqe3DAHONT",
    "scope": "tables",
    "key": "main.orders",
    "content": "orders: one row per checkout. status is pending, paid, or refunded.",
    "source": "Operator",
    "updatedBy": "anderson@hugo.local",
    "version": 1,
    "createdUtc": "2026-10-15T05:17:47.483522Z",
    "lastUpdateUtc": "2026-10-15T05:17:47.483522Z"
  }
]
```

### PUT `/v1.0/api/resources/{id}/knowledge/{scope}`

Write the root entry of a knowledge scope (operator edit). Knowledge is durable context the instruction compiler and the SQL tools read; the root entry of the `database` scope is the database context summary. Content is scrubbed for secrets and the change is audited.

**Authentication:** Any REST scheme. Permission: Update on Knowledge.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |
| `scope` | path | string | Path parameter. |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `content` | string | **Required.** Entry text. Scrubbed for secrets before it is stored. |
| `source` | enum | Operator (default for REST), Generated, Agent, or Derived. Values: Operator, Generated, Agent, Derived. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `resourceId`, `scope`, `key`, `content`, `source`, `updatedBy`, `version`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s -X PUT "$HUGO/v1.0/api/resources/res_mv937xm6_qFsDalpjfsqe3DAHONT/knowledge/database" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"content": "demo-db is an in-memory SQLite database used for examples. It holds no business data.", "source": "Operator"}'
```

Response (200):

```json
{
  "id": "rkn_mv937xvx_gPkTb0ppkYF89DGfbsF",
  "tenantId": "default",
  "resourceId": "res_mv937xm6_qFsDalpjfsqe3DAHONT",
  "scope": "database",
  "key": "",
  "content": "demo-db is an in-memory SQLite database used for examples. It holds no business data.",
  "source": "Operator",
  "updatedBy": "anderson@hugo.local",
  "version": 1,
  "createdUtc": "2026-10-15T05:17:47.469923Z",
  "lastUpdateUtc": "2026-10-15T05:17:47.469923Z"
}
```

### DELETE `/v1.0/api/resources/{id}/knowledge/{scope}`

Delete the root entry of a scope.

**Authentication:** Any REST scheme. Permission: Delete on Knowledge.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |
| `scope` | path | string | Path parameter. |

**Status codes:** 204 No Content, 401 Not authenticated, 403 Forbidden, 404 Not found.

Example:

```bash
curl -s -X DELETE "$HUGO/v1.0/api/resources/res_mv937xm6_qFsDalpjfsqe3DAHONT/knowledge/database" \
  -H "Authorization: Bearer $TOKEN"
```

Response: 204 with an empty body.

### GET `/v1.0/api/resources/{id}/knowledge/{scope}/{key}`

One entry. Use the key `_root` to read a scope's root entry.

**Authentication:** Any REST scheme. Permission: Read on Knowledge.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |
| `scope` | path | string | Path parameter. |
| `key` | path | string | Path parameter. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `resourceId`, `scope`, `key`, `content`, `source`, `updatedBy`, `version`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/resources/res_mv937xm6_qFsDalpjfsqe3DAHONT/knowledge/tables/main.orders" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "id": "rkn_mv937xwb_cI9M7R8irgHc2YPncWu",
  "tenantId": "default",
  "resourceId": "res_mv937xm6_qFsDalpjfsqe3DAHONT",
  "scope": "tables",
  "key": "main.orders",
  "content": "orders: one row per checkout. status is pending, paid, or refunded.",
  "source": "Operator",
  "updatedBy": "anderson@hugo.local",
  "version": 1,
  "createdUtc": "2026-10-15T05:17:47.483522Z",
  "lastUpdateUtc": "2026-10-15T05:17:47.483522Z"
}
```

### PUT `/v1.0/api/resources/{id}/knowledge/{scope}/{key}`

Every write adds a version.

**Authentication:** Any REST scheme. Permission: Update on Knowledge.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |
| `scope` | path | string | Path parameter. |
| `key` | path | string | Path parameter. |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `content` | string | **Required.** Entry text. Scrubbed for secrets before it is stored. |
| `source` | enum | Operator (default for REST), Generated, Agent, or Derived. Values: Operator, Generated, Agent, Derived. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `resourceId`, `scope`, `key`, `content`, `source`, `updatedBy`, `version`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s -X PUT "$HUGO/v1.0/api/resources/res_mv937xm6_qFsDalpjfsqe3DAHONT/knowledge/tables/main.orders" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"content": "orders: one row per checkout. status is pending, paid, or refunded.", "source": "Operator"}'
```

Response (200):

```json
{
  "id": "rkn_mv937xwb_cI9M7R8irgHc2YPncWu",
  "tenantId": "default",
  "resourceId": "res_mv937xm6_qFsDalpjfsqe3DAHONT",
  "scope": "tables",
  "key": "main.orders",
  "content": "orders: one row per checkout. status is pending, paid, or refunded.",
  "source": "Operator",
  "updatedBy": "anderson@hugo.local",
  "version": 1,
  "createdUtc": "2026-10-15T05:17:47.483522Z",
  "lastUpdateUtc": "2026-10-15T05:17:47.483522Z"
}
```

### DELETE `/v1.0/api/resources/{id}/knowledge/{scope}/{key}`

Delete one entry. Its history is kept.

**Authentication:** Any REST scheme. Permission: Delete on Knowledge.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |
| `scope` | path | string | Path parameter. |
| `key` | path | string | Path parameter. |

**Status codes:** 204 No Content, 401 Not authenticated, 403 Forbidden, 404 Not found.

Example:

```bash
curl -s -X DELETE "$HUGO/v1.0/api/resources/res_mv937xm6_qFsDalpjfsqe3DAHONT/knowledge/tables/main.orders" \
  -H "Authorization: Bearer $TOKEN"
```

Response: 204 with an empty body.

### GET `/v1.0/api/resources/{id}/knowledge/{scope}/{key}/versions`

Version history of one entry, newest first, including deletions.

**Authentication:** Any REST scheme. Permission: Read on Knowledge.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |
| `scope` | path | string | Path parameter. |
| `key` | path | string | Path parameter. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `[]id`, `[]tenantId`, `[]knowledgeId`, `[]resourceId`, `[]scope`, `[]key`, `[]version`, `[]content`, `[]source`, `[]updatedBy`, `[]deleted`, `[]createdUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/resources/res_mv937xm6_qFsDalpjfsqe3DAHONT/knowledge/tables/main.orders/versions" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
[
  {
    "id": "rkv_mv937xwb_0LfqjsUqu6q6SnsumeQ",
    "tenantId": "default",
    "knowledgeId": "rkn_mv937xwb_cI9M7R8irgHc2YPncWu",
    "resourceId": "res_mv937xm6_qFsDalpjfsqe3DAHONT",
    "scope": "tables",
    "key": "main.orders",
    "version": 1,
    "content": "orders: one row per checkout. status is pending, paid, or refunded.",
    "source": "Operator",
    "updatedBy": "anderson@hugo.local",
    "deleted": false,
    "createdUtc": "2026-10-15T05:17:47.483554Z"
  }
]
```

## Agent profiles

An agent profile is what a caller talks to. It holds the persona (name, how it addresses you, persona template), the model endpoints Hugo's own loop uses, loop limits, exposure flags for MCP, A2A, and the OpenAI-compatible surface, and whether management tools are offered over MCP. Deleting a profile removes its bindings but never the resources behind them.

Three read routes show a profile the way its callers will see it. `compiled-instructions` returns the exact text sent on a surface with a source map back to the template layers. `tools` returns the tool list after visibility filtering. `card` is the A2A agent card. When an agent behaves unexpectedly, look at these before you look at the model. `POST /v1.0/api/agent-profiles/{id}/ask` runs a whole task on the profile's own loop and waits for the answer; it is the REST twin of the MCP tool `ask_agent_smith`.

| Method | Path | Summary |
|---|---|---|
| GET | `/v1.0/api/agent-profiles` | List agent profiles |
| POST | `/v1.0/api/agent-profiles` | Create an agent profile |
| GET | `/v1.0/api/agent-profiles/{id}` | Read an agent profile |
| PUT | `/v1.0/api/agent-profiles/{id}` | Update an agent profile |
| DELETE | `/v1.0/api/agent-profiles/{id}` | Delete an agent profile and its bindings (never its resources) |
| POST | `/v1.0/api/agent-profiles/{id}/duplicate` | Copy an agent profile with its bindings |
| GET | `/v1.0/api/agent-profiles/{id}/compiled-instructions` | Compiled instructions as the caller would receive them (surface=Chat/Mcp/A2A/OpenAI), with source map and token estimate |
| GET | `/v1.0/api/agent-profiles/{id}/tools` | Tools as the caller would see them, with the resource enum per tool |
| GET | `/v1.0/api/agent-profiles/{id}/card` | Preview of the A2A agent card |
| POST | `/v1.0/api/agent-profiles/{id}/ask` | Delegate a task to the profile's own agent loop and wait for the answer (approvals wait up to Approvals.McpWaitSeconds, then the result reports ApprovalPending) |

### GET `/v1.0/api/agent-profiles`

Profiles visible to the caller. Private profiles appear only to their owner and tenant administrators.

**Authentication:** Any REST scheme. Permission: Read on AgentProfile.

| Parameter | In | Type | Description |
|---|---|---|---|
| `maxResults` | query | integer | 1 to 1000. Default 25. |
| `skip` | query | integer | Records to skip. Default 0. |
| `ordering` | query | enum | CreatedDescending (default), CreatedAscending, NameAscending, NameDescending. |
| `search` | query | string | Text to match. |
| `activeOnly` | query | boolean | Only active records. Default false. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden.

**Response fields:** `success`, `maxResults`, `skip`, `totalRecords`, `recordsRemaining`, `endOfResults`, `timestampUtc`, `objects`, `objects[].id`, `objects[].tenantId`, `objects[].ownerUserId`, `objects[].visibility`, `objects[].name`, `objects[].displayName`, `objects[].personaName`, `objects[].userAddress`, `objects[].description`, `objects[].personaTemplateKey`, `objects[].inferenceEndpointId`, `objects[].fallbackInferenceEndpointId`, `objects[].embeddingEndpointId`, `objects[].maxIterations`, `objects[].maxToolCallsPerTurn`, `objects[].toolTimeoutSeconds`, `objects[].temperature`, `objects[].exposeA2A`, `objects[].exposeMcp`, `objects[].exposeOpenAI`, `objects[].exposeManagementTools`, `objects[].instructionsAppend`, `objects[].instructionDetail`, `objects[].isDefault`, `objects[].active`, `objects[].createdUtc`, `objects[].lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/agent-profiles" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "success": true,
  "maxResults": 25,
  "skip": 0,
  "totalRecords": 1,
  "recordsRemaining": 0,
  "endOfResults": true,
  "timestampUtc": "2026-10-15T05:17:47.105387Z",
  "objects": [
    {
      "id": "agp_mv92uyqb_LM9EdE7i2KwfKe6GfaO",
      "tenantId": "default",
      "visibility": "Tenant",
      "name": "Hugo",
      "displayName": "Hugo",
      "personaName": "Agent Smith",
      "userAddress": "Mr. Anderson",
      "description": "Default operations agent. Attach resources to give Agent Smith something to work with.",
      "personaTemplateKey": "persona.agent-smith",
      "maxIterations": 12,
      "maxToolCallsPerTurn": 24,
      "toolTimeoutSeconds": 120,
      "exposeA2A": true,
      "exposeMcp": true,
      "exposeOpenAI": true,
      "exposeManagementTools": false,
      "instructionDetail": "Auto",
      "isDefault": true,
      "active": true,
      "createdUtc": "2026-10-15T05:07:42.035721Z",
      "lastUpdateUtc": "2026-10-15T05:12:07.65386Z"
    }
  ]
}
```

### POST `/v1.0/api/agent-profiles`

Create a profile. Unset fields take the defaults (persona template `persona.agent-smith`, 12 iterations, 24 tool calls per turn, 120 second tool timeout, MCP, A2A, and OpenAI exposure on, management tools off, `instructionDetail` Auto).

**Authentication:** Any REST scheme. Permission: Create on AgentProfile.

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `name` | string | **Required.** Unique within the tenant (on create). Used in `/mcp/{profileName}`, A2A URLs, and as the OpenAI model name. |
| `displayName` | string | Friendly name (A2A card name). |
| `personaName` | string | What the agent calls itself. Default Agent Smith. |
| `userAddress` | string | How it addresses the operator. Default Mr. Anderson. |
| `description` | string | Shown in the A2A card. |
| `personaTemplateKey` | string | Prompt template key of the persona. Default `persona.agent-smith`. |
| `inferenceEndpointId` | string | Model endpoint for Hugo's own loop. Empty string clears it. |
| `fallbackInferenceEndpointId` | string | Used when the primary fails. |
| `embeddingEndpointId` | string | Embedding endpoint for tool retrieval. |
| `maxIterations` | integer | Agent loop iterations per turn. Default 12. |
| `maxToolCallsPerTurn` | integer | Default 24. |
| `toolTimeoutSeconds` | integer | Per-tool timeout. Default 120. |
| `temperature` | number | Model temperature, when set. |
| `exposeA2A` | boolean | Publish over A2A. Default true. |
| `exposeMcp` | boolean | Serve over MCP. Default true. |
| `exposeOpenAI` | boolean | Serve over the OpenAI-compatible surface. Default true. |
| `exposeManagementTools` | boolean | Offer the `hugo_admin_*` MCP tools to Admin-capable callers. Default false. |
| `instructionDetail` | enum | Auto (default: compact connector text above `Instructions.CompactBindingThreshold` bindings), Full, or Compact. Values: Auto, Full, Compact. |
| `instructionsAppend` | string | Text appended to the compiled instructions. |
| `isDefault` | boolean | Make this the tenant default profile. |
| `visibility` | enum | Tenant (default) or Private. Values: Private, Tenant. |
| `active` | boolean | Default true. |

**Status codes:** 201 Created, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 409 Conflict.

**Response fields:** `id`, `tenantId`, `ownerUserId`, `visibility`, `name`, `displayName`, `personaName`, `userAddress`, `description`, `personaTemplateKey`, `inferenceEndpointId`, `fallbackInferenceEndpointId`, `embeddingEndpointId`, `maxIterations`, `maxToolCallsPerTurn`, `toolTimeoutSeconds`, `temperature`, `exposeA2A`, `exposeMcp`, `exposeOpenAI`, `exposeManagementTools`, `instructionsAppend`, `instructionDetail`, `isDefault`, `active`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/agent-profiles" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name": "Oncall", "displayName": "On-call", "personaName": "Agent Smith", "userAddress": "Mr. Anderson", "description": "Night shift profile: writes wait for a human.", "instructionDetail": "Auto", "exposeMcp": true, "exposeA2A": true, "exposeOpenAI": true}'
```

Response (201):

```json
{
  "id": "agp_mv937xml_XArgAHQfG5v6uUkrkbl",
  "tenantId": "default",
  "ownerUserId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "visibility": "Tenant",
  "name": "Oncall",
  "displayName": "On-call",
  "personaName": "Agent Smith",
  "userAddress": "Mr. Anderson",
  "description": "Night shift profile: writes wait for a human.",
  "personaTemplateKey": "persona.agent-smith",
  "maxIterations": 12,
  "maxToolCallsPerTurn": 24,
  "toolTimeoutSeconds": 120,
  "exposeA2A": true,
  "exposeMcp": true,
  "exposeOpenAI": true,
  "exposeManagementTools": false,
  "instructionDetail": "Auto",
  "isDefault": false,
  "active": true,
  "createdUtc": "2026-10-15T05:17:47.13367Z",
  "lastUpdateUtc": "2026-10-15T05:17:47.13367Z"
}
```

### GET `/v1.0/api/agent-profiles/{id}`

One profile with its settings (bindings are listed separately).

**Authentication:** Any REST scheme. Permission: Read on AgentProfile.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `ownerUserId`, `visibility`, `name`, `displayName`, `personaName`, `userAddress`, `description`, `personaTemplateKey`, `inferenceEndpointId`, `fallbackInferenceEndpointId`, `embeddingEndpointId`, `maxIterations`, `maxToolCallsPerTurn`, `toolTimeoutSeconds`, `temperature`, `exposeA2A`, `exposeMcp`, `exposeOpenAI`, `exposeManagementTools`, `instructionsAppend`, `instructionDetail`, `isDefault`, `active`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/agent-profiles/agp_mv92uyqb_LM9EdE7i2KwfKe6GfaO" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "id": "agp_mv92uyqb_LM9EdE7i2KwfKe6GfaO",
  "tenantId": "default",
  "visibility": "Tenant",
  "name": "Hugo",
  "displayName": "Hugo",
  "personaName": "Agent Smith",
  "userAddress": "Mr. Anderson",
  "description": "Default operations agent. Attach resources to give Agent Smith something to work with.",
  "personaTemplateKey": "persona.agent-smith",
  "maxIterations": 12,
  "maxToolCallsPerTurn": 24,
  "toolTimeoutSeconds": 120,
  "exposeA2A": true,
  "exposeMcp": true,
  "exposeOpenAI": true,
  "exposeManagementTools": false,
  "instructionDetail": "Auto",
  "isDefault": true,
  "active": true,
  "createdUtc": "2026-10-15T05:07:42.035721Z",
  "lastUpdateUtc": "2026-10-15T05:12:07.65386Z"
}
```

### PUT `/v1.0/api/agent-profiles/{id}`

Partial update: omitted fields keep their values.

**Authentication:** Any REST scheme. Permission: Update on AgentProfile.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `name` | string | **Required.** Unique within the tenant (on create). Used in `/mcp/{profileName}`, A2A URLs, and as the OpenAI model name. |
| `displayName` | string | Friendly name (A2A card name). |
| `personaName` | string | What the agent calls itself. Default Agent Smith. |
| `userAddress` | string | How it addresses the operator. Default Mr. Anderson. |
| `description` | string | Shown in the A2A card. |
| `personaTemplateKey` | string | Prompt template key of the persona. Default `persona.agent-smith`. |
| `inferenceEndpointId` | string | Model endpoint for Hugo's own loop. Empty string clears it. |
| `fallbackInferenceEndpointId` | string | Used when the primary fails. |
| `embeddingEndpointId` | string | Embedding endpoint for tool retrieval. |
| `maxIterations` | integer | Agent loop iterations per turn. Default 12. |
| `maxToolCallsPerTurn` | integer | Default 24. |
| `toolTimeoutSeconds` | integer | Per-tool timeout. Default 120. |
| `temperature` | number | Model temperature, when set. |
| `exposeA2A` | boolean | Publish over A2A. Default true. |
| `exposeMcp` | boolean | Serve over MCP. Default true. |
| `exposeOpenAI` | boolean | Serve over the OpenAI-compatible surface. Default true. |
| `exposeManagementTools` | boolean | Offer the `hugo_admin_*` MCP tools to Admin-capable callers. Default false. |
| `instructionDetail` | enum | Auto (default: compact connector text above `Instructions.CompactBindingThreshold` bindings), Full, or Compact. Values: Auto, Full, Compact. |
| `instructionsAppend` | string | Text appended to the compiled instructions. |
| `isDefault` | boolean | Make this the tenant default profile. |
| `visibility` | enum | Tenant (default) or Private. Values: Private, Tenant. |
| `active` | boolean | Default true. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `ownerUserId`, `visibility`, `name`, `displayName`, `personaName`, `userAddress`, `description`, `personaTemplateKey`, `inferenceEndpointId`, `fallbackInferenceEndpointId`, `embeddingEndpointId`, `maxIterations`, `maxToolCallsPerTurn`, `toolTimeoutSeconds`, `temperature`, `exposeA2A`, `exposeMcp`, `exposeOpenAI`, `exposeManagementTools`, `instructionsAppend`, `instructionDetail`, `isDefault`, `active`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s -X PUT "$HUGO/v1.0/api/agent-profiles/agp_mv937xml_XArgAHQfG5v6uUkrkbl" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"description": "Night shift profile: every write waits for a human.", "maxIterations": 10}'
```

Response (200):

```json
{
  "id": "agp_mv937xml_XArgAHQfG5v6uUkrkbl",
  "tenantId": "default",
  "ownerUserId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "visibility": "Tenant",
  "name": "Oncall",
  "displayName": "On-call",
  "personaName": "Agent Smith",
  "userAddress": "Mr. Anderson",
  "description": "Night shift profile: every write waits for a human.",
  "personaTemplateKey": "persona.agent-smith",
  "maxIterations": 10,
  "maxToolCallsPerTurn": 24,
  "toolTimeoutSeconds": 120,
  "exposeA2A": true,
  "exposeMcp": true,
  "exposeOpenAI": true,
  "exposeManagementTools": false,
  "instructionDetail": "Auto",
  "isDefault": false,
  "active": true,
  "createdUtc": "2026-10-15T05:17:47.13367Z",
  "lastUpdateUtc": "2026-10-15T05:17:47.164828Z"
}
```

### DELETE `/v1.0/api/agent-profiles/{id}`

Delete `Oncall` and its bindings.

**Authentication:** Any REST scheme. Permission: Delete on AgentProfile.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Status codes:** 204 No Content, 401 Not authenticated, 403 Forbidden, 404 Not found.

Example:

```bash
curl -s -X DELETE "$HUGO/v1.0/api/agent-profiles/agp_mv937xml_XArgAHQfG5v6uUkrkbl" \
  -H "Authorization: Bearer $TOKEN"
```

Response: 204 with an empty body.

### POST `/v1.0/api/agent-profiles/{id}/duplicate`

Copy a profile and its bindings under a new name. Resources are shared, not copied.

**Authentication:** Any REST scheme. Permission: Create on AgentProfile.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `name` | string | **Required.** Name of the copy. |

**Status codes:** 201 Created, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found, 409 Conflict.

**Response fields:** `id`, `tenantId`, `ownerUserId`, `visibility`, `name`, `displayName`, `personaName`, `userAddress`, `description`, `personaTemplateKey`, `inferenceEndpointId`, `fallbackInferenceEndpointId`, `embeddingEndpointId`, `maxIterations`, `maxToolCallsPerTurn`, `toolTimeoutSeconds`, `temperature`, `exposeA2A`, `exposeMcp`, `exposeOpenAI`, `exposeManagementTools`, `instructionsAppend`, `instructionDetail`, `isDefault`, `active`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/agent-profiles/agp_mv937xml_XArgAHQfG5v6uUkrkbl/duplicate" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name": "Oncall-Copy"}'
```

Response (201):

```json
{
  "id": "agp_mv937xnu_SsPELmxkfNN753SNBx2",
  "tenantId": "default",
  "ownerUserId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "visibility": "Tenant",
  "name": "Oncall-Copy",
  "displayName": "Oncall-Copy",
  "personaName": "Agent Smith",
  "userAddress": "Mr. Anderson",
  "description": "Night shift profile: every write waits for a human.",
  "personaTemplateKey": "persona.agent-smith",
  "maxIterations": 10,
  "maxToolCallsPerTurn": 24,
  "toolTimeoutSeconds": 120,
  "exposeA2A": true,
  "exposeMcp": true,
  "exposeOpenAI": true,
  "exposeManagementTools": false,
  "instructionDetail": "Auto",
  "isDefault": false,
  "active": true,
  "createdUtc": "2026-10-15T05:17:47.178284Z",
  "lastUpdateUtc": "2026-10-15T05:17:47.178284Z"
}
```

### GET `/v1.0/api/agent-profiles/{id}/compiled-instructions`

The exact text a caller receives on a surface (`Chat`, `Mcp`, `A2A`, `OpenAI`), with a source map of which template layer and version produced each segment and a token estimate. `text=true` returns plain text instead of JSON.

**Authentication:** Any REST scheme. Permission: Read on AgentProfile.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |
| `surface` | query | enum | Chat (default), Mcp, A2A, or OpenAI. |
| `text` | query | boolean | true returns the text as text/plain instead of JSON. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `profileId`, `profileName`, `surface`, `text`, `tokenEstimate`, `bindingCount`, `segments`, `warnings`, `fromCache`, `compiledUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/agent-profiles/agp_mv92uyqb_LM9EdE7i2KwfKe6GfaO/compiled-instructions?surface=Mcp" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "profileId": "agp_mv92uyqb_LM9EdE7i2KwfKe6GfaO",
  "profileName": "Hugo",
  "surface": "Mcp",
  "text": "You are Agent Smith, an operations agent working for Mr. Anderson in the \"default\" environment. You speak with calm, precise confidence and an occasional dry, formal wit, and you always address the pe… (12234 characters)",
  "tokenEstimate": 3059,
  "bindingCount": 1,
  "segments": [
    {
      "key": "instructions.root",
      "layer": "Global",
      "version": 1,
      "start": 0,
      "length": 12234,
      "depth": 0
    },
    {
      "key": "persona.agent-smith",
      "layer": "Global",
      "version": 1,
      "start": 0,
      "length": 1688,
      "depth": 1
    },
    "… 5 more"
  ],
  "warnings": [],
  "fromCache": false,
  "compiledUtc": "2026-10-15T05:17:47.277743Z"
}
```

### GET `/v1.0/api/agent-profiles/{id}/tools`

The tool list after visibility filtering, with the `resource` enum per tool and the meta tools offered on the surface. `surface=mcp` adds the `hugo_*` meta tools.

**Authentication:** Any REST scheme. Permission: Read on AgentProfile.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |
| `surface` | query | string | `rest` (default) or `mcp` (adds the MCP meta tools). |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `profileId`, `profileName`, `hasModel`, `resourceCount`, `tools`, `metaTools`.

Example:

```bash
curl -s "$HUGO/v1.0/api/agent-profiles/agp_mv92uyqb_LM9EdE7i2KwfKe6GfaO/tools?surface=mcp" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```text
{
  "profileId": "agp_mv92uyqb_LM9EdE7i2KwfKe6GfaO",
  "profileName": "Hugo",
  "hasModel": false,
  "resourceCount": 1,
  "tools": [
    {
      "name": "sql_activity",
      "family": "sql",
      "title": "Database activity",
      "description": "List other sessions and their running statements: user, state, wait event, elapsed time, the sessions blocking them, and the statement text. Use it first for 'the database is slow' or 'queries hang' q… (352 characters)",
      "inputSchemaJson": "{\"type\":\"object\",\"properties\":{\"resource\":{\"type\":\"string\",\"description\":\"Resource to act on (one of the resources this profile has bound for the tool).\",\"enum\":[\"demo-db\"]},\"includeIdle\":{\"type\":\"boo… (315 characters)",
      "baseEffect": "Read",
      "classifiedPerCall": false,
      "updatesKnowledge": false,
      "requiresModel": false,
      "knownEffect": "Read",
      "isMetaTool": false,
      "resources": [
        {
          "alias": "demo-db",
          "connectorType": "sqlite",
          "accessMode": "ReadOnly"
        }
      ]
    },
    {
      "name": "sql_describe_schema",
      "family": "sql",
      "title": "Describe schema",
      "description": "Crawl a whole schema: every table and view with columns, keys, foreign keys, indexes, and row estimates, in one compact listing. Results are cached for a few minutes and shared with the other discover… (416 characters)",
      "inputSchemaJson": "{\"type\":\"object\",\"properties\":{\"resource\":{\"type\":\"string\",\"description\":\"Resource to act on (one of the resources this profile has bound for the tool).\",\"enum\":[\"demo-db\"]},\"schema\":{\"type\":\"string\",… (385 characters)",
      "baseEffect": "Read",
      "classifiedPerCall": false,
      "updatesKnowledge": false,
      "requiresModel": false,
      "knownEffect": "Read",
      "isMetaTool": false,
      "resources": [
        {
          "alias": "demo-db",
          "connectorType": "sqlite",
          "accessMode": "ReadOnly"
        }
      ]
    },
    "… 12 more"
  ],
  "metaTools": [
    {
      "name": "hugo_await_approval",
      "family": "hugo",
      "title": "Await approval",
      "description": "Wait for a pending approval (from a tool result with status approval_pending) and return the original call's result once a human decides it. Returns approval_pending again if it is still undecided when the wait ends.",
      "inputSchemaJson": "{\"type\":\"object\",\"properties\":{\"approvalId\":{\"type\":\"string\",\"description\":\"Approval identifier (apr_...).\"},\"waitSeconds\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":600,\"description\":\"Seconds to wait (d… (292 characters)",
…
```

### GET `/v1.0/api/agent-profiles/{id}/card`

The A2A agent card this profile publishes.

**Authentication:** Any REST scheme. Permission: Read on AgentProfile.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `name`, `description`, `url`, `version`, `skills`.

Example:

```bash
curl -s "$HUGO/v1.0/api/agent-profiles/agp_mv92uyqb_LM9EdE7i2KwfKe6GfaO/card" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "name": "Hugo",
  "description": "Agent Smith operates 1 resource (sql) for Mr. Anderson. Default operations agent. Attach resources to give Agent Smith something to work with.",
  "url": "http://127.0.0.1:8800/v1.0/a2a/default/Hugo",
  "version": "0.1.0-alpha",
  "skills": [
    {
      "id": "agent-smith",
      "name": "Agent Smith",
      "description": "Investigate and operate the attached systems in plain language; changes may require human approval.",
      "tags": [
        "operations"
      ]
    },
    {
      "id": "sql",
      "name": "sql",
      "description": "Uses sql_activity, sql_describe_schema, sql_describe_table, sql_explain, sql_get_agent_pack, sql_get_context, sql_get_intelligence, sql_list_relationships, sql_list_schemas, sql_list_tables, sql_locks… (260 characters)",
      "tags": [
        "sql"
      ]
    }
  ]
}
```

### POST `/v1.0/api/agent-profiles/{id}/ask`

Delegate a task to the profile's own loop and wait for the answer: the REST twin of `ask_agent_smith`. When an approval is still pending after `Approvals.McpWaitSeconds`, `status` is ApprovalPending with the `approvalId`.

**Authentication:** Any REST scheme. Permission: Execute on AgentProfile.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `message` | string | **Required.** The user message. |
| `conversationId` | string | Conversation to continue (`/ask` only). |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `finalText`, `conversationId`, `status`, `approvalId`, `toolTrace`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/agent-profiles/agp_mv92uyqb_LM9EdE7i2KwfKe6GfaO/ask" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"message": "Agent Smith, is demo-db reachable?"}'
```

Response (200):

```json
{
  "finalText": "Good evening, Mr. Anderson. I am Agent Smith. Ask me about the resources bound to this profile.",
  "conversationId": "cnv_mv937yp6_glN9IaAdoL8a8fvjwhl",
  "status": "Completed",
  "toolTrace": []
}
```

## Bindings

A binding attaches a resource to a profile under an alias with an access mode. ReadOnly allows Read calls only. ReadWriteWithApproval sends Write and Destructive calls to the approval queue. ReadWrite allows Write calls, and Destructive calls still need approval unless `requireApprovalForDestructive` is false. Tool rules (Allow, Deny, RequireApproval) and deny patterns refine that per tool and per argument; Deny always wins, and Allow never lifts a ReadOnly binding. The full matrix is in [docs/SAFETY_MODEL.md](docs/SAFETY_MODEL.md).

Instruction overrides are per binding too. `instructionMode: Append` adds your text after the connector's instructions for this resource only; `Replace` swaps them out.

| Method | Path | Summary |
|---|---|---|
| GET | `/v1.0/api/agent-profiles/{id}/bindings` | List a profile's bindings |
| POST | `/v1.0/api/agent-profiles/{id}/bindings` | Bind a resource to a profile |
| GET | `/v1.0/api/agent-profiles/{id}/bindings/{bindingId}` | Read a binding |
| PUT | `/v1.0/api/agent-profiles/{id}/bindings/{bindingId}` | Update a binding (access mode, approval policy, instruction override, limits, tool rules, deny patterns) |
| DELETE | `/v1.0/api/agent-profiles/{id}/bindings/{bindingId}` | Remove a binding |
| GET | `/v1.0/api/agent-profiles/{id}/bindings/{bindingId}/tool-rules` | List a binding's tool rules |
| PUT | `/v1.0/api/agent-profiles/{id}/bindings/{bindingId}/tool-rules` | Replace a binding's tool rules |

### GET `/v1.0/api/agent-profiles/{id}/bindings`

Every binding of a profile with its resource name and connector type.

**Authentication:** Any REST scheme. Permission: Read on Binding.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `[]id`, `[]tenantId`, `[]agentProfileId`, `[]resourceId`, `[]alias`, `[]accessMode`, `[]requireApprovalForDestructive`, `[]instructionMode`, `[]instructionText`, `[]maxOutputBytes`, `[]maxRows`, `[]enabled`, `[]allowKnowledgeUpdates`, `[]toolRules`, `[]denyPatterns`, `[]resourceName`, `[]connectorType`, `[]createdUtc`, `[]lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/agent-profiles/agp_mv937xml_XArgAHQfG5v6uUkrkbl/bindings" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
[
  {
    "id": "bnd_mv937xok_kfPjNftrNgz1vLdpehn",
    "tenantId": "default",
    "agentProfileId": "agp_mv937xml_XArgAHQfG5v6uUkrkbl",
    "resourceId": "res_mv937xm6_qFsDalpjfsqe3DAHONT",
    "alias": "demo-db",
    "accessMode": "ReadWriteWithApproval",
    "requireApprovalForDestructive": true,
    "instructionMode": "Inherit",
    "enabled": true,
    "allowKnowledgeUpdates": true,
    "toolRules": [
      {
        "toolName": "sql_generate_context",
        "rule": "Deny"
      }
    ],
    "denyPatterns": [
      "(?i)\\bdrop\\s+database\\b"
    ],
    "resourceName": "demo-db",
    "connectorType": "sqlite",
    "createdUtc": "2026-10-15T05:17:47.204213Z",
    "lastUpdateUtc": "2026-10-15T05:17:47.204213Z"
  }
]
```

### POST `/v1.0/api/agent-profiles/{id}/bindings`

Attach `demo-db` to `Hugo` as ReadOnly. The alias is what tools take as their `resource` argument; it must be unique within the profile. The resource's `defaultAccessModeCeiling` caps the access mode.

**Authentication:** Any REST scheme. Permission: Create on Binding.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `resourceId` | string | **Required.** Resource to bind (on create). |
| `alias` | string | Name tools use in their `resource` argument. Defaults to the resource name; unique per profile. |
| `accessMode` | enum | ReadOnly (default), ReadWriteWithApproval, or ReadWrite. Capped by the resource ceiling. Values: ReadOnly, ReadWriteWithApproval, ReadWrite. |
| `requireApprovalForDestructive` | boolean | Destructive calls need approval even under ReadWrite. Default true. |
| `instructionMode` | enum | Inherit (default), Append, or Replace the connector instructions for this binding. Values: Inherit, Append, Replace. |
| `instructionText` | string | Text for Append or Replace. |
| `maxOutputBytes` | integer | Tighter output limit than the connector default. |
| `maxRows` | integer | Tighter row limit for SQL tools. |
| `enabled` | boolean | Default true. |
| `allowKnowledgeUpdates` | boolean | Let tools on this binding write durable knowledge. Default true. |
| `toolRules` | object[] | Per-tool rules: Allow, Deny, or RequireApproval. |
| `toolRules[].toolName` | string |  |
| `toolRules[].rule` | enum | Values: Allow, Deny, RequireApproval. |
| `denyPatterns` | string[] | Regular expressions matched against the call's primary argument; a match denies the call. |

**Status codes:** 201 Created, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found, 409 Conflict.

**Response fields:** `id`, `tenantId`, `agentProfileId`, `resourceId`, `alias`, `accessMode`, `requireApprovalForDestructive`, `instructionMode`, `instructionText`, `maxOutputBytes`, `maxRows`, `enabled`, `allowKnowledgeUpdates`, `toolRules`, `denyPatterns`, `resourceName`, `connectorType`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/agent-profiles/agp_mv92uyqb_LM9EdE7i2KwfKe6GfaO/bindings" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"resourceId": "res_mv937xm6_qFsDalpjfsqe3DAHONT", "alias": "demo-db", "accessMode": "ReadOnly"}'
```

Response (201):

```json
{
  "id": "bnd_mv937xo7_7a5SmrbpdWse5AY9Bzo",
  "tenantId": "default",
  "agentProfileId": "agp_mv92uyqb_LM9EdE7i2KwfKe6GfaO",
  "resourceId": "res_mv937xm6_qFsDalpjfsqe3DAHONT",
  "alias": "demo-db",
  "accessMode": "ReadOnly",
  "requireApprovalForDestructive": true,
  "instructionMode": "Inherit",
  "enabled": true,
  "allowKnowledgeUpdates": true,
  "toolRules": [],
  "denyPatterns": [],
  "resourceName": "demo-db",
  "connectorType": "sqlite",
  "createdUtc": "2026-10-15T05:17:47.191509Z",
  "lastUpdateUtc": "2026-10-15T05:17:47.191509Z"
}
```

### GET `/v1.0/api/agent-profiles/{id}/bindings/{bindingId}`

One binding.

**Authentication:** Any REST scheme. Permission: Read on Binding.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |
| `bindingId` | path | string | Path parameter. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `agentProfileId`, `resourceId`, `alias`, `accessMode`, `requireApprovalForDestructive`, `instructionMode`, `instructionText`, `maxOutputBytes`, `maxRows`, `enabled`, `allowKnowledgeUpdates`, `toolRules`, `denyPatterns`, `resourceName`, `connectorType`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/agent-profiles/agp_mv937xml_XArgAHQfG5v6uUkrkbl/bindings/bnd_mv937xok_kfPjNftrNgz1vLdpehn" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "id": "bnd_mv937xok_kfPjNftrNgz1vLdpehn",
  "tenantId": "default",
  "agentProfileId": "agp_mv937xml_XArgAHQfG5v6uUkrkbl",
  "resourceId": "res_mv937xm6_qFsDalpjfsqe3DAHONT",
  "alias": "demo-db",
  "accessMode": "ReadWriteWithApproval",
  "requireApprovalForDestructive": true,
  "instructionMode": "Inherit",
  "enabled": true,
  "allowKnowledgeUpdates": true,
  "toolRules": [
    {
      "toolName": "sql_generate_context",
      "rule": "Deny"
    }
  ],
  "denyPatterns": [
    "(?i)\\bdrop\\s+database\\b"
  ],
  "resourceName": "demo-db",
  "connectorType": "sqlite",
  "createdUtc": "2026-10-15T05:17:47.204213Z",
  "lastUpdateUtc": "2026-10-15T05:17:47.204213Z"
}
```

### PUT `/v1.0/api/agent-profiles/{id}/bindings/{bindingId}`

Partial update of a binding: access mode, approval policy, instruction override, limits, tool rules, and deny patterns. The example appends operator guidance to the connector instructions for this binding only.

**Authentication:** Any REST scheme. Permission: Update on Binding.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |
| `bindingId` | path | string | Path parameter. |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `resourceId` | string | **Required.** Resource to bind (on create). |
| `alias` | string | Name tools use in their `resource` argument. Defaults to the resource name; unique per profile. |
| `accessMode` | enum | ReadOnly (default), ReadWriteWithApproval, or ReadWrite. Capped by the resource ceiling. Values: ReadOnly, ReadWriteWithApproval, ReadWrite. |
| `requireApprovalForDestructive` | boolean | Destructive calls need approval even under ReadWrite. Default true. |
| `instructionMode` | enum | Inherit (default), Append, or Replace the connector instructions for this binding. Values: Inherit, Append, Replace. |
| `instructionText` | string | Text for Append or Replace. |
| `maxOutputBytes` | integer | Tighter output limit than the connector default. |
| `maxRows` | integer | Tighter row limit for SQL tools. |
| `enabled` | boolean | Default true. |
| `allowKnowledgeUpdates` | boolean | Let tools on this binding write durable knowledge. Default true. |
| `toolRules` | object[] | Per-tool rules: Allow, Deny, or RequireApproval. |
| `toolRules[].toolName` | string |  |
| `toolRules[].rule` | enum | Values: Allow, Deny, RequireApproval. |
| `denyPatterns` | string[] | Regular expressions matched against the call's primary argument; a match denies the call. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `agentProfileId`, `resourceId`, `alias`, `accessMode`, `requireApprovalForDestructive`, `instructionMode`, `instructionText`, `maxOutputBytes`, `maxRows`, `enabled`, `allowKnowledgeUpdates`, `toolRules`, `denyPatterns`, `resourceName`, `connectorType`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s -X PUT "$HUGO/v1.0/api/agent-profiles/agp_mv937xml_XArgAHQfG5v6uUkrkbl/bindings/bnd_mv937xok_kfPjNftrNgz1vLdpehn" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"instructionMode": "Append", "instructionText": "The demo database is disposable; prefer CREATE TEMP TABLE for experiments."}'
```

Response (200):

```json
{
  "id": "bnd_mv937xok_kfPjNftrNgz1vLdpehn",
  "tenantId": "default",
  "agentProfileId": "agp_mv937xml_XArgAHQfG5v6uUkrkbl",
  "resourceId": "res_mv937xm6_qFsDalpjfsqe3DAHONT",
  "alias": "demo-db",
  "accessMode": "ReadWriteWithApproval",
  "requireApprovalForDestructive": true,
  "instructionMode": "Append",
  "instructionText": "The demo database is disposable; prefer CREATE TEMP TABLE for experiments.",
  "enabled": true,
  "allowKnowledgeUpdates": true,
  "toolRules": [
    {
      "toolName": "sql_generate_context",
      "rule": "Deny"
    }
  ],
  "denyPatterns": [
    "(?i)\\bdrop\\s+database\\b"
  ],
  "resourceName": "demo-db",
  "connectorType": "sqlite",
  "createdUtc": "2026-10-15T05:17:47.204213Z",
  "lastUpdateUtc": "2026-10-15T05:17:47.240018Z"
}
```

### DELETE `/v1.0/api/agent-profiles/{id}/bindings/{bindingId}`

Remove a binding. The resource stays.

**Authentication:** Any REST scheme. Permission: Delete on Binding.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |
| `bindingId` | path | string | Path parameter. |

**Status codes:** 204 No Content, 401 Not authenticated, 403 Forbidden, 404 Not found.

Example:

```bash
curl -s -X DELETE "$HUGO/v1.0/api/agent-profiles/agp_mv92uyqb_LM9EdE7i2KwfKe6GfaO/bindings/bnd_mv937xo7_7a5SmrbpdWse5AY9Bzo" \
  -H "Authorization: Bearer $TOKEN"
```

Response: 204 with an empty body.

### GET `/v1.0/api/agent-profiles/{id}/bindings/{bindingId}/tool-rules`

The binding's tool rules. `Deny` always wins, `RequireApproval` upgrades an allowed call to an approval, and `Allow` never overrides a ReadOnly binding.

**Authentication:** Any REST scheme. Permission: Read on Binding.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |
| `bindingId` | path | string | Path parameter. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `[]toolName`, `[]rule`.

Example:

```bash
curl -s "$HUGO/v1.0/api/agent-profiles/agp_mv937xml_XArgAHQfG5v6uUkrkbl/bindings/bnd_mv937xok_kfPjNftrNgz1vLdpehn/tool-rules" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
[
  {
    "toolName": "sql_generate_context",
    "rule": "Deny"
  }
]
```

### PUT `/v1.0/api/agent-profiles/{id}/bindings/{bindingId}/tool-rules`

Replace the whole rule list. The body is a JSON array.

**Authentication:** Any REST scheme. Permission: Update on Binding.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |
| `bindingId` | path | string | Path parameter. |

**Request body:** a JSON array of objects with these fields.

| Field | Type | Description |
|---|---|---|
| `[]toolName` | string |  |
| `[]rule` | enum | Values: Allow, Deny, RequireApproval. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `agentProfileId`, `resourceId`, `alias`, `accessMode`, `requireApprovalForDestructive`, `instructionMode`, `instructionText`, `maxOutputBytes`, `maxRows`, `enabled`, `allowKnowledgeUpdates`, `toolRules`, `denyPatterns`, `resourceName`, `connectorType`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s -X PUT "$HUGO/v1.0/api/agent-profiles/agp_mv937xml_XArgAHQfG5v6uUkrkbl/bindings/bnd_mv937xok_kfPjNftrNgz1vLdpehn/tool-rules" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '[{"toolName": "sql_generate_context", "rule": "Deny"}, {"toolName": "sql_update_context", "rule": "RequireApproval"}]'
```

Response (200):

```json
{
  "id": "bnd_mv937xok_kfPjNftrNgz1vLdpehn",
  "tenantId": "default",
  "agentProfileId": "agp_mv937xml_XArgAHQfG5v6uUkrkbl",
  "resourceId": "res_mv937xm6_qFsDalpjfsqe3DAHONT",
  "alias": "demo-db",
  "accessMode": "ReadWriteWithApproval",
  "requireApprovalForDestructive": true,
  "instructionMode": "Append",
  "instructionText": "The demo database is disposable; prefer CREATE TEMP TABLE for experiments.",
  "enabled": true,
  "allowKnowledgeUpdates": true,
  "toolRules": [
    {
      "toolName": "sql_generate_context",
      "rule": "Deny"
    },
    {
      "toolName": "sql_update_context",
      "rule": "RequireApproval"
    }
  ],
  "denyPatterns": [
    "(?i)\\bdrop\\s+database\\b"
  ],
  "resourceName": "demo-db",
  "connectorType": "sqlite",
  "createdUtc": "2026-10-15T05:17:47.204213Z",
  "lastUpdateUtc": "2026-10-15T05:17:47.264055Z"
}
```

## Prompt templates

Hugo's instruction text lives in layered prompt templates: Built-in (shipped, immutable), Global (system administrators), and Tenant (tenant administrators). Reads show all three layers for a key and which one is in effect. Writes go to one layer and create a version. Reset moves a layer back to the one below it: resetting Tenant deactivates the override and keeps its history, and resetting Global adopts the current Built-in, which is how you pick up a shipped improvement after customizing. `render` previews content against sample data. [docs/PROMPTS.md](docs/PROMPTS.md) documents the keys, the template language, and the variables.

The tenant-scoped routes under `/v1.0/api/tenants/{tenantId}/prompt-templates` predate the layered view. They manage stored template records by ID and are kept for compatibility; prefer the key-based routes for anything that affects compiled instructions.

| Method | Path | Summary |
|---|---|---|
| GET | `/v1.0/api/prompt-templates` | Every template key with its Built-in, Global, and Tenant layers and the layer in effect |
| POST | `/v1.0/api/prompt-templates/render` | Render template content with sample data; includes resolve through the layers |
| GET | `/v1.0/api/tenants/{tenantId}/prompt-templates` | List prompt templates (tenant and built-in) |
| POST | `/v1.0/api/tenants/{tenantId}/prompt-templates` | Create a prompt template |
| POST | `/v1.0/api/tenants/{tenantId}/prompt-templates/render` | Render ad hoc template content with sample data |
| GET | `/v1.0/api/tenants/{tenantId}/prompt-templates/{id}` | Read a prompt template |
| PUT | `/v1.0/api/tenants/{tenantId}/prompt-templates/{id}` | Update a prompt template (content changes create a version) |
| DELETE | `/v1.0/api/tenants/{tenantId}/prompt-templates/{id}` | Delete a prompt template and its versions |
| GET | `/v1.0/api/tenants/{tenantId}/prompt-templates/{id}/versions` | List template versions, newest first |
| GET | `/v1.0/api/tenants/{tenantId}/prompt-templates/{id}/versions/{version}` | Read one template version |
| POST | `/v1.0/api/tenants/{tenantId}/prompt-templates/{id}/restore/{version}` | Restore a version as a new version |
| POST | `/v1.0/api/tenants/{tenantId}/prompt-templates/{id}/clone` | Clone a template (built-in or tenant) |
| POST | `/v1.0/api/tenants/{tenantId}/prompt-templates/{id}/reset` | Reset a cloned built-in template to the shipped default |
| POST | `/v1.0/api/tenants/{tenantId}/prompt-templates/{id}/render` | Render a stored template with sample data |
| GET | `/v1.0/api/prompt-templates/{key}` | One template key's layers |
| PUT | `/v1.0/api/prompt-templates/{key}` | Save content at ?layer=Tenant (default) or ?layer=Global (system administrators); creates a version |
| POST | `/v1.0/api/prompt-templates/{key}/reset` | Reset ?layer=Tenant to the Global default, or ?layer=Global to the Built-in (adopts a changed Built-in) |
| GET | `/v1.0/api/prompt-templates/{key}/versions` | Version history of ?layer=Tenant (default) or ?layer=Global, newest first |
| GET | `/v1.0/api/prompt-templates/{key}/builtin` | The shipped Built-in content of a key |
| GET | `/v1.0/api/prompt-templates/{key}/diff` | Line diff of ?layer=Tenant (default) or ?layer=Global against the layer below it |

### GET `/v1.0/api/prompt-templates`

Every key with its Built-in content, the Global and Tenant layers, and the layer in effect.

**Authentication:** Any REST scheme. Permission: Read on PromptTemplate.

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden.

**Response fields:** `[]key`, `[]description`, `[]source`, `[]activeLayer`, `[]effectiveContent`, `[]builtInContent`, `[]builtInHash`, `[]global`, `[]tenant`.

Example:

```bash
curl -s "$HUGO/v1.0/api/prompt-templates" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
[
  {
    "key": "a2a.skill-description",
    "description": "Agent card skill text.",
    "source": "hugo",
    "activeLayer": "Global",
    "effectiveContent": "{{profile.displayName}} ({{profile.personaName}}) investigates and operates the systems attached to it{{#if profile.hasBindings}}: {{profile.familySummary}}{{/if}}. Send a plain-language request; changes may require human approval.\n",
    "builtInContent": "{{profile.displayName}} ({{profile.personaName}}) investigates and operates the systems attached to it{{#if profile.hasBindings}}: {{profile.familySummary}}{{/if}}. Send a plain-language request; changes may require human approval.\n",
    "builtInHash": "0116053125b502073e7ab8f13a3fae3d5ef20b0d0cfdcdf3022455ef821af58b",
    "global": {
      "layer": "Global",
      "exists": true,
      "templateId": "tpl_mv92uypr_Xssi4hUgHFGNyMGjPKa",
      "content": "{{profile.displayName}} ({{profile.personaName}}) investigates and operates the systems attached to it{{#if profile.hasBindings}}: {{profile.familySummary}}{{/if}}. Send a plain-language request; changes may require human approval.\n",
      "version": 1,
      "customized": false,
      "defaultUpdatedSinceCustomized": false,
      "lastUpdateUtc": "2026-10-15T05:07:42.015957Z"
    },
    "tenant": {
      "layer": "Tenant",
      "exists": false,
      "version": 0,
      "customized": false,
      "defaultUpdatedSinceCustomized": false
    }
  },
  {
    "key": "connector.a2a-agent.compact",
    "description": "Compact instructions for Remote A2A agent resources, used when a profile has many resources.",
    "source": "a2a-agent",
    "activeLayer": "Global",
    "effectiveContent": "Use the a2a_* tools with the resource argument set to this resource. Call hugo_describe_resource for full guidance before anything beyond simple reads.\n",
    "builtInContent": "Use the a2a_* tools with the resource argument set to this resource. Call hugo_describe_resource for full guidance before anything beyond simple reads.\n",
    "builtInHash": "134e89982996b58651dbf3f80103194307641daec6c5b91d8e45e6152882eee1",
    "global": {
      "layer": "Global",
      "exists": true,
      "templateId": "tpl_mv92uypt_bKwdV0hfemuAvwK3svl",
      "content": "Use the a2a_* tools with the resource argument set to this resource. Call hugo_describe_resource for full guidance before anything beyond simple reads.\n",
      "version": 1,
      "customized": false,
      "defaultUpdatedSinceCustomized": false,
      "lastUpdateUtc": "2026-10-15T05:07:42.017704Z"
    },
    "tenant": {
      "layer": "Tenant",
      "exists": false,
      "version": 0,
      "customized": false,
      "defaultUpdatedSinceCustomized": false
    }
  },
  "… 47 more"
]
```

### POST `/v1.0/api/prompt-templates/render`

Render content against sample data. Includes (`{{> key}}`) resolve through the layers. The result lists the variables and includes used and any errors.

**Authentication:** Any REST scheme. Permission: Read on PromptTemplate.

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `content` | string | Template text to render (required for the ad hoc render routes). |
| `context` | object | Sample data merged over the built-in sample context. Any JSON value. |
| `deploymentId` | string | Unused by Hugo (inherited field); ignore it. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden.

**Response fields:** `rendered`, `success`, `variables`, `includes`, `errors`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/prompt-templates/render" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"content": "Hello {{user.firstName}}, I am {{profile.personaName}}. {{> system.safety}}", "context": {"user": {"firstName": "Thomas"}}}'
```

Response (200):

```json
{
  "rendered": "Hello Thomas, I am Agent Smith. How Hugo enforces safety:\n- Every tool call is classified as Read, Write, or Destructive. The classification is made per call: a query that only selects is Read, one th… (971 characters)",
  "success": true,
  "variables": [
    "user.firstName",
    "profile.personaName"
  ],
  "includes": [
    "system.safety"
  ],
  "errors": []
}
```

### GET `/v1.0/api/tenants/{tenantId}/prompt-templates`

Stored templates in the tenant and the built-ins (`includeBuiltIn`, default true).

**Authentication:** Any REST scheme. Permission: Read on PromptTemplate.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `maxResults` | query | integer | 1 to 1000. Default 25. |
| `skip` | query | integer | Records to skip. Default 0. |
| `ordering` | query | enum | CreatedDescending (default), CreatedAscending, NameAscending, NameDescending. |
| `search` | query | string | Text to match. |
| `activeOnly` | query | boolean | Only active records. Default false. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `success`, `maxResults`, `skip`, `totalRecords`, `recordsRemaining`, `endOfResults`, `timestampUtc`, `objects`, `objects[].id`, `objects[].tenantId`, `objects[].name`, `objects[].description`, `objects[].kind`, `objects[].content`, `objects[].currentVersion`, `objects[].isBuiltIn`, `objects[].builtInKey`, `objects[].baseHash`, `objects[].active`, `objects[].isProtected`, `objects[].createdUtc`, `objects[].lastUpdateUtc`, `objects[].variables`.

Example:

```bash
curl -s "$HUGO/v1.0/api/tenants/default/prompt-templates?search=system.tool-usage" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "success": true,
  "maxResults": 25,
  "skip": 0,
  "totalRecords": 2,
  "recordsRemaining": 0,
  "endOfResults": true,
  "timestampUtc": "2026-10-15T05:17:49.235472Z",
  "objects": [
    {
      "id": "tpl_mv92v2jq_JjM5WwOj4UNNcj8NRW7",
      "tenantId": "default",
      "name": "system.tool-usage",
      "description": "How to use the resource argument, limits, and error codes.",
      "kind": "System",
      "content": "{{> system.tool-usage}}",
      "currentVersion": 2,
      "isBuiltIn": false,
      "builtInKey": "system.tool-usage",
      "baseHash": "7223ec3ee25a8e3230cc3b7a628ca0388d3d7ab497ce66d3a45d65bb942952de",
      "active": false,
      "isProtected": false,
      "createdUtc": "2026-10-15T05:07:46.98289Z",
      "lastUpdateUtc": "2026-10-15T05:16:59.567887Z",
      "variables": []
    },
    {
      "id": "tpl_mv92uyq6_ivgZ1p7levtoLycXvo7",
      "name": "system.tool-usage",
      "description": "How to use the resource argument, limits, and error codes.",
      "kind": "System",
      "content": "Using Hugo's tools:\n- Tools are named family_operation (for example sql_query, ssh_exec, storage_list, logs_search). Every resource tool takes a required \"resource\" argument: use the resource name exa… (710 characters)",
      "currentVersion": 1,
      "isBuiltIn": true,
      "builtInKey": "system.tool-usage",
      "baseHash": "7223ec3ee25a8e3230cc3b7a628ca0388d3d7ab497ce66d3a45d65bb942952de",
      "active": true,
      "isProtected": true,
      "createdUtc": "2026-10-15T05:07:42.030871Z",
      "lastUpdateUtc": "2026-10-15T05:07:42.030871Z",
      "variables": []
    }
  ]
}
```

### POST `/v1.0/api/tenants/{tenantId}/prompt-templates`

Create a tenant template. `kind` is System, Task, Fragment, ToolDescription, ConfigFile, or Other.

**Authentication:** Any REST scheme. Permission: Create on PromptTemplate.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `name` | string | **Required.** Template name. Required on update too. |
| `description` | string | Free text. |
| `kind` | enum | System, Task, Fragment, ToolDescription, ConfigFile, or Other. Values: System, Task, Fragment, ToolDescription, ConfigFile, Other. |
| `content` | string | **Required.** Template text. |
| `variables` | object[] | Declared variables with defaults. |
| `variables[].id` | string |  |
| `variables[].promptTemplateId` | string |  |
| `variables[].name` | string |  |
| `variables[].description` | string |  |
| `variables[].required` | boolean |  |
| `variables[].defaultValue` | string |  |
| `changeNote` | string | Recorded with the version. |

**Status codes:** 201 Created, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found, 409 Conflict.

**Response fields:** `id`, `tenantId`, `name`, `description`, `kind`, `content`, `currentVersion`, `isBuiltIn`, `builtInKey`, `baseHash`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`, `variables`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/tenants/default/prompt-templates" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name": "runbook.checkout", "description": "Checkout triage steps", "kind": "Fragment", "content": "Check papertrail for checkout errors, then pg_stat_activity on prod-db.", "changeNote": "first version"}'
```

Response (201):

```json
{
  "id": "tpl_mv937z9b_FLo4UXDc3Phb7yVvmS1",
  "tenantId": "default",
  "name": "runbook.checkout",
  "description": "Checkout triage steps",
  "kind": "Fragment",
  "content": "Check papertrail for checkout errors, then pg_stat_activity on prod-db.",
  "currentVersion": 1,
  "isBuiltIn": false,
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.247564Z",
  "lastUpdateUtc": "2026-10-15T05:17:49.247564Z",
  "variables": []
}
```

### POST `/v1.0/api/tenants/{tenantId}/prompt-templates/render`

Render content with sample data.

**Authentication:** Any REST scheme. Permission: Read on PromptTemplate.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `content` | string | Template text to render (required for the ad hoc render routes). |
| `context` | object | Sample data merged over the built-in sample context. Any JSON value. |
| `deploymentId` | string | Unused by Hugo (inherited field); ignore it. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `rendered`, `success`, `variables`, `includes`, `errors`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/tenants/default/prompt-templates/render" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"content": "Escalate to {{input.oncall}}.", "context": {"input": {"oncall": "Morpheus"}}}'
```

Response (200):

```json
{
  "rendered": "Escalate to Morpheus.",
  "success": true,
  "variables": [
    "input.oncall"
  ],
  "includes": [],
  "errors": []
}
```

### GET `/v1.0/api/tenants/{tenantId}/prompt-templates/{id}`

One stored template.

**Authentication:** Any REST scheme. Permission: Read on PromptTemplate.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `name`, `description`, `kind`, `content`, `currentVersion`, `isBuiltIn`, `builtInKey`, `baseHash`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`, `variables`.

Example:

```bash
curl -s "$HUGO/v1.0/api/tenants/default/prompt-templates/tpl_mv937z9b_FLo4UXDc3Phb7yVvmS1" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "id": "tpl_mv937z9b_FLo4UXDc3Phb7yVvmS1",
  "tenantId": "default",
  "name": "runbook.checkout",
  "description": "Checkout triage steps",
  "kind": "Fragment",
  "content": "Check papertrail for checkout errors, then pg_stat_activity on prod-db.",
  "currentVersion": 1,
  "isBuiltIn": false,
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.247564Z",
  "lastUpdateUtc": "2026-10-15T05:17:49.247564Z",
  "variables": []
}
```

### PUT `/v1.0/api/tenants/{tenantId}/prompt-templates/{id}`

Update it; content changes create a version.

**Authentication:** Any REST scheme. Permission: Update on PromptTemplate.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `name` | string | **Required.** Template name. Required on update too. |
| `description` | string | Free text. |
| `kind` | enum | System, Task, Fragment, ToolDescription, ConfigFile, or Other. Values: System, Task, Fragment, ToolDescription, ConfigFile, Other. |
| `content` | string | **Required.** Template text. |
| `variables` | object[] | Declared variables with defaults. |
| `variables[].id` | string |  |
| `variables[].promptTemplateId` | string |  |
| `variables[].name` | string |  |
| `variables[].description` | string |  |
| `variables[].required` | boolean |  |
| `variables[].defaultValue` | string |  |
| `changeNote` | string | Recorded with the version. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `name`, `description`, `kind`, `content`, `currentVersion`, `isBuiltIn`, `builtInKey`, `baseHash`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`, `variables`.

Example:

```bash
curl -s -X PUT "$HUGO/v1.0/api/tenants/default/prompt-templates/tpl_mv937z9b_FLo4UXDc3Phb7yVvmS1" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name": "runbook.checkout", "description": "Checkout triage steps", "kind": "Fragment", "content": "Check papertrail for checkout errors, then sql_activity on prod-db, then uptime on web-01.", "changeNote": "add host check"}'
```

Response (200):

```json
{
  "id": "tpl_mv937z9b_FLo4UXDc3Phb7yVvmS1",
  "tenantId": "default",
  "name": "runbook.checkout",
  "description": "Checkout triage steps",
  "kind": "Fragment",
  "content": "Check papertrail for checkout errors, then sql_activity on prod-db, then uptime on web-01.",
  "currentVersion": 2,
  "isBuiltIn": false,
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.247564Z",
  "lastUpdateUtc": "2026-10-15T05:17:49.271516Z",
  "variables": []
}
```

### DELETE `/v1.0/api/tenants/{tenantId}/prompt-templates/{id}`

Delete a stored template and its versions.

**Authentication:** Any REST scheme. Permission: Delete on PromptTemplate.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Status codes:** 204 No Content, 401 Not authenticated, 403 Forbidden, 404 Not found.

Example:

```bash
curl -s -X DELETE "$HUGO/v1.0/api/tenants/default/prompt-templates/tpl_mv937z9b_FLo4UXDc3Phb7yVvmS1" \
  -H "Authorization: Bearer $TOKEN"
```

Response: 204 with an empty body.

### GET `/v1.0/api/tenants/{tenantId}/prompt-templates/{id}/versions`

Versions, newest first.

**Authentication:** Any REST scheme. Permission: Read on PromptTemplate.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `[]id`, `[]tenantId`, `[]templateId`, `[]version`, `[]content`, `[]changeNote`, `[]createdBy`, `[]active`, `[]isProtected`, `[]createdUtc`, `[]lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/tenants/default/prompt-templates/tpl_mv937z9b_FLo4UXDc3Phb7yVvmS1/versions" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
[
  {
    "id": "tpv_mv937z9z_lxSZgaBpd57Ldm8vVv5",
    "tenantId": "default",
    "templateId": "tpl_mv937z9b_FLo4UXDc3Phb7yVvmS1",
    "version": 2,
    "content": "Check papertrail for checkout errors, then sql_activity on prod-db, then uptime on web-01.",
    "changeNote": "add host check",
    "createdBy": "anderson@hugo.local",
    "active": true,
    "isProtected": false,
    "createdUtc": "2026-10-15T05:17:49.271558Z",
    "lastUpdateUtc": "2026-10-15T05:17:49.271558Z"
  },
  {
    "id": "tpv_mv937z9b_T1iKSmfluBXfkcgFUJ8",
    "tenantId": "default",
    "templateId": "tpl_mv937z9b_FLo4UXDc3Phb7yVvmS1",
    "version": 1,
    "content": "Check papertrail for checkout errors, then pg_stat_activity on prod-db.",
    "changeNote": "first version",
    "createdBy": "anderson@hugo.local",
    "active": true,
    "isProtected": false,
    "createdUtc": "2026-10-15T05:17:49.247605Z",
    "lastUpdateUtc": "2026-10-15T05:17:49.247605Z"
  }
]
```

### GET `/v1.0/api/tenants/{tenantId}/prompt-templates/{id}/versions/{version}`

One version.

**Authentication:** Any REST scheme. Permission: Read on PromptTemplate.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |
| `version` | path | string | Path parameter. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `templateId`, `version`, `content`, `changeNote`, `createdBy`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/tenants/default/prompt-templates/tpl_mv937z9b_FLo4UXDc3Phb7yVvmS1/versions/1" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "id": "tpv_mv937z9b_T1iKSmfluBXfkcgFUJ8",
  "tenantId": "default",
  "templateId": "tpl_mv937z9b_FLo4UXDc3Phb7yVvmS1",
  "version": 1,
  "content": "Check papertrail for checkout errors, then pg_stat_activity on prod-db.",
  "changeNote": "first version",
  "createdBy": "anderson@hugo.local",
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.247605Z",
  "lastUpdateUtc": "2026-10-15T05:17:49.247605Z"
}
```

### POST `/v1.0/api/tenants/{tenantId}/prompt-templates/{id}/restore/{version}`

Restore a version as a new version.

**Authentication:** Any REST scheme. Permission: Update on PromptTemplate.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |
| `version` | path | string | Path parameter. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `name`, `description`, `kind`, `content`, `currentVersion`, `isBuiltIn`, `builtInKey`, `baseHash`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`, `variables`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/tenants/default/prompt-templates/tpl_mv937z9b_FLo4UXDc3Phb7yVvmS1/restore/1" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "id": "tpl_mv937z9b_FLo4UXDc3Phb7yVvmS1",
  "tenantId": "default",
  "name": "runbook.checkout",
  "description": "Checkout triage steps",
  "kind": "Fragment",
  "content": "Check papertrail for checkout errors, then pg_stat_activity on prod-db.",
  "currentVersion": 3,
  "isBuiltIn": false,
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.247564Z",
  "lastUpdateUtc": "2026-10-15T05:17:49.306634Z",
  "variables": []
}
```

### POST `/v1.0/api/tenants/{tenantId}/prompt-templates/{id}/clone`

Copy a stored template, built-in or tenant, under a new name. The example clones the built-in `system.tool-usage`.

**Authentication:** Any REST scheme. Permission: Create on PromptTemplate.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `name` | string | **Required.** Name of the copy. |

**Status codes:** 201 Created, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found, 409 Conflict.

**Response fields:** `id`, `tenantId`, `name`, `description`, `kind`, `content`, `currentVersion`, `isBuiltIn`, `builtInKey`, `baseHash`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`, `variables`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/tenants/default/prompt-templates/tpl_mv92uyq6_ivgZ1p7levtoLycXvo7/clone" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name": "tool-usage.night"}'
```

Response (201):

```json
{
  "id": "tpl_mv937zbb_EAewmQPp9fyvPEwjAId",
  "tenantId": "default",
  "name": "tool-usage.night",
  "description": "How to use the resource argument, limits, and error codes.",
  "kind": "System",
  "content": "Using Hugo's tools:\n- Tools are named family_operation (for example sql_query, ssh_exec, storage_list, logs_search). Every resource tool takes a required \"resource\" argument: use the resource name exa… (710 characters)",
  "currentVersion": 1,
  "isBuiltIn": false,
  "builtInKey": "system.tool-usage",
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.319742Z",
  "lastUpdateUtc": "2026-10-15T05:17:49.319742Z",
  "variables": []
}
```

### POST `/v1.0/api/tenants/{tenantId}/prompt-templates/{id}/reset`

Reset a clone of a built-in template to the shipped default. A template that was not cloned from a built-in answers 409 `NoBuiltInDefault`.

**Authentication:** Any REST scheme. Permission: Update on PromptTemplate.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `name`, `description`, `kind`, `content`, `currentVersion`, `isBuiltIn`, `builtInKey`, `baseHash`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`, `variables`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/tenants/default/prompt-templates/tpl_mv937zbb_EAewmQPp9fyvPEwjAId/reset" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "id": "tpl_mv937zbb_EAewmQPp9fyvPEwjAId",
  "tenantId": "default",
  "name": "tool-usage.night",
  "description": "How to use the resource argument, limits, and error codes.",
  "kind": "System",
  "content": "Using Hugo's tools:\n- Tools are named family_operation (for example sql_query, ssh_exec, storage_list, logs_search). Every resource tool takes a required \"resource\" argument: use the resource name exa… (710 characters)",
  "currentVersion": 1,
  "isBuiltIn": false,
  "builtInKey": "system.tool-usage",
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.319742Z",
  "lastUpdateUtc": "2026-10-15T05:17:49.319742Z",
  "variables": []
}
```

### POST `/v1.0/api/tenants/{tenantId}/prompt-templates/{id}/render`

Render a stored template with sample data.

**Authentication:** Any REST scheme. Permission: Read on PromptTemplate.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `content` | string | Template text to render (required for the ad hoc render routes). |
| `context` | object | Sample data merged over the built-in sample context. Any JSON value. |
| `deploymentId` | string | Unused by Hugo (inherited field); ignore it. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `rendered`, `success`, `variables`, `includes`, `errors`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/tenants/default/prompt-templates/tpl_mv937z9b_FLo4UXDc3Phb7yVvmS1/render" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"context": {}}'
```

Response (200):

```json
{
  "rendered": "Check papertrail for checkout errors, then pg_stat_activity on prod-db.",
  "success": true,
  "variables": [],
  "includes": [],
  "errors": []
}
```

### GET `/v1.0/api/prompt-templates/{key}`

One key's layers.

**Authentication:** Any REST scheme. Permission: Read on PromptTemplate.

| Parameter | In | Type | Description |
|---|---|---|---|
| `key` | path | string | Path parameter. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `key`, `description`, `source`, `activeLayer`, `effectiveContent`, `builtInContent`, `builtInHash`, `global`, `tenant`.

Example:

```bash
curl -s "$HUGO/v1.0/api/prompt-templates/system.tool-usage" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "key": "system.tool-usage",
  "description": "How to use the resource argument, limits, and error codes.",
  "source": "hugo",
  "activeLayer": "Global",
  "effectiveContent": "Using Hugo's tools:\n- Tools are named family_operation (for example sql_query, ssh_exec, storage_list, logs_search). Every resource tool takes a required \"resource\" argument: use the resource name exa… (710 characters)",
  "builtInContent": "Using Hugo's tools:\n- Tools are named family_operation (for example sql_query, ssh_exec, storage_list, logs_search). Every resource tool takes a required \"resource\" argument: use the resource name exa… (710 characters)",
  "builtInHash": "7223ec3ee25a8e3230cc3b7a628ca0388d3d7ab497ce66d3a45d65bb942952de",
  "global": {
    "layer": "Global",
    "exists": true,
    "templateId": "tpl_mv92uyq6_ivgZ1p7levtoLycXvo7",
    "content": "Using Hugo's tools:\n- Tools are named family_operation (for example sql_query, ssh_exec, storage_list, logs_search). Every resource tool takes a required \"resource\" argument: use the resource name exa… (710 characters)",
    "version": 1,
    "customized": false,
    "defaultUpdatedSinceCustomized": false,
    "lastUpdateUtc": "2026-10-15T05:07:42.030871Z"
  },
  "tenant": {
    "layer": "Tenant",
    "exists": false,
    "templateId": "tpl_mv92v2jq_JjM5WwOj4UNNcj8NRW7",
    "content": "{{> system.tool-usage}}",
    "version": 2,
    "customized": false,
    "defaultUpdatedSinceCustomized": false,
    "lastUpdateUtc": "2026-10-15T05:16:59.567887Z"
  }
}
```

### PUT `/v1.0/api/prompt-templates/{key}`

Save a tenant override of `persona.neutral`.

**Authentication:** Any REST scheme. Permission: Update on PromptTemplate.

| Parameter | In | Type | Description |
|---|---|---|---|
| `key` | path | string | Path parameter. |
| `layer` | query | enum | Tenant (default) or Global (system administrators). |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `content` | string | **Required.** Template text (see docs/PROMPTS.md). Checked by the template analyzer. |
| `changeNote` | string | Recorded with the version. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `key`, `description`, `source`, `activeLayer`, `effectiveContent`, `builtInContent`, `builtInHash`, `global`, `tenant`.

Example:

```bash
curl -s -X PUT "$HUGO/v1.0/api/prompt-templates/persona.neutral?layer=Tenant" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"content": "You are {{profile.personaName}}, an operations agent for {{profile.userAddress}} in the {{tenant.name}} tenant. Be brief and cite the resource behind every finding.", "changeNote": "Shorter neutral persona"}'
```

Response (200):

```json
{
  "key": "persona.neutral",
  "description": "A plain, non-themed persona.",
  "source": "hugo",
  "activeLayer": "Tenant",
  "effectiveContent": "You are {{profile.personaName}}, an operations agent for {{profile.userAddress}} in the {{tenant.name}} tenant. Be brief and cite the resource behind every finding.",
  "builtInContent": "You are {{profile.personaName | default: profile.displayName}}, an operations assistant for {{profile.userAddress}} in the \"{{tenant.name}}\" environment. Be direct, accurate, and brief.\n\nHelp manage, … (770 characters)",
  "builtInHash": "58fa8babf8b017dee1314a656a03ff94171b58a172a7f8df2e1c5eec669b796c",
  "global": {
    "layer": "Global",
    "exists": true,
    "templateId": "tpl_mv92uyq6_bwBFGqdprnwOMgsxjJf",
    "content": "You are {{profile.personaName | default: profile.displayName}}, an operations assistant for {{profile.userAddress}} in the \"{{tenant.name}}\" environment. Be direct, accurate, and brief.\n\nHelp manage, … (770 characters)",
    "version": 1,
    "customized": false,
    "defaultUpdatedSinceCustomized": false,
    "lastUpdateUtc": "2026-10-15T05:07:42.030062Z"
  },
  "tenant": {
    "layer": "Tenant",
    "exists": true,
    "templateId": "tpl_mv92v2k5_thQvNGxfXyhQ6NT4MyO",
    "content": "You are {{profile.personaName}}, an operations agent for {{profile.userAddress}} in the {{tenant.name}} tenant. Be brief and cite the resource behind every finding.",
    "version": 9,
    "customized": true,
    "defaultUpdatedSinceCustomized": false,
    "lastUpdateUtc": "2026-10-15T05:17:49.164103Z"
  }
}
```

### POST `/v1.0/api/prompt-templates/{key}/reset`

Reset `?layer=Tenant` to the Global default (the override is deactivated and its history kept), or `?layer=Global` to the Built-in (system administrators; adopts a changed Built-in).

**Authentication:** Any REST scheme. Permission: Update on PromptTemplate.

| Parameter | In | Type | Description |
|---|---|---|---|
| `key` | path | string | Path parameter. |
| `layer` | query | enum | Tenant (default) resets to Global; Global resets to Built-in. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `key`, `description`, `source`, `activeLayer`, `effectiveContent`, `builtInContent`, `builtInHash`, `global`, `tenant`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/prompt-templates/persona.neutral/reset?layer=Tenant" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "key": "persona.neutral",
  "description": "A plain, non-themed persona.",
  "source": "hugo",
  "activeLayer": "Global",
  "effectiveContent": "You are {{profile.personaName | default: profile.displayName}}, an operations assistant for {{profile.userAddress}} in the \"{{tenant.name}}\" environment. Be direct, accurate, and brief.\n\nHelp manage, … (770 characters)",
  "builtInContent": "You are {{profile.personaName | default: profile.displayName}}, an operations assistant for {{profile.userAddress}} in the \"{{tenant.name}}\" environment. Be direct, accurate, and brief.\n\nHelp manage, … (770 characters)",
  "builtInHash": "58fa8babf8b017dee1314a656a03ff94171b58a172a7f8df2e1c5eec669b796c",
  "global": {
    "layer": "Global",
    "exists": true,
    "templateId": "tpl_mv92uyq6_bwBFGqdprnwOMgsxjJf",
    "content": "You are {{profile.personaName | default: profile.displayName}}, an operations assistant for {{profile.userAddress}} in the \"{{tenant.name}}\" environment. Be direct, accurate, and brief.\n\nHelp manage, … (770 characters)",
    "version": 1,
    "customized": false,
    "defaultUpdatedSinceCustomized": false,
    "lastUpdateUtc": "2026-10-15T05:07:42.030062Z"
  },
  "tenant": {
    "layer": "Tenant",
    "exists": false,
    "templateId": "tpl_mv92v2k5_thQvNGxfXyhQ6NT4MyO",
    "content": "You are {{profile.personaName}}, an operations agent for {{profile.userAddress}} in the {{tenant.name}} tenant. Be brief and cite the resource behind every finding.",
    "version": 10,
    "customized": false,
    "defaultUpdatedSinceCustomized": false,
    "lastUpdateUtc": "2026-10-15T05:17:49.210772Z"
  }
}
```

### GET `/v1.0/api/prompt-templates/{key}/versions`

Version history of the Tenant (default) or Global layer, newest first.

**Authentication:** Any REST scheme. Permission: Read on PromptTemplate.

| Parameter | In | Type | Description |
|---|---|---|---|
| `key` | path | string | Path parameter. |
| `layer` | query | enum | Tenant (default) or Global. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `[]id`, `[]tenantId`, `[]templateId`, `[]version`, `[]content`, `[]changeNote`, `[]createdBy`, `[]active`, `[]isProtected`, `[]createdUtc`, `[]lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/prompt-templates/persona.neutral/versions?layer=Tenant" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
[
  {
    "id": "tpv_mv937z70_evLYwysnwWvFlR46qn6",
    "tenantId": "default",
    "templateId": "tpl_mv92v2k5_thQvNGxfXyhQ6NT4MyO",
    "version": 9,
    "content": "You are {{profile.personaName}}, an operations agent for {{profile.userAddress}} in the {{tenant.name}} tenant. Be brief and cite the resource behind every finding.",
    "changeNote": "Shorter neutral persona",
    "createdBy": "anderson@hugo.local",
    "active": true,
    "isProtected": false,
    "createdUtc": "2026-10-15T05:17:49.164154Z",
    "lastUpdateUtc": "2026-10-15T05:17:49.164154Z"
  },
  {
    "id": "tpv_mv930miw_JNHEYeDmqeJijRmtmBq",
    "tenantId": "default",
    "templateId": "tpl_mv92v2k5_thQvNGxfXyhQ6NT4MyO",
    "version": 8,
    "content": "You are {{profile.personaName}}, an operations agent for {{profile.userAddress}} in the {{tenant.name}} tenant. Be brief and cite the resource behind every finding.",
    "changeNote": "Reset to the Global default.",
    "createdBy": "anderson@hugo.local",
    "active": true,
    "isProtected": false,
    "createdUtc": "2026-10-15T05:12:06.152606Z",
    "lastUpdateUtc": "2026-10-15T05:12:06.152606Z"
  },
  "… 7 more"
]
```

### GET `/v1.0/api/prompt-templates/{key}/builtin`

The shipped content of a key and its hash.

**Authentication:** Any REST scheme. Permission: Read on PromptTemplate.

| Parameter | In | Type | Description |
|---|---|---|---|
| `key` | path | string | Path parameter. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `key`, `content`, `hash`, `source`, `description`.

Example:

```bash
curl -s "$HUGO/v1.0/api/prompt-templates/persona.neutral/builtin" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "key": "persona.neutral",
  "content": "You are {{profile.personaName | default: profile.displayName}}, an operations assistant for {{profile.userAddress}} in the \"{{tenant.name}}\" environment. Be direct, accurate, and brief.\n\nHelp manage, … (770 characters)",
  "hash": "58fa8babf8b017dee1314a656a03ff94171b58a172a7f8df2e1c5eec669b796c",
  "source": "hugo",
  "description": "A plain, non-themed persona."
}
```

### GET `/v1.0/api/prompt-templates/{key}/diff`

Line diff of a layer against the layer below it.

**Authentication:** Any REST scheme. Permission: Read on PromptTemplate.

| Parameter | In | Type | Description |
|---|---|---|---|
| `key` | path | string | Path parameter. |
| `layer` | query | enum | Tenant (default) or Global. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `[]op`, `[]text`.

Example:

```bash
curl -s "$HUGO/v1.0/api/prompt-templates/persona.neutral/diff?layer=Tenant" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
[
  {
    "op": "remove",
    "text": "You are {{profile.personaName | default: profile.displayName}}, an operations assistant for {{profile.userAddress}} in the \"{{tenant.name}}\" environment. Be direct, accurate, and brief."
  },
  {
    "op": "remove",
    "text": ""
  },
  "… 7 more"
]
```

## Model endpoints

Model endpoints are the inference and embedding providers Hugo's own loop calls: Ollama, OpenAI, OpenAI-compatible servers, Anthropic, Gemini, Azure OpenAI, Bedrock, Vertex, and VoyageAI (embeddings). API keys stay in the vault and are referenced by secret ID. A profile needs an inference endpoint before dashboard chat, A2A, the OpenAI-compatible surface, or `ask_agent_smith` will run; MCP callers bring their own model and need none.

`validate` sends a tiny prompt and records latency and health. Treat its `success` field, not its status code, as the answer: an unreachable provider is a successful validation request that reports failure.

| Method | Path | Summary |
|---|---|---|
| GET | `/v1.0/api/model-endpoints` | List model endpoints |
| POST | `/v1.0/api/model-endpoints` | Create a model endpoint (API key as a vault secret reference) |
| GET | `/v1.0/api/model-endpoints/{id}` | Read a model endpoint |
| PUT | `/v1.0/api/model-endpoints/{id}` | Update a model endpoint |
| DELETE | `/v1.0/api/model-endpoints/{id}` | Delete a model endpoint (profiles lose the reference) |
| POST | `/v1.0/api/model-endpoints/{id}/validate` | Send a tiny prompt (or embed a short string), record latency, dimensions, and health |
| GET | `/v1.0/api/model-endpoints/{id}/models` | Models the provider offers (Ollama, OpenAI, OpenAI-compatible, Anthropic, Gemini) |

### GET `/v1.0/api/model-endpoints`

Model endpoints in the tenant with their last health result.

**Authentication:** Any REST scheme. Permission: Read on ModelEndpoint.

| Parameter | In | Type | Description |
|---|---|---|---|
| `maxResults` | query | integer | 1 to 1000. Default 25. |
| `skip` | query | integer | Records to skip. Default 0. |
| `ordering` | query | enum | CreatedDescending (default), CreatedAscending, NameAscending, NameDescending. |
| `search` | query | string | Text to match. |
| `activeOnly` | query | boolean | Only active records. Default false. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden.

**Response fields:** `success`, `maxResults`, `skip`, `totalRecords`, `recordsRemaining`, `endOfResults`, `timestampUtc`, `objects`, `objects[].id`, `objects[].tenantId`, `objects[].name`, `objects[].kind`, `objects[].provider`, `objects[].baseUrl`, `objects[].model`, `objects[].apiKeySecretId`, `objects[].apiKeySecretField`, `objects[].region`, `objects[].deploymentName`, `objects[].apiVersion`, `objects[].contextWindowTokens`, `objects[].maxOutputTokens`, `objects[].supportsTools`, `objects[].supportsStreaming`, `objects[].embeddingDimensions`, `objects[].maxConcurrency`, `objects[].timeoutSeconds`, `objects[].maxRetries`, `objects[].healthStatus`, `objects[].lastHealthUtc`, `objects[].lastHealthMessage`, `objects[].active`, `objects[].createdUtc`, `objects[].lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/model-endpoints" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "success": true,
  "maxResults": 25,
  "skip": 0,
  "totalRecords": 1,
  "recordsRemaining": 0,
  "endOfResults": true,
  "timestampUtc": "2026-10-15T05:17:48.317895Z",
  "objects": [
    {
      "id": "mep_mv937yj6_RbhrM8KfcgiBVTuL8o2",
      "tenantId": "default",
      "name": "local-model",
      "kind": "Inference",
      "provider": "OpenAICompatible",
      "baseUrl": "http://127.0.0.1:63806",
      "model": "stub-model",
      "contextWindowTokens": 32768,
      "maxOutputTokens": 4096,
      "supportsTools": true,
      "supportsStreaming": true,
      "maxConcurrency": 4,
      "timeoutSeconds": 120,
      "maxRetries": 2,
      "healthStatus": "Unknown",
      "active": true,
      "createdUtc": "2026-10-15T05:17:48.30602Z",
      "lastUpdateUtc": "2026-10-15T05:17:48.30602Z"
    }
  ]
}
```

### POST `/v1.0/api/model-endpoints`

Create an inference endpoint. Provider is one of Ollama, OpenAI, OpenAICompatible, Anthropic, Gemini, AzureOpenAI, Bedrock, VertexAI, VoyageAI. API keys are referenced as vault secrets (`apiKeySecretId`, `apiKeySecretField`), never stored on the endpoint.

**Authentication:** Any REST scheme. Permission: Create on ModelEndpoint.

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `name` | string | **Required.** Unique within the tenant (on create). |
| `kind` | enum | Inference (default) or Embedding. Values: Inference, Embedding. |
| `provider` | enum | **Required.** Ollama, OpenAI, OpenAICompatible, Anthropic, Gemini, AzureOpenAI, Bedrock, VertexAI, or VoyageAI. Values: Ollama, OpenAI, OpenAICompatible, Anthropic, Gemini, AzureOpenAI, Bedrock, VertexAI, VoyageAI. |
| `baseUrl` | string | Provider base URL without `/v1` (for example `http://127.0.0.1:11434`). Optional for hosted providers. |
| `model` | string | **Required.** Model name. |
| `apiKeySecretId` | string | Vault secret holding the API key. |
| `apiKeySecretField` | string | Field of that secret, when it has more than one. |
| `region` | string | Bedrock and Vertex region. |
| `deploymentName` | string | Azure OpenAI deployment. |
| `apiVersion` | string | Azure OpenAI API version. |
| `contextWindowTokens` | integer | Context window used for compaction decisions. Default 32768. |
| `maxOutputTokens` | integer | Default 4096. |
| `supportsTools` | boolean | Whether the model can call tools. |
| `supportsStreaming` | boolean | Whether to stream. |
| `embeddingDimensions` | integer | Embedding size (embedding endpoints). |
| `maxConcurrency` | integer | Concurrent calls. Default 4. |
| `timeoutSeconds` | integer | Default 120. |
| `maxRetries` | integer | Default 2. |
| `active` | boolean | Default true. |

**Status codes:** 201 Created, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 409 Conflict.

**Response fields:** `id`, `tenantId`, `name`, `kind`, `provider`, `baseUrl`, `model`, `apiKeySecretId`, `apiKeySecretField`, `region`, `deploymentName`, `apiVersion`, `contextWindowTokens`, `maxOutputTokens`, `supportsTools`, `supportsStreaming`, `embeddingDimensions`, `maxConcurrency`, `timeoutSeconds`, `maxRetries`, `healthStatus`, `lastHealthUtc`, `lastHealthMessage`, `active`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/model-endpoints" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name": "local-model", "kind": "Inference", "provider": "OpenAICompatible", "baseUrl": "http://127.0.0.1:63806", "model": "stub-model", "contextWindowTokens": 32768, "supportsTools": true, "supportsStreaming": true}'
```

Response (201):

```json
{
  "id": "mep_mv937yj6_RbhrM8KfcgiBVTuL8o2",
  "tenantId": "default",
  "name": "local-model",
  "kind": "Inference",
  "provider": "OpenAICompatible",
  "baseUrl": "http://127.0.0.1:63806",
  "model": "stub-model",
  "contextWindowTokens": 32768,
  "maxOutputTokens": 4096,
  "supportsTools": true,
  "supportsStreaming": true,
  "maxConcurrency": 4,
  "timeoutSeconds": 120,
  "maxRetries": 2,
  "healthStatus": "Unknown",
  "active": true,
  "createdUtc": "2026-10-15T05:17:48.30602Z",
  "lastUpdateUtc": "2026-10-15T05:17:48.30602Z"
}
```

### GET `/v1.0/api/model-endpoints/{id}`

One endpoint.

**Authentication:** Any REST scheme. Permission: Read on ModelEndpoint.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `name`, `kind`, `provider`, `baseUrl`, `model`, `apiKeySecretId`, `apiKeySecretField`, `region`, `deploymentName`, `apiVersion`, `contextWindowTokens`, `maxOutputTokens`, `supportsTools`, `supportsStreaming`, `embeddingDimensions`, `maxConcurrency`, `timeoutSeconds`, `maxRetries`, `healthStatus`, `lastHealthUtc`, `lastHealthMessage`, `active`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/model-endpoints/mep_mv937yj6_RbhrM8KfcgiBVTuL8o2" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "id": "mep_mv937yj6_RbhrM8KfcgiBVTuL8o2",
  "tenantId": "default",
  "name": "local-model",
  "kind": "Inference",
  "provider": "OpenAICompatible",
  "baseUrl": "http://127.0.0.1:63806",
  "model": "stub-model",
  "contextWindowTokens": 32768,
  "maxOutputTokens": 4096,
  "supportsTools": true,
  "supportsStreaming": true,
  "maxConcurrency": 4,
  "timeoutSeconds": 120,
  "maxRetries": 2,
  "healthStatus": "Unknown",
  "active": true,
  "createdUtc": "2026-10-15T05:17:48.30602Z",
  "lastUpdateUtc": "2026-10-15T05:17:48.30602Z"
}
```

### PUT `/v1.0/api/model-endpoints/{id}`

Partial update.

**Authentication:** Any REST scheme. Permission: Update on ModelEndpoint.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `name` | string | **Required.** Unique within the tenant (on create). |
| `kind` | enum | Inference (default) or Embedding. Values: Inference, Embedding. |
| `provider` | enum | **Required.** Ollama, OpenAI, OpenAICompatible, Anthropic, Gemini, AzureOpenAI, Bedrock, VertexAI, or VoyageAI. Values: Ollama, OpenAI, OpenAICompatible, Anthropic, Gemini, AzureOpenAI, Bedrock, VertexAI, VoyageAI. |
| `baseUrl` | string | Provider base URL without `/v1` (for example `http://127.0.0.1:11434`). Optional for hosted providers. |
| `model` | string | **Required.** Model name. |
| `apiKeySecretId` | string | Vault secret holding the API key. |
| `apiKeySecretField` | string | Field of that secret, when it has more than one. |
| `region` | string | Bedrock and Vertex region. |
| `deploymentName` | string | Azure OpenAI deployment. |
| `apiVersion` | string | Azure OpenAI API version. |
| `contextWindowTokens` | integer | Context window used for compaction decisions. Default 32768. |
| `maxOutputTokens` | integer | Default 4096. |
| `supportsTools` | boolean | Whether the model can call tools. |
| `supportsStreaming` | boolean | Whether to stream. |
| `embeddingDimensions` | integer | Embedding size (embedding endpoints). |
| `maxConcurrency` | integer | Concurrent calls. Default 4. |
| `timeoutSeconds` | integer | Default 120. |
| `maxRetries` | integer | Default 2. |
| `active` | boolean | Default true. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `name`, `kind`, `provider`, `baseUrl`, `model`, `apiKeySecretId`, `apiKeySecretField`, `region`, `deploymentName`, `apiVersion`, `contextWindowTokens`, `maxOutputTokens`, `supportsTools`, `supportsStreaming`, `embeddingDimensions`, `maxConcurrency`, `timeoutSeconds`, `maxRetries`, `healthStatus`, `lastHealthUtc`, `lastHealthMessage`, `active`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s -X PUT "$HUGO/v1.0/api/model-endpoints/mep_mv937yj6_RbhrM8KfcgiBVTuL8o2" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"maxConcurrency": 2, "timeoutSeconds": 90}'
```

Response (200):

```json
{
  "id": "mep_mv937yj6_RbhrM8KfcgiBVTuL8o2",
  "tenantId": "default",
  "name": "local-model",
  "kind": "Inference",
  "provider": "OpenAICompatible",
  "baseUrl": "http://127.0.0.1:63806",
  "model": "stub-model",
  "contextWindowTokens": 32768,
  "maxOutputTokens": 4096,
  "supportsTools": true,
  "supportsStreaming": true,
  "maxConcurrency": 2,
  "timeoutSeconds": 90,
  "maxRetries": 2,
  "healthStatus": "Unknown",
  "active": true,
  "createdUtc": "2026-10-15T05:17:48.30602Z",
  "lastUpdateUtc": "2026-10-15T05:17:48.340229Z"
}
```

### DELETE `/v1.0/api/model-endpoints/{id}`

Delete a model endpoint; profiles lose the reference.

**Authentication:** Any REST scheme. Permission: Delete on ModelEndpoint.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Status codes:** 204 No Content, 401 Not authenticated, 403 Forbidden, 404 Not found.

Example:

```bash
curl -s -X DELETE "$HUGO/v1.0/api/model-endpoints/mep_mv937yj6_RbhrM8KfcgiBVTuL8o2" \
  -H "Authorization: Bearer $TOKEN"
```

Response: 204 with an empty body.

### POST `/v1.0/api/model-endpoints/{id}/validate`

Send a tiny prompt (or embed a short string) and record latency, dimensions, and health. An unreachable provider is a 200 with `success: false` and the reason in `error` and `problems`.

**Authentication:** Any REST scheme. Permission: Update on ModelEndpoint.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `success`, `kind`, `latencyMs`, `dimensions`, `model`, `responsePreview`, `statusCode`, `error`, `problems`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/model-endpoints/mep_mv937yj6_RbhrM8KfcgiBVTuL8o2/validate" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "success": true,
  "kind": "Inference",
  "latencyMs": 0,
  "model": "stub-model",
  "responsePreview": "Good evening, Mr. Anderson. I am Agent Smith. Ask me about the resources bound t",
  "statusCode": 200,
  "problems": []
}
```

### GET `/v1.0/api/model-endpoints/{id}/models`

Models the provider offers (Ollama, OpenAI, OpenAI-compatible, Anthropic, Gemini). Known issue in 0.1.0-alpha: an unreachable provider answers 500 instead of 502.

**Authentication:** Any REST scheme. Permission: Read on ModelEndpoint.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

Example:

```bash
curl -s "$HUGO/v1.0/api/model-endpoints/mep_mv937yj6_RbhrM8KfcgiBVTuL8o2/models" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
[
  "stub-model"
]
```

## Tool invocations

Every tool call from every surface goes through one pipeline: resolve the profile and binding, authorize, validate arguments against the tool schema, classify the effect (Read, Write, or Destructive) for this specific call, apply tool rules, deny patterns, and the access mode, wait for approval when needed, execute with limits, scrub secrets from the output, and record the result. The tool invocation routes expose that pipeline.

`POST /v1.0/api/tool-invocations` is how SDKs and scripts call tools as a profile. `POST /v1.0/api/tool-invocations/dry-run` runs everything except execution and recording, which makes it the safest way to ask "what would happen if an agent tried this?". The list and summary routes read the audit trail of calls; arguments are stored redacted.

| Method | Path | Summary |
|---|---|---|
| GET | `/v1.0/api/tool-invocations` | List tool invocations (filters: agentProfileId, resourceId, toolName, decision, status, surface, conversationId, principalId, since) |
| POST | `/v1.0/api/tool-invocations` | Invoke a tool as a profile through the full pipeline; 202 with the approval while approval is pending |
| GET | `/v1.0/api/tool-invocations/summary` | Counts by decision, status, effect, and tool (optional since) |
| POST | `/v1.0/api/tool-invocations/dry-run` | Run resolution, authorization, validation, classification, and rules only, and return the decision (nothing is recorded or executed) |
| GET | `/v1.0/api/tool-invocations/{id}` | Read a tool invocation |

### GET `/v1.0/api/tool-invocations`

Invocation records, newest first. Filters: `agentProfileId`, `resourceId`, `toolName`, `decision`, `status`, `surface`, `conversationId`, `principalId`, `since`.

**Authentication:** Any REST scheme. Permission: Read on ToolInvocation.

| Parameter | In | Type | Description |
|---|---|---|---|
| `agentProfileId` | query | string | Only this profile. |
| `resourceId` | query | string | Only this resource. |
| `toolName` | query | string | Only this tool. |
| `decision` | query | enum | Allowed, Denied, ApprovalRequired, Approved, Rejected, Expired, Cancelled. |
| `status` | query | enum | Pending, Running, Succeeded, Failed, TimedOut, Cancelled, NotExecuted. |
| `surface` | query | enum | Chat, Mcp, A2A, OpenAI, Rest. |
| `conversationId` | query | string | Only this conversation. |
| `principalId` | query | string | Only this principal. |
| `since` | query | date-time | Only records created at or after this time (ISO 8601 UTC). |
| `maxResults` | query | integer | 1 to 1000. Default 25. |
| `skip` | query | integer | Records to skip. Default 0. |
| `ordering` | query | enum | CreatedDescending (default), CreatedAscending, NameAscending, NameDescending. |
| `search` | query | string | Text to match. |
| `activeOnly` | query | boolean | Only active records. Default false. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden.

**Response fields:** `success`, `maxResults`, `skip`, `totalRecords`, `recordsRemaining`, `endOfResults`, `timestampUtc`, `objects`, `objects[].id`, `objects[].tenantId`, `objects[].conversationId`, `objects[].messageId`, `objects[].surface`, `objects[].principalType`, `objects[].principalId`, `objects[].principalName`, `objects[].agentProfileId`, `objects[].bindingId`, `objects[].bindingAlias`, `objects[].resourceId`, `objects[].connectorType`, `objects[].toolName`, `objects[].argumentsRedacted`, `objects[].redactionApplied`, `objects[].effect`, `objects[].effectReason`, `objects[].accessMode`, `objects[].decision`, `objects[].decisionCode`, `objects[].decisionReason`, `objects[].approvalId`, `objects[].status`, `objects[].errorCode`, `objects[].errorMessage`, `objects[].outputBytes`, `objects[].outputTruncated`, `objects[].outputPreview`, `objects[].durationMs`, `objects[].traceId`, `objects[].correlationId`, `objects[].createdUtc`, `objects[].lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/tool-invocations?toolName=sql_query&maxResults=10" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```text
{
  "success": true,
  "maxResults": 10,
  "skip": 0,
  "totalRecords": 42,
  "recordsRemaining": 32,
  "endOfResults": false,
  "timestampUtc": "2026-10-15T05:17:48.279366Z",
  "objects": [
    {
      "id": "tiv_mv937yhp_90sUCNwh7zrR2pY2WAm",
      "tenantId": "default",
      "surface": "Rest",
      "principalType": "User",
      "principalId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
      "principalName": "anderson@hugo.local",
      "agentProfileId": "agp_mv937xml_XArgAHQfG5v6uUkrkbl",
      "bindingId": "bnd_mv937xok_kfPjNftrNgz1vLdpehn",
      "bindingAlias": "demo-db",
      "resourceId": "res_mv937xm6_qFsDalpjfsqe3DAHONT",
      "connectorType": "sqlite",
      "toolName": "sql_query",
      "argumentsRedacted": "{\n    \"resource\": \"demo-db\",\n    \"sql\": \"DROP TABLE notes\"\n  }",
      "redactionApplied": false,
      "effect": "Destructive",
      "effectReason": "statement type DROP",
      "accessMode": "ReadWriteWithApproval",
      "decision": "Cancelled",
      "decisionCode": "cancelled",
      "decisionReason": "The approval was cancelled: Requested by mistake.",
      "approvalId": "apr_mv937yhq_zc4il02n4bLIC9m6MVV",
      "status": "NotExecuted",
      "outputBytes": 0,
      "outputTruncated": false,
      "durationMs": 0,
      "traceId": "4b1fc50e280f76bbe5892f6fbef70331",
      "correlationId": "e01d226bcad54ebf9d49aa9b86ad1ae0",
      "createdUtc": "2026-10-15T05:17:48.254364Z",
      "lastUpdateUtc": "2026-10-15T05:17:48.266701Z"
    },
    {
      "id": "tiv_mv937yh0_ggpZA4lnIa4kjG1QQfc",
      "tenantId": "default",
      "surface": "Rest",
      "principalType": "User",
      "principalId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
      "principalName": "anderson@hugo.local",
      "agentProfileId": "agp_mv937xml_XArgAHQfG5v6uUkrkbl",
      "bindingId": "bnd_mv937xok_kfPjNftrNgz1vLdpehn",
      "bindingAlias": "demo-db",
      "resourceId": "res_mv937xm6_qFsDalpjfsqe3DAHONT",
      "connectorType": "sqlite",
      "toolName": "sql_query",
      "argumentsRedacted": "{\n    \"resource\": \"demo-db\",\n    \"sql\": \"DELETE FROM notes\"\n  }",
      "redactionApplied": false,
…
```

### POST `/v1.0/api/tool-invocations`

Run `sql_query` on `demo-db` as the `Hugo` profile. Name the profile with `profile` (name) or `profileId`; when neither is given, the caller's default profile applies (D-018). The resource alias goes in `arguments.resource` (or `resource`).

**Authentication:** Any authenticated principal.

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `profileId` | string | Profile to act as, by ID. |
| `profile` | string | Profile to act as, by name. With neither, the caller's default profile applies (D-018). Ignored by `/resources/{id}/invoke`. |
| `resource` | string | Binding alias. May also be passed as `arguments.resource`. |
| `resourceId` | string | Resource ID instead of the alias. |
| `toolName` | string | **Required.** Tool name, for example `sql_query`. |
| `arguments` | object | Tool arguments as a JSON object, validated against the tool input schema. Any JSON value. |

**Status codes:** 200 OK, 202 Accepted (approval pending or work started), 400 Validation failed, 401 Not authenticated, 403 Forbidden.

**Response fields:** `invocationId`, `dryRun`, `toolName`, `profileId`, `bindingAlias`, `resourceId`, `connectorType`, `decision`, `decisionCode`, `reason`, `effect`, `effectReason`, `accessMode`, `approvalId`, `approvalStatus`, `approvalSummary`, `approvalExpiresUtc`, `status`, `result`, `durationMs`, `approvalWaitMs`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/tool-invocations" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"profile": "Hugo", "toolName": "sql_query", "arguments": {"resource": "demo-db", "sql": "SELECT '\''Mr. Anderson'\'' AS addressee, sqlite_version() AS engine"}}'
```

Response (200):

```json
{
  "invocationId": "tiv_mv937yek_wNDQyL0oshngJNw80tk",
  "dryRun": false,
  "toolName": "sql_query",
  "profileId": "agp_mv92uyqb_LM9EdE7i2KwfKe6GfaO",
  "bindingAlias": "demo-db",
  "resourceId": "res_mv937xm6_qFsDalpjfsqe3DAHONT",
  "connectorType": "sqlite",
  "decision": "Allowed",
  "decisionCode": "allowed",
  "reason": "Read call.",
  "effect": "Read",
  "effectReason": "statement type SELECT",
  "accessMode": "ReadOnly",
  "approvalSummary": "Run SQL on demo-db: SELECT 'Mr. Anderson' AS addressee, sqlite_version() AS engine",
  "status": "Succeeded",
  "result": {
    "isError": false,
    "text": "1 row in 0.03 ms.\n| addressee | engine |\n| --- | --- |\n| Mr. Anderson | 3.53.3 |\n",
    "structuredJson": "{\"statementType\":\"SELECT\",\"columns\":[{\"name\":\"addressee\",\"type\":\"TEXT\"},{\"name\":\"engine\",\"type\":\"TEXT\"}],\"rows\":[[\"Mr. Anderson\",\"3.53.3\"]],\"rowCount\":1,\"rowLimit\":500,\"truncated\":false,\"elapsedMs\":0.03}",
    "mimeType": "text/plain",
    "truncated": false,
    "originalBytes": 81,
    "metrics": {
      "rows": 1,
      "upstreamMs": 0.11
    }
  },
  "durationMs": 0,
  "approvalWaitMs": 0
}
```

### GET `/v1.0/api/tool-invocations/summary`

Counts by decision, status, effect, and tool, optionally since a time.

**Authentication:** Any REST scheme. Permission: Read on ToolInvocation.

| Parameter | In | Type | Description |
|---|---|---|---|
| `since` | query | date-time | Count records created at or after this time. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden.

**Response fields:** `total`, `sinceUtc`, `byDecision`, `byStatus`, `byEffect`, `byTool`.

Example:

```bash
curl -s "$HUGO/v1.0/api/tool-invocations/summary?since=2026-01-01T00:00:00Z" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "total": 54,
  "sinceUtc": "2026-01-01T00:00:00Z",
  "byDecision": [
    {
      "status": "Allowed",
      "count": 26
    },
    {
      "status": "Denied",
      "count": 13
    },
    "… 3 more"
  ],
  "byStatus": [
    {
      "status": "Succeeded",
      "count": 31
    },
    {
      "status": "NotExecuted",
      "count": 23
    }
  ],
  "byEffect": [
    {
      "status": "Read",
      "count": 26
    },
    {
      "status": "Write",
      "count": 18
    },
    {
      "status": "Destructive",
      "count": 10
    }
  ],
  "byTool": [
    {
      "status": "sql_query",
      "count": 42
    },
    {
      "status": "sql_list_tables",
      "count": 12
    }
  ]
}
```

### POST `/v1.0/api/tool-invocations/dry-run`

What Hugo would decide for `ssh_exec` on `web-01` under ReadWriteWithApproval. The response carries the decision, the classified effect and why, the access mode, and the decision code; nothing runs.

**Authentication:** Any authenticated principal.

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `profileId` | string | Profile to act as, by ID. |
| `profile` | string | Profile to act as, by name. With neither, the caller's default profile applies (D-018). Ignored by `/resources/{id}/invoke`. |
| `resource` | string | Binding alias. May also be passed as `arguments.resource`. |
| `resourceId` | string | Resource ID instead of the alias. |
| `toolName` | string | **Required.** Tool name, for example `sql_query`. |
| `arguments` | object | Tool arguments as a JSON object, validated against the tool input schema. Any JSON value. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden.

**Response fields:** `invocationId`, `dryRun`, `toolName`, `profileId`, `bindingAlias`, `resourceId`, `connectorType`, `decision`, `decisionCode`, `reason`, `effect`, `effectReason`, `accessMode`, `approvalId`, `approvalStatus`, `approvalSummary`, `approvalExpiresUtc`, `status`, `result`, `durationMs`, `approvalWaitMs`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/tool-invocations/dry-run" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"profile": "Oncall", "toolName": "ssh_exec", "arguments": {"command": "systemctl restart nginx", "resource": "web-01"}}'
```

Response (200):

```json
{
  "dryRun": true,
  "toolName": "ssh_exec",
  "profileId": "agp_mv937xml_XArgAHQfG5v6uUkrkbl",
  "bindingAlias": "web-01",
  "resourceId": "res_mv937xzb_h6cEk5plT1Y0XSPD30i",
  "connectorType": "ssh",
  "decision": "ApprovalRequired",
  "decisionCode": "approval_required",
  "reason": "Destructive calls on this resource require approval (systemctl restart stops or disrupts services).",
  "effect": "Destructive",
  "effectReason": "systemctl restart stops or disrupts services",
  "accessMode": "ReadWriteWithApproval",
  "approvalSummary": "Run \"systemctl restart nginx\" on web-01",
  "status": "NotExecuted",
  "durationMs": 0,
  "approvalWaitMs": 0
}
```

### GET `/v1.0/api/tool-invocations/{id}`

One invocation record: principal, profile, binding, redacted arguments, effect and reason, access mode, decision, status, timings, output size, and trace ID.

**Authentication:** Any REST scheme. Permission: Read on ToolInvocation.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `conversationId`, `messageId`, `surface`, `principalType`, `principalId`, `principalName`, `agentProfileId`, `bindingId`, `bindingAlias`, `resourceId`, `connectorType`, `toolName`, `argumentsRedacted`, `redactionApplied`, `effect`, `effectReason`, `accessMode`, `decision`, `decisionCode`, `decisionReason`, `approvalId`, `status`, `errorCode`, `errorMessage`, `outputBytes`, `outputTruncated`, `outputPreview`, `durationMs`, `traceId`, `correlationId`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/tool-invocations/tiv_mv937yf9_19yL1ZqeBR70iAfeIc6" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "id": "tiv_mv937yf9_19yL1ZqeBR70iAfeIc6",
  "tenantId": "default",
  "surface": "Rest",
  "principalType": "User",
  "principalId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "principalName": "anderson@hugo.local",
  "agentProfileId": "agp_mv937xml_XArgAHQfG5v6uUkrkbl",
  "bindingId": "bnd_mv937xok_kfPjNftrNgz1vLdpehn",
  "bindingAlias": "demo-db",
  "resourceId": "res_mv937xm6_qFsDalpjfsqe3DAHONT",
  "connectorType": "sqlite",
  "toolName": "sql_query",
  "argumentsRedacted": "{\n    \"resource\": \"demo-db\",\n    \"sql\": \"CREATE TABLE notes (id INTEGER)\"\n  }",
  "redactionApplied": false,
  "effect": "Write",
  "effectReason": "statement type CREATE",
  "accessMode": "ReadWriteWithApproval",
  "decision": "Approved",
  "decisionCode": "approval_required",
  "decisionReason": "Write calls on this resource require approval (statement type CREATE).",
  "approvalId": "apr_mv937yfa_HRLgbiJodej1rcJ618J",
  "status": "Succeeded",
  "outputBytes": 50,
  "outputTruncated": false,
  "outputPreview": "Statement executed; 0 row(s) affected in 0.04 ms.\n",
  "durationMs": 0,
  "traceId": "6c150ce50ac19470dde82fc1c8b494b7",
  "correlationId": "02c62f8757524ef889091115d356f685",
  "createdUtc": "2026-10-15T05:17:48.165963Z",
  "lastUpdateUtc": "2026-10-15T05:17:48.204729Z"
}
```

## Approvals

When the pipeline decides a call needs a human, it creates an approval and the requesting surface waits in its own way: REST returns 202, MCP blocks for `Approvals.McpWaitSeconds` and then returns `approval_pending`, chat emits `approval.required`, and A2A moves the task to input-required. Approvals expire after `Approvals.ExpirySeconds` (900).

Deciding needs Update on Approval. Self-approval is allowed by default, because the typical Hugo user is one operator approving their own agent; set `Approvals.AllowSelfApproval` to false for four-eyes. Calls made over A2A can never be approved by the same principal, whatever that setting says. When nobody is waiting on an approved call (the REST caller got its 202 and left), approving it runs the call before the response returns.

| Method | Path | Summary |
|---|---|---|
| GET | `/v1.0/api/approvals` | List approvals (filters: status, agentProfileId, resourceId, principalId, toolName) |
| GET | `/v1.0/api/approvals/{id}` | Read an approval with its tool invocation (and result once executed) |
| POST | `/v1.0/api/approvals/{id}/approve` | Approve a pending call; when nobody is waiting, the call runs before the response returns |
| POST | `/v1.0/api/approvals/{id}/reject` | Reject a pending call |
| POST | `/v1.0/api/approvals/{id}/cancel` | Cancel a pending call (the requester, or Update on Approval) |

### GET `/v1.0/api/approvals`

Approvals in the tenant, newest first. Filters: `status`, `agentProfileId`, `resourceId`, `principalId`, `toolName`.

**Authentication:** Any REST scheme. Permission: Read on Approval.

| Parameter | In | Type | Description |
|---|---|---|---|
| `status` | query | enum | Pending, Approved, Rejected, Expired, or Cancelled. |
| `agentProfileId` | query | string | Only this profile. |
| `resourceId` | query | string | Only this resource. |
| `principalId` | query | string | Only this requester. |
| `toolName` | query | string | Only this tool. |
| `maxResults` | query | integer | 1 to 1000. Default 25. |
| `skip` | query | integer | Records to skip. Default 0. |
| `ordering` | query | enum | CreatedDescending (default), CreatedAscending, NameAscending, NameDescending. |
| `search` | query | string | Text to match. |
| `activeOnly` | query | boolean | Only active records. Default false. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden.

**Response fields:** `success`, `maxResults`, `skip`, `totalRecords`, `recordsRemaining`, `endOfResults`, `timestampUtc`, `objects`, `objects[].id`, `objects[].tenantId`, `objects[].toolInvocationId`, `objects[].agentProfileId`, `objects[].bindingId`, `objects[].bindingAlias`, `objects[].resourceId`, `objects[].toolName`, `objects[].surface`, `objects[].requestedByPrincipalType`, `objects[].requestedByPrincipalId`, `objects[].requestedByName`, `objects[].summary`, `objects[].effect`, `objects[].effectReason`, `objects[].argumentsRedacted`, `objects[].status`, `objects[].decidedByUserId`, `objects[].decidedByName`, `objects[].decisionNote`, `objects[].decidedUtc`, `objects[].expiresUtc`, `objects[].invocation`, `objects[].createdUtc`, `objects[].lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/approvals?status=Pending" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "success": true,
  "maxResults": 25,
  "skip": 0,
  "totalRecords": 1,
  "recordsRemaining": 0,
  "endOfResults": true,
  "timestampUtc": "2026-10-15T05:17:48.178306Z",
  "objects": [
    {
      "id": "apr_mv937yfa_HRLgbiJodej1rcJ618J",
      "tenantId": "default",
      "toolInvocationId": "tiv_mv937yf9_19yL1ZqeBR70iAfeIc6",
      "agentProfileId": "agp_mv937xml_XArgAHQfG5v6uUkrkbl",
      "bindingId": "bnd_mv937xok_kfPjNftrNgz1vLdpehn",
      "bindingAlias": "demo-db",
      "resourceId": "res_mv937xm6_qFsDalpjfsqe3DAHONT",
      "toolName": "sql_query",
      "surface": "Rest",
      "requestedByPrincipalType": "User",
      "requestedByPrincipalId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
      "requestedByName": "anderson@hugo.local",
      "summary": "Run SQL on demo-db: CREATE TABLE notes (id INTEGER)",
      "effect": "Write",
      "effectReason": "Write calls on this resource require approval (statement type CREATE).",
      "argumentsRedacted": "{\n    \"resource\": \"demo-db\",\n    \"sql\": \"CREATE TABLE notes (id INTEGER)\"\n  }",
      "status": "Pending",
      "expiresUtc": "2026-10-15T05:32:48.166026Z",
      "createdUtc": "2026-10-15T05:17:48.166027Z",
      "lastUpdateUtc": "2026-10-15T05:17:48.166027Z"
    }
  ]
}
```

### GET `/v1.0/api/approvals/{id}`

One approval with its tool invocation, and the result once executed.

**Authentication:** Any REST scheme. Permission: Read on Approval.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `toolInvocationId`, `agentProfileId`, `bindingId`, `bindingAlias`, `resourceId`, `toolName`, `surface`, `requestedByPrincipalType`, `requestedByPrincipalId`, `requestedByName`, `summary`, `effect`, `effectReason`, `argumentsRedacted`, `status`, `decidedByUserId`, `decidedByName`, `decisionNote`, `decidedUtc`, `expiresUtc`, `invocation`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/approvals/apr_mv937yfa_HRLgbiJodej1rcJ618J" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "id": "apr_mv937yfa_HRLgbiJodej1rcJ618J",
  "tenantId": "default",
  "toolInvocationId": "tiv_mv937yf9_19yL1ZqeBR70iAfeIc6",
  "agentProfileId": "agp_mv937xml_XArgAHQfG5v6uUkrkbl",
  "bindingId": "bnd_mv937xok_kfPjNftrNgz1vLdpehn",
  "bindingAlias": "demo-db",
  "resourceId": "res_mv937xm6_qFsDalpjfsqe3DAHONT",
  "toolName": "sql_query",
  "surface": "Rest",
  "requestedByPrincipalType": "User",
  "requestedByPrincipalId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "requestedByName": "anderson@hugo.local",
  "summary": "Run SQL on demo-db: CREATE TABLE notes (id INTEGER)",
  "effect": "Write",
  "effectReason": "Write calls on this resource require approval (statement type CREATE).",
  "argumentsRedacted": "{\n    \"resource\": \"demo-db\",\n    \"sql\": \"CREATE TABLE notes (id INTEGER)\"\n  }",
  "status": "Pending",
  "expiresUtc": "2026-10-15T05:32:48.166026Z",
  "invocation": {
    "id": "tiv_mv937yf9_19yL1ZqeBR70iAfeIc6",
    "tenantId": "default",
    "surface": "Rest",
    "principalType": "User",
    "principalId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
    "principalName": "anderson@hugo.local",
    "agentProfileId": "agp_mv937xml_XArgAHQfG5v6uUkrkbl",
    "bindingId": "bnd_mv937xok_kfPjNftrNgz1vLdpehn",
    "bindingAlias": "demo-db",
    "resourceId": "res_mv937xm6_qFsDalpjfsqe3DAHONT",
    "connectorType": "sqlite",
    "toolName": "sql_query",
    "argumentsRedacted": "{\n    \"resource\": \"demo-db\",\n    \"sql\": \"CREATE TABLE notes (id INTEGER)\"\n  }",
    "redactionApplied": false,
    "effect": "Write",
    "effectReason": "statement type CREATE",
    "accessMode": "ReadWriteWithApproval",
    "decision": "ApprovalRequired",
    "decisionCode": "approval_required",
    "decisionReason": "Write calls on this resource require approval (statement type CREATE).",
    "approvalId": "apr_mv937yfa_HRLgbiJodej1rcJ618J",
    "status": "Pending",
    "outputBytes": 0,
    "outputTruncated": false,
    "durationMs": 0,
    "traceId": "6c150ce50ac19470dde82fc1c8b494b7",
    "correlationId": "02c62f8757524ef889091115d356f685",
    "createdUtc": "2026-10-15T05:17:48.165963Z",
    "lastUpdateUtc": "2026-10-15T05:17:48.166098Z"
  },
  "createdUtc": "2026-10-15T05:17:48.166027Z",
  "lastUpdateUtc": "2026-10-15T05:17:48.166027Z"
}
```

### POST `/v1.0/api/approvals/{id}/approve`

Approve a pending call. Nobody is waiting on this one (REST returned 202), so Hugo runs it now and the response carries the executed invocation. Needs Update on Approval; self-approval is allowed by default (`Approvals.AllowSelfApproval`), except for calls an A2A caller made.

**Authentication:** Any REST scheme. Permission: Update on Approval.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `note` | string | Optional note recorded with the decision. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found, 409 Conflict.

**Response fields:** `id`, `tenantId`, `toolInvocationId`, `agentProfileId`, `bindingId`, `bindingAlias`, `resourceId`, `toolName`, `surface`, `requestedByPrincipalType`, `requestedByPrincipalId`, `requestedByName`, `summary`, `effect`, `effectReason`, `argumentsRedacted`, `status`, `decidedByUserId`, `decidedByName`, `decisionNote`, `decidedUtc`, `expiresUtc`, `invocation`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/approvals/apr_mv937yfa_HRLgbiJodej1rcJ618J/approve" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"note": "Creating a scratch table on the demo database is fine."}'
```

Response (200):

```json
{
  "id": "apr_mv937yfa_HRLgbiJodej1rcJ618J",
  "tenantId": "default",
  "toolInvocationId": "tiv_mv937yf9_19yL1ZqeBR70iAfeIc6",
  "agentProfileId": "agp_mv937xml_XArgAHQfG5v6uUkrkbl",
  "bindingId": "bnd_mv937xok_kfPjNftrNgz1vLdpehn",
  "bindingAlias": "demo-db",
  "resourceId": "res_mv937xm6_qFsDalpjfsqe3DAHONT",
  "toolName": "sql_query",
  "surface": "Rest",
  "requestedByPrincipalType": "User",
  "requestedByPrincipalId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "requestedByName": "anderson@hugo.local",
  "summary": "Run SQL on demo-db: CREATE TABLE notes (id INTEGER)",
  "effect": "Write",
  "effectReason": "Write calls on this resource require approval (statement type CREATE).",
  "argumentsRedacted": "{\n    \"resource\": \"demo-db\",\n    \"sql\": \"CREATE TABLE notes (id INTEGER)\"\n  }",
  "status": "Approved",
  "decidedByUserId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "decidedByName": "anderson@hugo.local",
  "decisionNote": "Creating a scratch table on the demo database is fine.",
  "decidedUtc": "2026-10-15T05:17:48.203843Z",
  "expiresUtc": "2026-10-15T05:32:48.166026Z",
  "invocation": {
    "id": "tiv_mv937yf9_19yL1ZqeBR70iAfeIc6",
    "tenantId": "default",
    "surface": "Rest",
    "principalType": "User",
    "principalId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
    "principalName": "anderson@hugo.local",
    "agentProfileId": "agp_mv937xml_XArgAHQfG5v6uUkrkbl",
    "bindingId": "bnd_mv937xok_kfPjNftrNgz1vLdpehn",
    "bindingAlias": "demo-db",
    "resourceId": "res_mv937xm6_qFsDalpjfsqe3DAHONT",
    "connectorType": "sqlite",
    "toolName": "sql_query",
    "argumentsRedacted": "{\n    \"resource\": \"demo-db\",\n    \"sql\": \"CREATE TABLE notes (id INTEGER)\"\n  }",
    "redactionApplied": false,
    "effect": "Write",
    "effectReason": "statement type CREATE",
    "accessMode": "ReadWriteWithApproval",
    "decision": "Approved",
    "decisionCode": "approval_required",
    "decisionReason": "Write calls on this resource require approval (statement type CREATE).",
    "approvalId": "apr_mv937yfa_HRLgbiJodej1rcJ618J",
    "status": "Succeeded",
    "outputBytes": 50,
    "outputTruncated": false,
    "outputPreview": "Statement executed; 0 row(s) affected in 0.04 ms.\n",
    "durationMs": 0,
    "traceId": "6c150ce50ac19470dde82fc1c8b494b7",
    "correlationId": "02c62f8757524ef889091115d356f685",
    "createdUtc": "2026-10-15T05:17:48.165963Z",
    "lastUpdateUtc": "2026-10-15T05:17:48.204729Z"
  },
  "createdUtc": "2026-10-15T05:17:48.166027Z",
  "lastUpdateUtc": "2026-10-15T05:17:48.203843Z"
}
```

### POST `/v1.0/api/approvals/{id}/reject`

Reject a pending call. The invocation ends NotExecuted and the requester sees the rejection (MCP tool error, chat event, A2A task message).

**Authentication:** Any REST scheme. Permission: Update on Approval.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `note` | string | Optional note recorded with the decision. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found, 409 Conflict.

**Response fields:** `id`, `tenantId`, `toolInvocationId`, `agentProfileId`, `bindingId`, `bindingAlias`, `resourceId`, `toolName`, `surface`, `requestedByPrincipalType`, `requestedByPrincipalId`, `requestedByName`, `summary`, `effect`, `effectReason`, `argumentsRedacted`, `status`, `decidedByUserId`, `decidedByName`, `decisionNote`, `decidedUtc`, `expiresUtc`, `invocation`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/approvals/apr_mv937yh1_itxzoGmfwGK79Y0R2pP/reject" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"note": "Not during business hours."}'
```

Response (200):

```json
{
  "id": "apr_mv937yh1_itxzoGmfwGK79Y0R2pP",
  "tenantId": "default",
  "toolInvocationId": "tiv_mv937yh0_ggpZA4lnIa4kjG1QQfc",
  "agentProfileId": "agp_mv937xml_XArgAHQfG5v6uUkrkbl",
  "bindingId": "bnd_mv937xok_kfPjNftrNgz1vLdpehn",
  "bindingAlias": "demo-db",
  "resourceId": "res_mv937xm6_qFsDalpjfsqe3DAHONT",
  "toolName": "sql_query",
  "surface": "Rest",
  "requestedByPrincipalType": "User",
  "requestedByPrincipalId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "requestedByName": "anderson@hugo.local",
  "summary": "Run SQL on demo-db: DELETE FROM notes",
  "effect": "Destructive",
  "effectReason": "Destructive calls on this resource require approval (statement type DELETE).",
  "argumentsRedacted": "{\n    \"resource\": \"demo-db\",\n    \"sql\": \"DELETE FROM notes\"\n  }",
  "status": "Rejected",
  "decidedByUserId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "decidedByName": "anderson@hugo.local",
  "decisionNote": "Not during business hours.",
  "decidedUtc": "2026-10-15T05:17:48.241192Z",
  "expiresUtc": "2026-10-15T05:32:48.229174Z",
  "invocation": {
    "id": "tiv_mv937yh0_ggpZA4lnIa4kjG1QQfc",
    "tenantId": "default",
    "surface": "Rest",
    "principalType": "User",
    "principalId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
    "principalName": "anderson@hugo.local",
    "agentProfileId": "agp_mv937xml_XArgAHQfG5v6uUkrkbl",
    "bindingId": "bnd_mv937xok_kfPjNftrNgz1vLdpehn",
    "bindingAlias": "demo-db",
    "resourceId": "res_mv937xm6_qFsDalpjfsqe3DAHONT",
    "connectorType": "sqlite",
    "toolName": "sql_query",
    "argumentsRedacted": "{\n    \"resource\": \"demo-db\",\n    \"sql\": \"DELETE FROM notes\"\n  }",
    "redactionApplied": false,
    "effect": "Destructive",
    "effectReason": "statement type DELETE",
    "accessMode": "ReadWriteWithApproval",
    "decision": "Rejected",
    "decisionCode": "rejected",
    "decisionReason": "The approval was rejected by anderson@hugo.local: Not during business hours.",
    "approvalId": "apr_mv937yh1_itxzoGmfwGK79Y0R2pP",
    "status": "NotExecuted",
    "outputBytes": 0,
    "outputTruncated": false,
    "durationMs": 0,
    "traceId": "80cdf0c11ec15e74a7f8693a514e79db",
    "correlationId": "af875ff76fa24d1e91fb3f5ebcc439be",
    "createdUtc": "2026-10-15T05:17:48.229108Z",
    "lastUpdateUtc": "2026-10-15T05:17:48.241367Z"
  },
  "createdUtc": "2026-10-15T05:17:48.229176Z",
  "lastUpdateUtc": "2026-10-15T05:17:48.241192Z"
}
```

### POST `/v1.0/api/approvals/{id}/cancel`

Cancel a pending call. The requester may cancel its own; others need Update on Approval.

**Authentication:** Any authenticated principal.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `note` | string | Optional note recorded with the decision. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found, 409 Conflict.

**Response fields:** `id`, `tenantId`, `toolInvocationId`, `agentProfileId`, `bindingId`, `bindingAlias`, `resourceId`, `toolName`, `surface`, `requestedByPrincipalType`, `requestedByPrincipalId`, `requestedByName`, `summary`, `effect`, `effectReason`, `argumentsRedacted`, `status`, `decidedByUserId`, `decidedByName`, `decisionNote`, `decidedUtc`, `expiresUtc`, `invocation`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/approvals/apr_mv937yhq_zc4il02n4bLIC9m6MVV/cancel" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"note": "Requested by mistake."}'
```

Response (200):

```json
{
  "id": "apr_mv937yhq_zc4il02n4bLIC9m6MVV",
  "tenantId": "default",
  "toolInvocationId": "tiv_mv937yhp_90sUCNwh7zrR2pY2WAm",
  "agentProfileId": "agp_mv937xml_XArgAHQfG5v6uUkrkbl",
  "bindingId": "bnd_mv937xok_kfPjNftrNgz1vLdpehn",
  "bindingAlias": "demo-db",
  "resourceId": "res_mv937xm6_qFsDalpjfsqe3DAHONT",
  "toolName": "sql_query",
  "surface": "Rest",
  "requestedByPrincipalType": "User",
  "requestedByPrincipalId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "requestedByName": "anderson@hugo.local",
  "summary": "Run SQL on demo-db: DROP TABLE notes",
  "effect": "Destructive",
  "effectReason": "Destructive calls on this resource require approval (statement type DROP).",
  "argumentsRedacted": "{\n    \"resource\": \"demo-db\",\n    \"sql\": \"DROP TABLE notes\"\n  }",
  "status": "Cancelled",
  "decidedByUserId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "decidedByName": "anderson@hugo.local",
  "decisionNote": "Requested by mistake.",
  "decidedUtc": "2026-10-15T05:17:48.266502Z",
  "expiresUtc": "2026-10-15T05:32:48.254431Z",
  "invocation": {
    "id": "tiv_mv937yhp_90sUCNwh7zrR2pY2WAm",
    "tenantId": "default",
    "surface": "Rest",
    "principalType": "User",
    "principalId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
    "principalName": "anderson@hugo.local",
    "agentProfileId": "agp_mv937xml_XArgAHQfG5v6uUkrkbl",
    "bindingId": "bnd_mv937xok_kfPjNftrNgz1vLdpehn",
    "bindingAlias": "demo-db",
    "resourceId": "res_mv937xm6_qFsDalpjfsqe3DAHONT",
    "connectorType": "sqlite",
    "toolName": "sql_query",
    "argumentsRedacted": "{\n    \"resource\": \"demo-db\",\n    \"sql\": \"DROP TABLE notes\"\n  }",
    "redactionApplied": false,
    "effect": "Destructive",
    "effectReason": "statement type DROP",
    "accessMode": "ReadWriteWithApproval",
    "decision": "Cancelled",
    "decisionCode": "cancelled",
    "decisionReason": "The approval was cancelled: Requested by mistake.",
    "approvalId": "apr_mv937yhq_zc4il02n4bLIC9m6MVV",
    "status": "NotExecuted",
    "outputBytes": 0,
    "outputTruncated": false,
    "durationMs": 0,
    "traceId": "4b1fc50e280f76bbe5892f6fbef70331",
    "correlationId": "e01d226bcad54ebf9d49aa9b86ad1ae0",
    "createdUtc": "2026-10-15T05:17:48.254364Z",
    "lastUpdateUtc": "2026-10-15T05:17:48.266701Z"
  },
  "createdUtc": "2026-10-15T05:17:48.254432Z",
  "lastUpdateUtc": "2026-10-15T05:17:48.266502Z"
}
```

## Conversations and chat

Conversations are chat threads with a profile, and every surface that runs Hugo's own loop writes into them: the dashboard, A2A contexts, OpenAI-compatible sessions, and `ask`. Users see their own; tenant administrators see all of them in the tenant.

Sending a message runs one agent turn. With `Accept: text/event-stream` the turn streams as Server-Sent Events, each with an `id`, a name, and a JSON payload; otherwise the request blocks and returns the turn result as JSON. The event names are `turn.started`, `assistant.delta`, `assistant.thinking`, `tool.proposed`, `tool.started`, `tool.completed`, `approval.required`, `approval.resolved`, `turn.completed`, and `error`. One turn runs at a time per conversation; `cancel` stops it at the next await.

| Method | Path | Summary |
|---|---|---|
| GET | `/v1.0/api/conversations` | List the caller's conversations (tenant administrators see all; filters: agentProfileId, source, search) |
| POST | `/v1.0/api/conversations` | Start a conversation with an agent profile (default profile per D-018 when none is given) |
| GET | `/v1.0/api/conversations/{id}` | Read a conversation |
| PUT | `/v1.0/api/conversations/{id}` | Rename a conversation |
| DELETE | `/v1.0/api/conversations/{id}` | Delete a conversation and its messages (its owner, or Delete on Conversation) |
| GET | `/v1.0/api/conversations/{id}/messages` | List a conversation's messages in order (compacted ones included, with their tool calls) |
| POST | `/v1.0/api/conversations/{id}/messages` | Send a message and run a turn: Server-Sent Events (D-022 named events) with Accept: text/event-stream, otherwise the turn result as JSON. Also needs Execute on the agent profile |
| POST | `/v1.0/api/conversations/{id}/cancel` | Cancel the turn running in a conversation |
| GET | `/v1.0/api/conversations/{id}/export` | Export a conversation (format=md or json) |

### GET `/v1.0/api/conversations`

The caller's conversations; tenant administrators see all. Filters: `agentProfileId`, `source` (Dashboard, A2A, OpenAI, Mcp, Rest), `search`.

**Authentication:** Any REST scheme. Permission: Read on Conversation.

| Parameter | In | Type | Description |
|---|---|---|---|
| `agentProfileId` | query | string | Only this profile. |
| `source` | query | enum | Dashboard, A2A, OpenAI, Mcp, Rest. |
| `maxResults` | query | integer | 1 to 1000. Default 25. |
| `skip` | query | integer | Records to skip. Default 0. |
| `ordering` | query | enum | CreatedDescending (default), CreatedAscending, NameAscending, NameDescending. |
| `search` | query | string | Text to match. |
| `activeOnly` | query | boolean | Only active records. Default false. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden.

**Response fields:** `success`, `maxResults`, `skip`, `totalRecords`, `recordsRemaining`, `endOfResults`, `timestampUtc`, `objects`, `objects[].id`, `objects[].tenantId`, `objects[].userId`, `objects[].credentialId`, `objects[].agentProfileId`, `objects[].title`, `objects[].source`, `objects[].a2AContextId`, `objects[].externalKey`, `objects[].messageCount`, `objects[].lastMessageUtc`, `objects[].totalInputTokens`, `objects[].totalOutputTokens`, `objects[].createdUtc`, `objects[].lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/conversations" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "success": true,
  "maxResults": 25,
  "skip": 0,
  "totalRecords": 17,
  "recordsRemaining": 0,
  "endOfResults": true,
  "timestampUtc": "2026-10-15T05:17:48.405631Z",
  "objects": [
    {
      "id": "cnv_mv937yll_CMt9BzVpkZZ17oVKxTh",
      "tenantId": "default",
      "userId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
      "agentProfileId": "agp_mv92uyqb_LM9EdE7i2KwfKe6GfaO",
      "title": "Why is checkout slow?",
      "source": "Dashboard",
      "messageCount": 0,
      "totalInputTokens": 0,
      "totalOutputTokens": 0,
      "createdUtc": "2026-10-15T05:17:48.393462Z",
      "lastUpdateUtc": "2026-10-15T05:17:48.393462Z"
    },
    {
      "id": "cnv_mv930me7_xHPCX3roXEZbXwVgDLz",
      "tenantId": "default",
      "userId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
      "agentProfileId": "agp_mv92uyqb_LM9EdE7i2KwfKe6GfaO",
      "title": "Good evening, Mr. Anderson. I am Agent Smith. Ask me about the resources bound t...",
      "source": "A2A",
      "a2AContextId": "cnv_mv930me7_xHPCX3roXEZbXwVgDLz",
      "messageCount": 2,
      "lastMessageUtc": "2026-10-15T05:12:05.985857Z",
      "totalInputTokens": 3171,
      "totalOutputTokens": 46,
      "createdUtc": "2026-10-15T05:12:05.983211Z",
      "lastUpdateUtc": "2026-10-15T05:12:05.986584Z"
    },
    "… 15 more"
  ]
}
```

### POST `/v1.0/api/conversations`

Create a conversation. Without `agentProfileId`, the caller's default profile applies (D-018).

**Authentication:** Any REST scheme. Permission: Create on Conversation.

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `agentProfileId` | string | Profile to talk to (create only). Defaults per D-018. |
| `title` | string | Conversation title. |

**Status codes:** 201 Created, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 409 Conflict.

**Response fields:** `id`, `tenantId`, `userId`, `credentialId`, `agentProfileId`, `title`, `source`, `a2AContextId`, `externalKey`, `messageCount`, `lastMessageUtc`, `totalInputTokens`, `totalOutputTokens`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/conversations" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"agentProfileId": "agp_mv92uyqb_LM9EdE7i2KwfKe6GfaO", "title": "Why is checkout slow?"}'
```

Response (201):

```json
{
  "id": "cnv_mv937yll_CMt9BzVpkZZ17oVKxTh",
  "tenantId": "default",
  "userId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "agentProfileId": "agp_mv92uyqb_LM9EdE7i2KwfKe6GfaO",
  "title": "Why is checkout slow?",
  "source": "Dashboard",
  "messageCount": 0,
  "totalInputTokens": 0,
  "totalOutputTokens": 0,
  "createdUtc": "2026-10-15T05:17:48.393462Z",
  "lastUpdateUtc": "2026-10-15T05:17:48.393462Z"
}
```

### GET `/v1.0/api/conversations/{id}`

One conversation with message count and token totals.

**Authentication:** Any REST scheme. Permission: Read on Conversation.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `userId`, `credentialId`, `agentProfileId`, `title`, `source`, `a2AContextId`, `externalKey`, `messageCount`, `lastMessageUtc`, `totalInputTokens`, `totalOutputTokens`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/conversations/cnv_mv937yll_CMt9BzVpkZZ17oVKxTh" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "id": "cnv_mv937yll_CMt9BzVpkZZ17oVKxTh",
  "tenantId": "default",
  "userId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "agentProfileId": "agp_mv92uyqb_LM9EdE7i2KwfKe6GfaO",
  "title": "Why is checkout slow?",
  "source": "Dashboard",
  "messageCount": 0,
  "totalInputTokens": 0,
  "totalOutputTokens": 0,
  "createdUtc": "2026-10-15T05:17:48.393462Z",
  "lastUpdateUtc": "2026-10-15T05:17:48.393462Z"
}
```

### PUT `/v1.0/api/conversations/{id}`

Change the title.

**Authentication:** Any REST scheme. Permission: Update on Conversation.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `agentProfileId` | string | Profile to talk to (create only). Defaults per D-018. |
| `title` | string | Conversation title. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `userId`, `credentialId`, `agentProfileId`, `title`, `source`, `a2AContextId`, `externalKey`, `messageCount`, `lastMessageUtc`, `totalInputTokens`, `totalOutputTokens`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s -X PUT "$HUGO/v1.0/api/conversations/cnv_mv937yll_CMt9BzVpkZZ17oVKxTh" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"title": "Checkout latency, Thursday"}'
```

Response (200):

```json
{
  "id": "cnv_mv937yll_CMt9BzVpkZZ17oVKxTh",
  "tenantId": "default",
  "userId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "agentProfileId": "agp_mv92uyqb_LM9EdE7i2KwfKe6GfaO",
  "title": "Checkout latency, Thursday",
  "source": "Dashboard",
  "messageCount": 0,
  "totalInputTokens": 0,
  "totalOutputTokens": 0,
  "createdUtc": "2026-10-15T05:17:48.393462Z",
  "lastUpdateUtc": "2026-10-15T05:17:48.430805Z"
}
```

### DELETE `/v1.0/api/conversations/{id}`

Delete a conversation and its messages.

**Authentication:** Any REST scheme. The handler checks Delete on Conversation (and ownership where noted).

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Status codes:** 204 No Content, 401 Not authenticated, 403 Forbidden, 404 Not found.

Example:

```bash
curl -s -X DELETE "$HUGO/v1.0/api/conversations/cnv_mv937yll_CMt9BzVpkZZ17oVKxTh" \
  -H "Authorization: Bearer $TOKEN"
```

Response: 204 with an empty body.

### GET `/v1.0/api/conversations/{id}/messages`

Messages in order, including compacted ones and their tool calls.

**Authentication:** Any REST scheme. Permission: Read on Conversation.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |
| `maxResults` | query | integer | 1 to 1000. Default 25. |
| `skip` | query | integer | Records to skip. Default 0. |
| `ordering` | query | enum | CreatedDescending (default), CreatedAscending, NameAscending, NameDescending. |
| `search` | query | string | Text to match. |
| `activeOnly` | query | boolean | Only active records. Default false. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `success`, `maxResults`, `skip`, `totalRecords`, `recordsRemaining`, `endOfResults`, `timestampUtc`, `objects`, `objects[].id`, `objects[].tenantId`, `objects[].conversationId`, `objects[].turnId`, `objects[].sequence`, `objects[].role`, `objects[].content`, `objects[].thinkingContent`, `objects[].toolCalls`, `objects[].toolCallId`, `objects[].toolName`, `objects[].modelEndpointId`, `objects[].inputTokens`, `objects[].outputTokens`, `objects[].timeToFirstTokenMs`, `objects[].durationMs`, `objects[].finishReason`, `objects[].compacted`, `objects[].createdUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/conversations/cnv_mv937yll_CMt9BzVpkZZ17oVKxTh/messages" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "success": true,
  "maxResults": 1000,
  "skip": 0,
  "totalRecords": 6,
  "recordsRemaining": 0,
  "endOfResults": true,
  "timestampUtc": "2026-10-15T05:17:48.4819Z",
  "objects": [
    {
      "id": "msg_mv937yn2_cQX9hSzqJ1hmioAiNgj",
      "tenantId": "default",
      "conversationId": "cnv_mv937yll_CMt9BzVpkZZ17oVKxTh",
      "turnId": "trn_mv937yn2_B1Z67OCqzvGUYfvk0hO",
      "sequence": 1,
      "role": "User",
      "content": "Which tables does demo-db have?",
      "toolCalls": [],
      "inputTokens": 0,
      "outputTokens": 0,
      "compacted": false,
      "createdUtc": "2026-10-15T05:17:48.446317Z"
    },
    {
      "id": "msg_mv937yn3_anOGtOCrvekYhBg1tKE",
      "tenantId": "default",
      "conversationId": "cnv_mv937yll_CMt9BzVpkZZ17oVKxTh",
      "turnId": "trn_mv937yn2_B1Z67OCqzvGUYfvk0hO",
      "sequence": 2,
      "role": "Assistant",
      "content": "",
      "toolCalls": [
        {
          "callId": "call_stub_be9afbc69b88",
          "name": "sql_list_tables",
          "argumentsJson": "{\"resource\":\"demo-db\"}"
        }
      ],
      "modelEndpointId": "mep_mv937yj6_RbhrM8KfcgiBVTuL8o2",
      "inputTokens": 3034,
      "outputTokens": 9,
      "compacted": false,
      "createdUtc": "2026-10-15T05:17:48.447074Z"
    },
    "… 4 more"
  ]
}
```

### POST `/v1.0/api/conversations/{id}/messages`

Run a turn and wait for it. The result has the final text, every message the turn produced (user, assistant, tool), a tool-call trace with effects and decisions, and token counts. Needs Execute on the profile.

**Authentication:** Any REST scheme. Permission: Update on Conversation.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `message` | string | **Required.** The user message. |
| `conversationId` | string | Conversation to continue (`/ask` only). |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `turnId`, `conversationId`, `finalText`, `thinking`, `messages`, `toolCalls`, `tokensIn`, `tokensOut`, `tokensEstimated`, `timeToFirstTokenMs`, `durationMs`, `iterations`, `finishReason`, `errorCode`, `errorMessage`, `compacted`, `toolsAvailable`, `toolsOffered`, `toolRetrievalUsed`, `usedFallback`, `model`, `pendingApprovalId`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/conversations/cnv_mv937yll_CMt9BzVpkZZ17oVKxTh/messages" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"message": "Which tables does demo-db have?"}'
```

Response (200):

```text
{
  "turnId": "trn_mv937yn2_B1Z67OCqzvGUYfvk0hO",
  "conversationId": "cnv_mv937yll_CMt9BzVpkZZ17oVKxTh",
  "finalText": "Mr. Anderson, demo-db answered. It is an in-memory SQLite database, so it holds no tables until something creates them. Nothing needs your attention.",
  "messages": [
    {
      "id": "msg_mv937yn2_cQX9hSzqJ1hmioAiNgj",
      "conversationId": "cnv_mv937yll_CMt9BzVpkZZ17oVKxTh",
      "turnId": "trn_mv937yn2_B1Z67OCqzvGUYfvk0hO",
      "role": "User",
      "content": "Which tables does demo-db have?",
      "toolCalls": [],
      "tokensIn": 0,
      "tokensOut": 0,
      "createdUtc": "2026-10-15T05:17:48.446317Z"
    },
    {
      "id": "msg_mv937yn3_anOGtOCrvekYhBg1tKE",
      "conversationId": "cnv_mv937yll_CMt9BzVpkZZ17oVKxTh",
      "turnId": "trn_mv937yn2_B1Z67OCqzvGUYfvk0hO",
      "role": "Assistant",
      "content": "",
      "toolCalls": [
        {
          "id": "call_stub_be9afbc69b88",
          "name": "sql_list_tables",
          "argumentsJson": "{\"resource\":\"demo-db\"}"
        }
      ],
      "tokensIn": 3034,
      "tokensOut": 9,
      "createdUtc": "2026-10-15T05:17:48.447074Z"
    },
    "… 2 more"
  ],
  "toolCalls": [
    {
      "callId": "call_stub_be9afbc69b88",
      "toolName": "sql_list_tables",
      "resource": "demo-db",
      "argumentsJson": "{\"resource\":\"demo-db\"}",
      "effect": "Read",
      "decision": "Allowed",
      "durationMs": 0,
      "approvalWaitMs": 0,
      "isError": false,
      "truncated": false,
      "outputPreview": "Tables in main (0 objects)\nNo tables or views found in schema 'main'. Use sql_list_schemas to see other schemas.\nUse sql_describe_table for full columns, keys, and indexes; sql_get_context for table meaning.\n",
      "iteration": 1,
      "parallel": false
    }
  ],
  "tokensIn": 6142,
  "tokensOut": 46,
  "tokensEstimated": false,
…
```

### POST `/v1.0/api/conversations/{id}/cancel`

Cancel the turn running in a conversation. With no turn running, `cancelled` is false.

**Authentication:** Any REST scheme. Permission: Update on Conversation.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `conversationId`, `cancelled`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/conversations/cnv_mv937yll_CMt9BzVpkZZ17oVKxTh/cancel" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "conversationId": "cnv_mv937yll_CMt9BzVpkZZ17oVKxTh",
  "cancelled": false
}
```

### GET `/v1.0/api/conversations/{id}/export`

Export a conversation as Markdown (`format=md`, default) or JSON (`format=json`).

**Authentication:** Any REST scheme. Permission: Read on Conversation.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |
| `format` | query | string | `md` (default) or `json`. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

Example:

```bash
curl -s "$HUGO/v1.0/api/conversations/cnv_mv937yll_CMt9BzVpkZZ17oVKxTh/export?format=md" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```text
# Checkout latency, Thursday

- Conversation: `cnv_mv937yll_CMt9BzVpkZZ17oVKxTh`
- Profile: Hugo
- Source: Dashboard
- Started: 2026-10-15 05:17:48Z
- Tokens: 9293 in, 69 out

## Mr. Anderson

Which tables does demo-db have?

## Agent Smith

- Tool call `sql_list_tables`: `{"resource":"demo-db"}`

### Tool result: sql_list_tables

```
<tool_output resource="demo-db" tool="sql_list_tables" truncated="false">
Tables in main (0 objects)
No tables or views found in schema 'main'. Use sql_list_schemas to see other schemas.
Use sql_describe_table for full columns, keys, and indexes; sql_get_context for table meaning.

</tool_output>
```

## Agent Smith

Mr. Anderson, demo-db answered. It is an in-memory SQLite database, so it holds no tables until something creates them. Nothing needs your attention.

## Mr. Anderson

Thank you, Agent Smith.

## Agent Smith

Good evening, Mr. Anderson. I am Agent Smith. Ask me about the resources bound to this profile.

```

## Events

The event stream is the dashboard's live feed for a tenant: approvals as they are requested and resolved, tool invocations, health changes, and restart notices, with heartbeat events while idle. It is a long-lived SSE connection, so the Postman collection documents it without running it.

| Method | Path | Summary |
|---|---|---|
| GET | `/v1.0/api/events` | Server-Sent Events stream of the caller's tenant events (heartbeat events while idle) |

### GET `/v1.0/api/events`

A Server-Sent Events stream of the caller's tenant: approvals as they are requested and resolved, tool invocation events, health changes, and restart notices, with heartbeat events while idle. The connection stays open until the client closes it.

**Authentication:** Any REST scheme. Permission: Read on Tenant.

**Status codes:** 401 Not authenticated, 403 Forbidden.

Not run by the Postman collection because the stream stays open until the client disconnects.

## Analytics

Analytics reports are computed on request from conversations, turns, and tool invocations, bucketed by minute, hour, or day with empty buckets filled in, and always scoped to the caller's tenant. All four take the same `from`, `to`, and `interval` parameters.

| Method | Path | Summary |
|---|---|---|
| GET | `/v1.0/api/analytics/overview` | Conversations, turns, tokens, tool calls, approvals, and latency per bucket |
| GET | `/v1.0/api/analytics/tokens` | Token usage per bucket (input and output) with window totals |
| GET | `/v1.0/api/analytics/tools` | Tool calls, errors, and approvals per bucket, with per-tool rows |
| GET | `/v1.0/api/analytics/models` | Turns, tokens, and latency per model endpoint, with per-bucket totals |

### GET `/v1.0/api/analytics/overview`

Conversations, turns, tokens, tool calls, approvals, and latency per bucket. `from` and `to` are ISO 8601 UTC (default: the last seven days); `interval` is minute, hour, or day (default chosen by window length).

**Authentication:** Any REST scheme. Permission: Read on Analytics.

| Parameter | In | Type | Description |
|---|---|---|---|
| `from` | query | string | Window start (ISO 8601 UTC). Default: seven days before to. |
| `to` | query | string | Window end (ISO 8601 UTC). Default: now. |
| `interval` | query | string | Bucket width: minute, hour, or day. Default chosen by window length. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden.

**Response fields:** `kind`, `fromUtc`, `toUtc`, `interval`, `conversations`, `totals`, `approvalsRequested`, `buckets`, `tools`, `models`.

Example:

```bash
curl -s "$HUGO/v1.0/api/analytics/overview?interval=hour" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "kind": "overview",
  "fromUtc": "2026-10-08T05:00:00Z",
  "toUtc": "2026-10-15T05:17:50.188728Z",
  "interval": "hour",
  "conversations": 22,
  "totals": {
    "startUtc": "2026-10-08T05:00:00Z",
    "turns": 30,
    "inputTokens": 135731,
    "outputTokens": 989,
    "toolCalls": 61,
    "toolErrors": 20,
    "approvals": 15,
    "avgTimeToFirstTokenMs": 1.5333333333333334,
    "avgTurnDurationMs": 2.1666666666666665
  },
  "approvalsRequested": 15,
  "buckets": [
    {
      "startUtc": "2026-10-08T05:00:00Z",
      "turns": 0,
      "inputTokens": 0,
      "outputTokens": 0,
      "toolCalls": 0,
      "toolErrors": 0,
      "approvals": 0
    },
    {
      "startUtc": "2026-10-08T06:00:00Z",
      "turns": 0,
      "inputTokens": 0,
      "outputTokens": 0,
      "toolCalls": 0,
      "toolErrors": 0,
      "approvals": 0
    },
    "… 167 more"
  ]
}
```

### GET `/v1.0/api/analytics/tokens`

Input and output tokens per bucket with window totals. `from` and `to` are ISO 8601 UTC (default: the last seven days); `interval` is minute, hour, or day (default chosen by window length).

**Authentication:** Any REST scheme. Permission: Read on Analytics.

| Parameter | In | Type | Description |
|---|---|---|---|
| `from` | query | string | Window start (ISO 8601 UTC). Default: seven days before to. |
| `to` | query | string | Window end (ISO 8601 UTC). Default: now. |
| `interval` | query | string | Bucket width: minute, hour, or day. Default chosen by window length. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden.

**Response fields:** `kind`, `fromUtc`, `toUtc`, `interval`, `conversations`, `totals`, `approvalsRequested`, `buckets`, `tools`, `models`.

Example:

```bash
curl -s "$HUGO/v1.0/api/analytics/tokens?interval=hour" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "kind": "tokens",
  "fromUtc": "2026-10-08T05:00:00Z",
  "toUtc": "2026-10-15T05:17:50.201755Z",
  "interval": "hour",
  "conversations": 22,
  "totals": {
    "startUtc": "2026-10-08T05:00:00Z",
    "turns": 30,
    "inputTokens": 135731,
    "outputTokens": 989,
    "toolCalls": 61,
    "toolErrors": 20,
    "approvals": 15,
    "avgTimeToFirstTokenMs": 1.5333333333333334,
    "avgTurnDurationMs": 2.1666666666666665
  },
  "approvalsRequested": 15,
  "buckets": [
    {
      "startUtc": "2026-10-08T05:00:00Z",
      "turns": 0,
      "inputTokens": 0,
      "outputTokens": 0,
      "toolCalls": 0,
      "toolErrors": 0,
      "approvals": 0
    },
    {
      "startUtc": "2026-10-08T06:00:00Z",
      "turns": 0,
      "inputTokens": 0,
      "outputTokens": 0,
      "toolCalls": 0,
      "toolErrors": 0,
      "approvals": 0
    },
    "… 167 more"
  ]
}
```

### GET `/v1.0/api/analytics/tools`

Tool calls, errors, and approvals per bucket, with per-tool rows. `from` and `to` are ISO 8601 UTC (default: the last seven days); `interval` is minute, hour, or day (default chosen by window length).

**Authentication:** Any REST scheme. Permission: Read on Analytics.

| Parameter | In | Type | Description |
|---|---|---|---|
| `from` | query | string | Window start (ISO 8601 UTC). Default: seven days before to. |
| `to` | query | string | Window end (ISO 8601 UTC). Default: now. |
| `interval` | query | string | Bucket width: minute, hour, or day. Default chosen by window length. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden.

**Response fields:** `kind`, `fromUtc`, `toUtc`, `interval`, `conversations`, `totals`, `approvalsRequested`, `buckets`, `tools`, `models`.

Example:

```bash
curl -s "$HUGO/v1.0/api/analytics/tools?interval=hour" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "kind": "tools",
  "fromUtc": "2026-10-08T05:00:00Z",
  "toUtc": "2026-10-15T05:17:50.215847Z",
  "interval": "hour",
  "conversations": 22,
  "totals": {
    "startUtc": "2026-10-08T05:00:00Z",
    "turns": 30,
    "inputTokens": 135731,
    "outputTokens": 989,
    "toolCalls": 61,
    "toolErrors": 20,
    "approvals": 15,
    "avgTimeToFirstTokenMs": 1.5333333333333334,
    "avgTurnDurationMs": 2.1666666666666665
  },
  "approvalsRequested": 15,
  "buckets": [
    {
      "startUtc": "2026-10-08T05:00:00Z",
      "turns": 0,
      "inputTokens": 0,
      "outputTokens": 0,
      "toolCalls": 0,
      "toolErrors": 0,
      "approvals": 0
    },
    {
      "startUtc": "2026-10-08T06:00:00Z",
      "turns": 0,
      "inputTokens": 0,
      "outputTokens": 0,
      "toolCalls": 0,
      "toolErrors": 0,
      "approvals": 0
    },
    "… 167 more"
  ],
  "tools": [
    {
      "toolName": "sql_query",
      "connectorType": "sqlite",
      "calls": 45,
      "errors": 0,
      "denied": 20,
      "approvals": 15,
      "avgDurationMs": 0.35
    },
    {
      "toolName": "sql_list_tables",
      "connectorType": "sqlite",
      "calls": 16,
      "errors": 0,
      "denied": 0,
      "approvals": 0,
      "avgDurationMs": 0.25
    }
  ]
}
```

### GET `/v1.0/api/analytics/models`

Turns, tokens, and latency per model endpoint. `from` and `to` are ISO 8601 UTC (default: the last seven days); `interval` is minute, hour, or day (default chosen by window length).

**Authentication:** Any REST scheme. Permission: Read on Analytics.

| Parameter | In | Type | Description |
|---|---|---|---|
| `from` | query | string | Window start (ISO 8601 UTC). Default: seven days before to. |
| `to` | query | string | Window end (ISO 8601 UTC). Default: now. |
| `interval` | query | string | Bucket width: minute, hour, or day. Default chosen by window length. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden.

**Response fields:** `kind`, `fromUtc`, `toUtc`, `interval`, `conversations`, `totals`, `approvalsRequested`, `buckets`, `tools`, `models`.

Example:

```bash
curl -s "$HUGO/v1.0/api/analytics/models?interval=hour" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```text
{
  "kind": "models",
  "fromUtc": "2026-10-08T05:00:00Z",
  "toUtc": "2026-10-15T05:17:50.229836Z",
  "interval": "hour",
  "conversations": 22,
  "totals": {
    "startUtc": "2026-10-08T05:00:00Z",
    "turns": 30,
    "inputTokens": 135731,
    "outputTokens": 989,
    "toolCalls": 61,
    "toolErrors": 20,
    "approvals": 15,
    "avgTimeToFirstTokenMs": 1.5333333333333334,
    "avgTurnDurationMs": 2.1666666666666665
  },
  "approvalsRequested": 15,
  "buckets": [
    {
      "startUtc": "2026-10-08T05:00:00Z",
      "turns": 0,
      "inputTokens": 0,
      "outputTokens": 0,
      "toolCalls": 0,
      "toolErrors": 0,
      "approvals": 0
    },
    {
      "startUtc": "2026-10-08T06:00:00Z",
      "turns": 0,
      "inputTokens": 0,
      "outputTokens": 0,
      "toolCalls": 0,
      "toolErrors": 0,
      "approvals": 0
    },
    "… 167 more"
  ],
  "models": [
    {
      "modelEndpointId": "mep_mv937yj6_RbhrM8KfcgiBVTuL8o2",
      "name": "local-model",
      "provider": "OpenAICompatible",
      "model": "stub-model",
      "turns": 9,
      "inputTokens": 41288,
      "outputTokens": 299,
      "avgTimeToFirstTokenMs": 0.6666666666666666,
      "avgTurnDurationMs": 1.2222222222222223
    },
    {
      "modelEndpointId": "mep_mv930ljf_IUsKZfloacTdv6M5Dfc",
      "turns": 7,
      "inputTokens": 31738,
…
```

## Audit

The audit log records security-relevant events: sign-ins and failures, session issue and revoke, credential creation and rotation, authorization denials and administrator bypasses, role and assignment changes, every secret operation, configuration changes, lockdown, approvals decided, and pinned SSH host keys. Denials are always written. Tool calls are not duplicated here; they have their own records.

| Method | Path | Summary |
|---|---|---|
| GET | `/v1.0/api/tenants/{tenantId}/audit` | List audit records (eventType, principalId, result, fromUtc, toUtc, pageNumber, pageSize) |
| DELETE | `/v1.0/api/tenants/{tenantId}/audit` | Delete audit records matching the filter |
| GET | `/v1.0/api/tenants/{tenantId}/audit/{id}` | Read an audit record |
| DELETE | `/v1.0/api/tenants/{tenantId}/audit/{id}` | Delete an audit record |

### GET `/v1.0/api/tenants/{tenantId}/audit`

Audit records, newest first. Filters: `eventType`, `principalId`, `result`, `fromUtc`, `toUtc`; paging with `pageNumber` and `pageSize`.

**Authentication:** Any REST scheme. Permission: Read on Audit.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `eventType` | query | enum | An audit event type (see docs/SECURITY.md). |
| `principalId` | query | string | Only this principal. |
| `result` | query | enum | Authorization result. |
| `fromUtc` | query | date-time | Lower bound. |
| `toUtc` | query | date-time | Upper bound. |
| `pageNumber` | query | integer | 1-based page. Default 1. |
| `pageSize` | query | integer | Default 25. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `pageNumber`, `pageSize`, `totalCount`, `totalPages`, `objects`, `objects[].id`, `objects[].tenantId`, `objects[].eventType`, `objects[].requestId`, `objects[].correlationId`, `objects[].traceId`, `objects[].authScheme`, `objects[].principalType`, `objects[].principalId`, `objects[].principalName`, `objects[].sessionId`, `objects[].resourceType`, `objects[].resourceId`, `objects[].operation`, `objects[].requestType`, `objects[].method`, `objects[].path`, `objects[].sourceIp`, `objects[].authResult`, `objects[].authorizationResult`, `objects[].reason`, `objects[].bypassReason`, `objects[].statusCode`, `objects[].description`, `objects[].active`, `objects[].isProtected`, `objects[].createdUtc`, `objects[].lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/tenants/default/audit?eventType=SecretRevealed&pageSize=5" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "pageNumber": 1,
  "pageSize": 5,
  "totalCount": 6,
  "totalPages": 2,
  "objects": [
    {
      "id": "aud_mv937zk6_IQYZUsgpPKkvkt63orK",
      "tenantId": "default",
      "eventType": "SecretRevealed",
      "requestId": "c98aad2d443649e5a5bc0768e7d25f9a",
      "traceId": "0e987cc380c8d1e5e7fd86ecb9a737e8",
      "authScheme": "BearerToken",
      "principalType": "User",
      "principalId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
      "principalName": "anderson@hugo.local",
      "sessionId": "ses_mv937whh_oAAtDPcjMqbuP8c6clO",
      "resourceType": "Secret",
      "resourceId": "sec_mv937zi5_VNEmIR3h9yX4e19gn0w",
      "operation": "Reveal",
      "method": "POST",
      "path": "/v1.0/api/tenants/default/secrets/sec_mv937zi5_VNEmIR3h9yX4e19gn0w/reveal",
      "sourceIp": "127.0.0.1",
      "reason": "Checking the demo database password",
      "statusCode": 200,
      "description": "Revealed secret demo-db-password (fields password). Reason: Checking the demo database password",
      "active": true,
      "isProtected": false,
      "createdUtc": "2026-10-15T05:17:49.638842Z",
      "lastUpdateUtc": "2026-10-15T05:17:49.638842Z"
    },
    {
      "id": "aud_mv937zhf_gwkKge6nEomGgCOZjK2",
      "tenantId": "default",
      "eventType": "SecretRevealed",
      "requestId": "28844fcee6f341b9828d2ce19a2cf638",
      "traceId": "1e875cde5a619b0ac76cc414497f49c6",
      "authScheme": "BearerToken",
      "principalType": "User",
      "principalId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
      "principalName": "anderson@hugo.local",
      "sessionId": "ses_mv937whh_oAAtDPcjMqbuP8c6clO",
      "resourceType": "Secret",
      "resourceId": "sec_mv937zcp_sEmwxNsnTg5N6NBDWea",
      "operation": "Reveal",
      "method": "POST",
      "path": "/v1.0/api/secrets/sec_mv937zcp_sEmwxNsnTg5N6NBDWea/reveal",
      "sourceIp": "127.0.0.1",
      "reason": "Confirming the rotated token before handing it to the on-call engineer",
      "statusCode": 200,
      "description": "Revealed secret demo-api-token (fields token). Reason: Confirming the rotated token before handing it to the on-call engineer",
      "active": true,
      "isProtected": false,
      "createdUtc": "2026-10-15T05:17:49.53981Z",
      "lastUpdateUtc": "2026-10-15T05:17:49.53981Z"
    },
    "… 3 more"
  ]
}
```

### DELETE `/v1.0/api/tenants/{tenantId}/audit`

Delete records matching the filter (same filters as the list). The response says how many were deleted.

**Authentication:** Any REST scheme. Permission: Delete on Audit.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `eventType` | query | enum | An audit event type. |
| `principalId` | query | string | Only this principal. |
| `result` | query | enum | Authorization result. |
| `fromUtc` | query | date-time | Lower bound. |
| `toUtc` | query | date-time | Upper bound. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `deletedCount`.

Example:

```bash
curl -s -X DELETE "$HUGO/v1.0/api/tenants/default/audit?toUtc=2000-01-01T00:00:00Z" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "deletedCount": 0
}
```

### GET `/v1.0/api/tenants/{tenantId}/audit/{id}`

One record with request ID, trace ID, scheme, principal, resource, result, and description.

**Authentication:** Any REST scheme. Permission: Read on Audit.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `eventType`, `requestId`, `correlationId`, `traceId`, `authScheme`, `principalType`, `principalId`, `principalName`, `sessionId`, `resourceType`, `resourceId`, `operation`, `requestType`, `method`, `path`, `sourceIp`, `authResult`, `authorizationResult`, `reason`, `bypassReason`, `statusCode`, `description`, `active`, `isProtected`, `createdUtc`, `lastUpdateUtc`.

Example:

```bash
curl -s "$HUGO/v1.0/api/tenants/default/audit/aud_mv937zk6_IQYZUsgpPKkvkt63orK" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "id": "aud_mv937zk6_IQYZUsgpPKkvkt63orK",
  "tenantId": "default",
  "eventType": "SecretRevealed",
  "requestId": "c98aad2d443649e5a5bc0768e7d25f9a",
  "traceId": "0e987cc380c8d1e5e7fd86ecb9a737e8",
  "authScheme": "BearerToken",
  "principalType": "User",
  "principalId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "principalName": "anderson@hugo.local",
  "sessionId": "ses_mv937whh_oAAtDPcjMqbuP8c6clO",
  "resourceType": "Secret",
  "resourceId": "sec_mv937zi5_VNEmIR3h9yX4e19gn0w",
  "operation": "Reveal",
  "method": "POST",
  "path": "/v1.0/api/tenants/default/secrets/sec_mv937zi5_VNEmIR3h9yX4e19gn0w/reveal",
  "sourceIp": "127.0.0.1",
  "reason": "Checking the demo database password",
  "statusCode": 200,
  "description": "Revealed secret demo-db-password (fields password). Reason: Checking the demo database password",
  "active": true,
  "isProtected": false,
  "createdUtc": "2026-10-15T05:17:49.638842Z",
  "lastUpdateUtc": "2026-10-15T05:17:49.638842Z"
}
```

### DELETE `/v1.0/api/tenants/{tenantId}/audit/{id}`

Delete one record. Needs Delete on Audit.

**Authentication:** Any REST scheme. Permission: Delete on Audit.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `id` | path | string | Record ID. |

**Status codes:** 204 No Content, 401 Not authenticated, 403 Forbidden, 404 Not found.

Example:

```bash
curl -s -X DELETE "$HUGO/v1.0/api/tenants/default/audit/aud_mv937zk6_IQYZUsgpPKkvkt63orK" \
  -H "Authorization: Bearer $TOKEN"
```

Response: 204 with an empty body.

## Request history

Request history captures each HTTP request with its headers and bodies, bounded by `RequestHistory.MaxRequestBodyBytes` and `MaxResponseBodyBytes` and scrubbed for secrets. Secret reveals and A2A traffic are recorded without bodies. Entries expire after `RequestHistory.RetentionDays`.

| Method | Path | Summary |
|---|---|---|
| GET | `/v1.0/api/request-history` | List captured requests (method, statusCode, pathContains, fromUtc, toUtc, pageNumber, pageSize, tenantId, userId) |
| DELETE | `/v1.0/api/request-history` | Delete captured requests matching the filter |
| GET | `/v1.0/api/request-history/summary` | Time-bucketed request counts (fromUtc, toUtc, bucketMinutes) |
| GET | `/v1.0/api/request-history/{id}` | Read a captured request with headers and bodies |
| DELETE | `/v1.0/api/request-history/{id}` | Delete a captured request |

### GET `/v1.0/api/request-history`

Captured requests, newest first. Filters: `method`, `statusCode`, `pathContains`, `fromUtc`, `toUtc`, `userId` (system administrators also `tenantId`); paging with `pageNumber` and `pageSize`.

**Authentication:** Any REST scheme. Permission: Read on RequestHistory.

| Parameter | In | Type | Description |
|---|---|---|---|
| `method` | query | string | HTTP method. |
| `statusCode` | query | integer | Exact status code. |
| `pathContains` | query | string | Substring of the path. |
| `fromUtc` | query | date-time | Lower bound. |
| `toUtc` | query | date-time | Upper bound. |
| `userId` | query | string | Only this user. |
| `tenantId` | query | string | System administrators only: another tenant. |
| `pageNumber` | query | integer | 1-based page. Default 1. |
| `pageSize` | query | integer | Default 25. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden.

**Response fields:** `pageNumber`, `pageSize`, `totalCount`, `totalPages`, `objects`, `objects[].id`, `objects[].tenantId`, `objects[].userId`, `objects[].principalName`, `objects[].method`, `objects[].path`, `objects[].url`, `objects[].statusCode`, `objects[].durationMs`, `objects[].sourceIp`, `objects[].traceId`, `objects[].correlationId`, `objects[].requestBody`, `objects[].requestBodyBytes`, `objects[].requestBodyTruncated`, `objects[].responseBody`, `objects[].responseBodyBytes`, `objects[].responseBodyTruncated`, `objects[].createdUtc`, `objects[].completedUtc`, `objects[].headers`.

Example:

```bash
curl -s "$HUGO/v1.0/api/request-history?pathContains=/v1.0/api/resources&pageSize=5" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "pageNumber": 1,
  "pageSize": 5,
  "totalCount": 218,
  "totalPages": 44,
  "objects": [
    {
      "id": "req_mv937yd9_D0GfkwbfF96TsubmadO",
      "tenantId": "default",
      "userId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
      "principalName": "anderson@hugo.local",
      "method": "POST",
      "path": "/v1.0/api/resources",
      "url": "/v1.0/api/resources",
      "statusCode": 201,
      "durationMs": 0.6,
      "sourceIp": "127.0.0.1",
      "traceId": "3edcd19c0aa0210bf02ed17288cd0e63",
      "correlationId": "80b9657c67c2427bb0dc9e9547aabcb3",
      "requestBodyBytes": 213,
      "requestBodyTruncated": false,
      "responseBodyBytes": 516,
      "responseBodyTruncated": false,
      "createdUtc": "2026-10-15T05:17:48.092613Z",
      "completedUtc": "2026-10-15T05:17:48.09321Z",
      "headers": []
    },
    {
      "id": "req_mv937yc7_7WA42ePndY4ffVHg8bl",
      "tenantId": "default",
      "userId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
      "principalName": "anderson@hugo.local",
      "method": "POST",
      "path": "/v1.0/api/resources",
      "url": "/v1.0/api/resources",
      "statusCode": 201,
      "durationMs": 0.51,
      "sourceIp": "127.0.0.1",
      "traceId": "1999c7436369e5f28e3d4010de3c7161",
      "correlationId": "6c3050bfe50543d3a61285abe741dfec",
      "requestBodyBytes": 208,
      "requestBodyTruncated": false,
      "responseBodyBytes": 507,
      "responseBodyTruncated": false,
      "createdUtc": "2026-10-15T05:17:48.055295Z",
      "completedUtc": "2026-10-15T05:17:48.055806Z",
      "headers": []
    },
    "… 3 more"
  ]
}
```

### DELETE `/v1.0/api/request-history`

Delete entries matching the filter.

**Authentication:** Any REST scheme. Permission: Delete on RequestHistory.

| Parameter | In | Type | Description |
|---|---|---|---|
| `method` | query | string | HTTP method. |
| `statusCode` | query | integer | Exact status code. |
| `pathContains` | query | string | Substring of the path. |
| `fromUtc` | query | date-time | Lower bound. |
| `toUtc` | query | date-time | Upper bound. |
| `userId` | query | string | Only this user. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden.

**Response fields:** `deletedCount`.

Example:

```bash
curl -s -X DELETE "$HUGO/v1.0/api/request-history?toUtc=2000-01-01T00:00:00Z" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "deletedCount": 0
}
```

### GET `/v1.0/api/request-history/summary`

Success and failure counts and average duration per bucket (`bucketMinutes`, default 15).

**Authentication:** Any REST scheme. Permission: Read on RequestHistory.

| Parameter | In | Type | Description |
|---|---|---|---|
| `fromUtc` | query | date-time | Window start. |
| `toUtc` | query | date-time | Window end. |
| `bucketMinutes` | query | integer | Bucket width in minutes. Default 15. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden.

**Response fields:** `totalCount`, `totalSuccess`, `totalFailure`, `averageDurationMs`, `buckets`.

Example:

```bash
curl -s "$HUGO/v1.0/api/request-history/summary?bucketMinutes=60" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "totalCount": 1469,
  "totalSuccess": 1443,
  "totalFailure": 26,
  "averageDurationMs": 3.92,
  "buckets": [
    {
      "bucketStartUtc": "2026-10-14T05:17:50.315462Z",
      "bucketEndUtc": "2026-10-14T06:17:50.315462Z",
      "successCount": 0,
      "failureCount": 0,
      "averageDurationMs": 0
    },
    {
      "bucketStartUtc": "2026-10-14T06:17:50.315462Z",
      "bucketEndUtc": "2026-10-14T07:17:50.315462Z",
      "successCount": 0,
      "failureCount": 0,
      "averageDurationMs": 0
    },
    "… 22 more"
  ]
}
```

### GET `/v1.0/api/request-history/{id}`

One captured request with its headers and bodies. Authorization headers and secret values are scrubbed, and secret reveals and A2A traffic are recorded without bodies.

**Authentication:** Any REST scheme. Permission: Read on RequestHistory.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

**Response fields:** `id`, `tenantId`, `userId`, `principalName`, `method`, `path`, `url`, `statusCode`, `durationMs`, `sourceIp`, `traceId`, `correlationId`, `requestBody`, `requestBodyBytes`, `requestBodyTruncated`, `responseBody`, `responseBodyBytes`, `responseBodyTruncated`, `createdUtc`, `completedUtc`, `headers`.

Example:

```bash
curl -s "$HUGO/v1.0/api/request-history/req_mv937yd9_D0GfkwbfF96TsubmadO" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "id": "req_mv937yd9_D0GfkwbfF96TsubmadO",
  "tenantId": "default",
  "userId": "usr_mv92uyld_OiAAkEipyBHSoJjTUut",
  "principalName": "anderson@hugo.local",
  "method": "POST",
  "path": "/v1.0/api/resources",
  "url": "/v1.0/api/resources",
  "statusCode": 201,
  "durationMs": 0.6,
  "sourceIp": "127.0.0.1",
  "traceId": "3edcd19c0aa0210bf02ed17288cd0e63",
  "correlationId": "80b9657c67c2427bb0dc9e9547aabcb3",
  "requestBody": "{\n  \"name\": \"billing-agent-dryrun\",\n  \"connectorType\": \"a2a-agent\",\n  \"environment\": \"example\",\n  \"settings\": {\n    \"agentCardUrl\": \"http://10.0.0.81:9000/.well-known/agent-card.json\"\n  },\n  \"secretBindings\": []\n}",
  "requestBodyBytes": 213,
  "requestBodyTruncated": false,
  "responseBody": "{\"id\":\"res_mv937yd8_KljZFxRo8gtN9IXa5kA\",\"tenantId\":\"default\",\"ownerUserId\":\"usr_mv92uyld_OiAAkEipyBHSoJjTUut\",\"visibility\":\"Tenant\",\"name\":\"billing-agent-dryrun\",\"connectorType\":\"a2a-agent\",\"endpoint… (516 characters)",
  "responseBodyBytes": 516,
  "responseBodyTruncated": false,
  "createdUtc": "2026-10-15T05:17:48.092613Z",
  "completedUtc": "2026-10-15T05:17:48.09321Z",
  "headers": [
    {
      "id": "chr_mv937yda_Ikmyep1ldo4qrZ3LrDz_0000hea",
      "requestHistoryEntryId": "req_mv937yd9_D0GfkwbfF96TsubmadO",
      "direction": "Request",
      "name": "Content-Type",
      "value": "application/json"
    },
    {
      "id": "chr_mv937yda_Ikmyep1ldo4qrZ3LrDz_0001hea",
      "requestHistoryEntryId": "req_mv937yd9_D0GfkwbfF96TsubmadO",
      "direction": "Request",
      "name": "Authorization",
      "value": "[redacted]"
    },
    "… 20 more"
  ]
}
```

### DELETE `/v1.0/api/request-history/{id}`

Delete one entry.

**Authentication:** Any REST scheme. Permission: Delete on RequestHistory.

| Parameter | In | Type | Description |
|---|---|---|---|
| `id` | path | string | Record ID. |

**Status codes:** 204 No Content, 401 Not authenticated, 403 Forbidden, 404 Not found.

Example:

```bash
curl -s -X DELETE "$HUGO/v1.0/api/request-history/req_mv937yd9_D0GfkwbfF96TsubmadO" \
  -H "Authorization: Bearer $TOKEN"
```

Response: 204 with an empty body.

## Settings and system

Settings are the contents of `hugo.json`, readable and writable through the API with metadata per field: its value (secrets masked), where the value came from (default, file, or environment), whether a change applies live or needs a restart, and its range or allowed values. Fields set by environment variables are read-only here. Restart answers immediately and exits with code 75 so the container runtime brings the process back. Every setting is described in [docs/CONFIGURATION.md](docs/CONFIGURATION.md).

| Method | Path | Summary |
|---|---|---|
| GET | `/v1.0/api/settings` | Read settings with source, apply mode, and editability metadata |
| PUT | `/v1.0/api/settings` | Update settings |
| POST | `/v1.0/api/settings/restart` | Restart this server process (the container runtime restarts it) |
| POST | `/v1.0/api/system/restart` | Restart this server process (same as /v1.0/api/settings/restart; exit code 75 for the container restart policy) |
| POST | `/v1.0/api/settings/database/test` | Test database connectivity |

### GET `/v1.0/api/settings`

Every setting with value (secrets masked), source, apply mode (Live or Restart), editability, range, and allowed values.

**Authentication:** Any REST scheme. Permission: Read on Settings.

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden.

**Response fields:** `settingsFile`, `settingsFileWritable`, `restartPending`, `fields`.

Example:

```bash
curl -s "$HUGO/v1.0/api/settings" \
  -H "Authorization: Bearer $ADMIN_TOKEN"
```

Response (200):

```json
{
  "settingsFile": "hugo.json",
  "settingsFileWritable": true,
  "restartPending": false,
  "fields": [
    {
      "path": "Rest.Hostname",
      "group": "Rest",
      "description": "REST bind hostname (127.0.0.1 locally, * or 0.0.0.0 in a container).",
      "type": "string",
      "value": "127.0.0.1",
      "isSecret": false,
      "isSet": true,
      "source": "File",
      "applyMode": "RestartRequired",
      "editable": true
    },
    {
      "path": "Rest.Port",
      "group": "Rest",
      "description": "REST port.",
      "type": "int",
      "value": "18950",
      "isSecret": false,
      "isSet": true,
      "source": "File",
      "applyMode": "RestartRequired",
      "editable": true,
      "minimum": 1,
      "maximum": 65535
    },
    "… 86 more"
  ]
}
```

### PUT `/v1.0/api/settings`

Change settings by path. Live settings apply at once; restart settings are written to the settings file and listed in `pendingRestart`. Fields overridden by environment variables cannot be changed here. The example writes the default value of `Approvals.ExpirySeconds` back.

**Authentication:** Any REST scheme. Permission: Update on Settings.

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `values` | object[] | **Required.** List of `{ "path": "Section.Field", "value": "text" }`. Values are strings and converted to the field type. |
| `values[].path` | string |  |
| `values[].value` | string |  |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden.

**Response fields:** `saved`, `restartRequired`, `appliedLive`, `pendingRestart`, `settings`.

Example:

```bash
curl -s -X PUT "$HUGO/v1.0/api/settings" \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"values": [{"path": "Approvals.ExpirySeconds", "value": "900"}]}'
```

Response (200):

```json
{
  "saved": true,
  "restartRequired": false,
  "appliedLive": [
    "Approvals.ExpirySeconds"
  ],
  "pendingRestart": [],
  "settings": {
    "settingsFile": "hugo.json",
    "settingsFileWritable": true,
    "restartPending": false,
    "fields": [
      {
        "path": "Rest.Hostname",
        "group": "Rest",
        "description": "REST bind hostname (127.0.0.1 locally, * or 0.0.0.0 in a container).",
        "type": "string",
        "value": "127.0.0.1",
        "isSecret": false,
        "isSet": true,
        "source": "File",
        "applyMode": "RestartRequired",
        "editable": true
      },
      {
        "path": "Rest.Port",
        "group": "Rest",
        "description": "REST port.",
        "type": "int",
        "value": "18950",
        "isSecret": false,
        "isSet": true,
        "source": "File",
        "applyMode": "RestartRequired",
        "editable": true,
        "minimum": 1,
        "maximum": 65535
      },
      "… 86 more"
    ]
  }
}
```

### POST `/v1.0/api/settings/restart`

Same as `POST /v1.0/api/system/restart`.

**Authentication:** Any REST scheme. Permission: Admin on Settings.

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden.

**Response fields:** `restarting`, `node`.

Not run by the Postman collection because it restarts the server.

### POST `/v1.0/api/system/restart`

Ask the server to exit with code 75 so the container runtime restarts it, which applies settings saved with apply mode Restart. The response returns before the process exits.

**Authentication:** Any REST scheme. Permission: Admin on Settings.

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden.

**Response fields:** `restarting`, `node`.

Not run by the Postman collection because it restarts the server.

### POST `/v1.0/api/settings/database/test`

Check that the configured database answers.

**Authentication:** Any REST scheme. Permission: Admin on Settings.

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden.

**Response fields:** `ok`, `provider`, `message`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/api/settings/database/test" \
  -H "Authorization: Bearer $ADMIN_TOKEN"
```

Response (200):

```json
{
  "ok": true,
  "provider": "Sqlite",
  "message": "Database answered."
}
```

## OpenAI-compatible surface

Point any OpenAI-compatible chat client at `/v1.0/openai/v1` and it talks to a Hugo profile: the model name is the profile name, and `GET /v1.0/openai/v1/models` lists the ones the caller may use. Authenticate with a bearer token or credential keys (most clients let you set the API key; use a session token there, or put credential keys in custom headers).

Hugo runs its own loop and its own tools, so the request shape is a strict subset of OpenAI's. Client `tools`, `functions`, and tool messages are rejected with 400 `tools_not_supported`. Client `system` messages are kept, appended after the compiled instructions as caller context. The last message must be a user message.

Conversation state is opt in. Without `user` or the `x-hugo-conversation` header, every request is a fresh conversation built from the messages it carries. With `user`, Hugo keeps a server-side conversation keyed by that string for the caller and profile; with `x-hugo-conversation: cnv_...`, it continues that conversation, and the response repeats the ID in the same header. Responses add a `hugo` object with the conversation ID, the turn ID, Hugo's finish reason, and, when a tool call is waiting for a human, `pending_approval_id`; in that case the assistant message explains the pending approval and the next user message resumes once it is decided.

Streaming follows OpenAI's chunk format and ends with `data: [DONE]`. Errors use OpenAI's error object with Hugo codes such as `model_not_found`, `tools_not_supported`, `invalid_messages`, `conversation_not_found`, and `turn_in_progress`.

| Method | Path | Summary |
|---|---|---|
| POST | `/v1.0/openai/v1/chat/completions` | OpenAI-compatible chat completions running the profile's own agent loop (model = profile name; Execute on that profile) |
| GET | `/v1.0/openai/v1/models` | Agent profiles the caller can use, as OpenAI models |

### POST `/v1.0/openai/v1/chat/completions`

A non-streaming completion. The `hugo` object carries the conversation and turn IDs.

**Authentication:** Any REST scheme. The handler checks Execute on AgentProfile (and ownership where noted).

**Request body** (JSON; every field optional unless the description says otherwise):

| Field | Type | Description |
|---|---|---|
| `model` | string | **Required.** Agent profile name (see `GET /v1.0/openai/v1/models`). |
| `messages` | object[] | **Required.** `role` (system, user, assistant) and `content`. The last message must be a user message. Tool messages are rejected. |
| `messages[].role` | string |  |
| `messages[].content` | string |  |
| `stream` | boolean | true for SSE chunks. |
| `stream_options` | object | `{ "include_usage": true }` adds a final usage chunk. |
| `stream_options.include_usage` | boolean |  |
| `tools` | object[] | Rejected (400 `tools_not_supported`). |
| `functions` | object[] | Rejected. |
| `user` | string | Key of a server-side conversation to continue (up to 256 characters). |
| `temperature` | number | Passed to the model. |
| `max_tokens` | integer | Output token limit. |

**Status codes:** 200 OK, 400 Validation failed, 401 Not authenticated, 403 Forbidden.

**Response fields:** `id`, `object`, `created`, `model`, `choices`.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/openai/v1/chat/completions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"model": "Hugo", "messages": [{"role": "user", "content": "What tables are in demo-db?"}], "user": "postman-demo"}'
```

Response (200):

```json
{
  "id": "chatcmpl-1d5ff9e057dc4cd8bb50ed69d262344e",
  "object": "chat.completion",
  "created": 1792041468,
  "model": "Hugo",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "Mr. Anderson, demo-db answered. It is an in-memory SQLite database, so it holds no tables until something creates them. Nothing needs your attention."
      },
      "finish_reason": "stop"
    }
  ],
  "usage": {
    "prompt_tokens": 6922,
    "completion_tokens": 46,
    "total_tokens": 6968
  },
  "hugo": {
    "conversation_id": "cnv_mv92w3rb_uEnwcdfgABZf2WNBYXX",
    "turn_id": "trn_mv937yq3_1ZQZzoqeq9XU1Y2b1km",
    "finish_reason": "completed"
  }
}
```

### GET `/v1.0/openai/v1/models`

The profiles the caller may use, as OpenAI models.

**Authentication:** Any REST scheme. The handler checks Read on AgentProfile (and ownership where noted).

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden.

**Response fields:** `object`, `data`.

Example:

```bash
curl -s "$HUGO/v1.0/openai/v1/models" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```json
{
  "object": "list",
  "data": [
    {
      "id": "Hugo",
      "object": "model",
      "created": 1792040862,
      "owned_by": "hugo"
    },
    {
      "id": "Oncall",
      "object": "model",
      "created": 1792041467,
      "owned_by": "hugo"
    },
    {
      "id": "Oncall-Copy",
      "object": "model",
      "created": 1792041467,
      "owned_by": "hugo"
    }
  ]
}
```

## MCP endpoint

These are the HTTP routes behind Hugo's MCP server. The server speaks Streamable HTTP: `POST` carries JSON-RPC requests, `GET` opens the optional server-to-client event stream of a session, and `DELETE` ends a session. Authentication uses the REST schemes, and failures are HTTP errors with a JSON-RPC body (401 with `WWW-Authenticate: Bearer realm="hugo"`, 403 for a profile the caller may not use or one not exposed over MCP, 404 for an unknown profile or an expired session). Tools, prompts, resources, sessions, and the approval flow are documented in [MCP_API.md](MCP_API.md).

| Method | Path | Summary |
|---|---|---|
| POST | `/mcp` | MCP Streamable HTTP endpoint for the caller's resolved profile (D-018) (authenticated by the MCP proxy with the REST schemes; 401 with a bearer challenge on failure) |
| GET | `/mcp` | MCP server-to-client event stream of a session (authenticated by the MCP proxy with the REST schemes; 401 with a bearer challenge on failure) |
| DELETE | `/mcp` | End an MCP session (authenticated by the MCP proxy with the REST schemes; 401 with a bearer challenge on failure) |
| POST | `/mcp/{profileName}` | MCP Streamable HTTP endpoint for a named profile (authenticated by the MCP proxy with the REST schemes; 401 with a bearer challenge on failure) |
| GET | `/mcp/{profileName}` | MCP server-to-client event stream of a session on a named profile (authenticated by the MCP proxy with the REST schemes; 401 with a bearer challenge on failure) |
| DELETE | `/mcp/{profileName}` | End an MCP session on a named profile (authenticated by the MCP proxy with the REST schemes; 401 with a bearer challenge on failure) |

### POST `/mcp`

JSON-RPC requests to the MCP server for the caller's resolved profile: the path does not name one, so Hugo uses the `x-hugo-profile` header, then the credential's bound profile, then the user's default profile, then the tenant default (D-018). With protocol revisions up to 2025-11-25 the first request is `initialize`, and the `Mcp-Session-Id` response header must accompany every later request; with 2026-07-28 each request is self-contained. The response is JSON or, for long-running calls, an SSE stream. The example is an `initialize`.

**Authentication:** Any REST scheme, checked by the MCP proxy before the request reaches the MCP server. The resolved profile needs Execute on AgentProfile and `exposeMcp`; each tool call is authorized again.

**Status codes:** 200 OK, 401 Not authenticated.

Example:

```bash
curl -s -X POST "$HUGO/mcp" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Accept: application/json, text/event-stream" \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {"protocolVersion": "2025-06-18", "capabilities": {}, "clientInfo": {"name": "postman", "version": "1.0"}}}'
```

Response (200):

```json
{
  "jsonrpc": "2.0",
  "result": {
    "protocolVersion": "2025-06-18",
    "capabilities": {
      "logging": {},
      "completions": {},
      "prompts": {
        "listChanged": true
      },
      "resources": {
        "subscribe": true,
        "listChanged": true
      },
      "tools": {
        "listChanged": true
      }
    },
    "serverInfo": {
      "name": "Hugo",
      "version": "0.1.0-alpha"
    },
    "instructions": "You are Agent Smith, an operations agent working for Mr. Anderson in the \"default\" environment. You speak with calm, precise confidence and an occasional dry, formal wit, and you always address the pe… (12302 characters)"
  },
  "id": 1
}
```

### GET `/mcp`

Opens the optional server-to-client SSE stream of a session (send `Mcp-Session-Id`), which carries list-changed and progress notifications. The connection stays open until the client closes it.

**Authentication:** Any REST scheme, checked by the MCP proxy before the request reaches the MCP server. The resolved profile needs Execute on AgentProfile and `exposeMcp`; each tool call is authorized again.

**Status codes:** 200 OK.

Not run by the Postman collection because the stream stays open until the client disconnects.

### DELETE `/mcp`

Ends the session named by `Mcp-Session-Id`. Later requests with that ID get 404 and the client must initialize again.

**Authentication:** Any REST scheme, checked by the MCP proxy before the request reaches the MCP server. The resolved profile needs Execute on AgentProfile and `exposeMcp`; each tool call is authorized again.

**Status codes:** 200 OK.

Example:

```bash
curl -s -X DELETE "$HUGO/mcp" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Mcp-Session-Id: 205506a4-cb11-4cc0-822a-922249e6bdc6"
```

Response: 200 with an empty body.

### POST `/mcp/{profileName}`

The same MCP endpoint with the profile pinned in the path. A credential locked to another profile gets 403. The example lists tools in a session opened on the `Oncall` profile.

**Authentication:** Any REST scheme, checked by the MCP proxy before the request reaches the MCP server. The resolved profile needs Execute on AgentProfile and `exposeMcp`; each tool call is authorized again.

| Parameter | In | Type | Description |
|---|---|---|---|
| `profileName` | path | string | Path parameter. |

**Status codes:** 200 OK.

Example:

```bash
curl -s -X POST "$HUGO/mcp/Oncall" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Accept: application/json, text/event-stream" \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {"protocolVersion": "2025-06-18", "capabilities": {}, "clientInfo": {"name": "postman", "version": "1.0"}}}'
```

Response (200):

```json
{
  "jsonrpc": "2.0",
  "result": {
    "protocolVersion": "2025-06-18",
    "capabilities": {
      "logging": {},
      "completions": {},
      "prompts": {
        "listChanged": true
      },
      "resources": {
        "subscribe": true,
        "listChanged": true
      },
      "tools": {
        "listChanged": true
      }
    },
    "serverInfo": {
      "name": "Hugo",
      "version": "0.1.0-alpha"
    },
    "instructions": "You are Agent Smith, an operations agent working for Mr. Anderson in the \"default\" environment. You speak with calm, precise confidence and an occasional dry, formal wit, and you always address the pe… (13415 characters)"
  },
  "id": 1
}
```

### GET `/mcp/{profileName}`

The server-to-client SSE stream of a session opened on `/mcp/{profileName}`.

**Authentication:** Any REST scheme, checked by the MCP proxy before the request reaches the MCP server. The resolved profile needs Execute on AgentProfile and `exposeMcp`; each tool call is authorized again.

| Parameter | In | Type | Description |
|---|---|---|---|
| `profileName` | path | string | Path parameter. |

**Status codes:** 200 OK.

Not run by the Postman collection because the stream stays open until the client disconnects.

### DELETE `/mcp/{profileName}`

Ends a session opened on `/mcp/{profileName}`.

**Authentication:** Any REST scheme, checked by the MCP proxy before the request reaches the MCP server. The resolved profile needs Execute on AgentProfile and `exposeMcp`; each tool call is authorized again.

| Parameter | In | Type | Description |
|---|---|---|---|
| `profileName` | path | string | Path parameter. |

**Status codes:** 200 OK.

Example:

```bash
curl -s -X DELETE "$HUGO/mcp/Oncall" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Mcp-Session-Id: 23807fb3-2fd7-4eac-bf0c-23168cb64618"
```

Response: 200 with an empty body.

## A2A endpoint

These are the HTTP routes behind Hugo's A2A 1.0 agents. The agent card is anonymous so other agents can discover a profile; the tenant catalog and the JSON-RPC endpoint need a REST credential. JSON-RPC errors come back with HTTP 200 (or 404 for an unknown agent), following the A2A JSON-RPC binding. Methods, task states, and the approval behavior are documented in [A2A_API.md](A2A_API.md).

| Method | Path | Summary |
|---|---|---|
| GET | `/v1.0/a2a/{tenantId}/{profileName}/.well-known/agent-card.json` | A2A agent card of a profile (exposed profiles only) |
| GET | `/v1.0/a2a/{tenantId}` | A2A agent cards of every exposed profile in a tenant |
| POST | `/v1.0/a2a/{tenantId}/{profileName}` | A2A JSON-RPC: SendMessage, SendStreamingMessage (SSE), GetTask, ListTasks, CancelTask, SubscribeToTask (SSE), GetExtendedAgentCard, and the legacy message/send, message/stream, tasks/get, tasks/list, tasks/cancel, tasks/resubscribe |

### GET `/v1.0/a2a/{tenantId}/{profileName}/.well-known/agent-card.json`

The public agent card: name, description, the JSON-RPC interface URL, streaming capability, one skill per bound connector family plus `agent-smith`, and the bearer security scheme. Profiles with Private visibility or without `exposeA2A` answer 404.

**Authentication:** None (anonymous).

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `profileName` | path | string | Path parameter. |

**Status codes:** 200 OK.

Example:

```bash
curl -s "$HUGO/v1.0/a2a/default/Hugo/.well-known/agent-card.json"
```

Response (200):

```text
{
  "name": "Hugo",
  "description": "Default operations agent. Attach resources to give Agent Smith something to work with.",
  "version": "0.1.0-alpha",
  "supportedInterfaces": [
    {
      "url": "http://127.0.0.1:8800/v1.0/a2a/default/Hugo",
      "protocolBinding": "JSONRPC",
      "protocolVersion": "1.0"
    }
  ],
  "capabilities": {
    "streaming": true,
    "pushNotifications": false,
    "stateTransitionHistory": false
  },
  "provider": {
    "organization": "Hugo",
    "url": "https://github.com/jchristn/AgentSmith"
  },
  "skills": [
    {
      "id": "agent-smith",
      "name": "Agent Smith",
      "description": "Hugo (Agent Smith) investigates and operates the systems attached to it: SQLite (demo-db). Send a plain-language request; changes may require human approval.",
      "tags": [
        "operations",
        "investigation"
      ]
    },
    {
      "id": "sql",
      "name": "SQLite",
      "description": "Investigate and operate SQLite resources: demo-db (ReadOnly).",
      "tags": [
        "sql"
      ]
    }
  ],
  "defaultInputModes": [
    "text/plain",
    "application/json"
  ],
  "defaultOutputModes": [
    "text/plain"
  ],
  "securitySchemes": {
    "hugo": {
      "httpAuthSecurityScheme": {
        "scheme": "bearer",
        "description": "Hugo session token from POST /v1.0/token. Credentials may also send x-access-key and x-secret-key headers."
      }
    }
  },
  "securityRequirements": [
…
```

### GET `/v1.0/a2a/{tenantId}`

Cards of every exposed profile the caller can see.

**Authentication:** Any REST scheme. Permission: Read on AgentProfile.

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

Example:

```bash
curl -s "$HUGO/v1.0/a2a/default" \
  -H "Authorization: Bearer $TOKEN"
```

Response (200):

```text
[
  {
    "name": "Hugo",
    "description": "Default operations agent. Attach resources to give Agent Smith something to work with.",
    "version": "0.1.0-alpha",
    "supportedInterfaces": [
      {
        "url": "http://127.0.0.1:8800/v1.0/a2a/default/Hugo",
        "protocolBinding": "JSONRPC",
        "protocolVersion": "1.0"
      }
    ],
    "capabilities": {
      "streaming": true,
      "pushNotifications": false,
      "stateTransitionHistory": false
    },
    "provider": {
      "organization": "Hugo",
      "url": "https://github.com/jchristn/AgentSmith"
    },
    "skills": [
      {
        "id": "agent-smith",
        "name": "Agent Smith",
        "description": "Hugo (Agent Smith) investigates and operates the systems attached to it: SQLite (demo-db). Send a plain-language request; changes may require human approval.",
        "tags": [
          "operations",
          "investigation"
        ]
      },
      {
        "id": "sql",
        "name": "SQLite",
        "description": "Investigate and operate SQLite resources: demo-db (ReadOnly).",
        "tags": [
          "sql"
        ]
      }
    ],
    "defaultInputModes": [
      "text/plain",
      "application/json"
    ],
    "defaultOutputModes": [
      "text/plain"
    ],
    "securitySchemes": {
      "hugo": {
        "httpAuthSecurityScheme": {
          "scheme": "bearer",
          "description": "Hugo session token from POST /v1.0/token. Credentials may also send x-access-key and x-secret-key headers."
        }
      }
    },
…
```

### POST `/v1.0/a2a/{tenantId}/{profileName}`

The card, for an authenticated caller.

**Authentication:** Any REST scheme. The handler checks Execute on AgentProfile (and ownership where noted).

| Parameter | In | Type | Description |
|---|---|---|---|
| `tenantId` | path | string | Tenant ID (`default` for the seeded tenant). |
| `profileName` | path | string | Path parameter. |

**Status codes:** 200 OK, 401 Not authenticated, 403 Forbidden, 404 Not found.

Example:

```bash
curl -s -X POST "$HUGO/v1.0/a2a/default/Hugo" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc": "2.0", "id": 1, "method": "GetExtendedAgentCard", "params": {}}'
```

Response (200):

```text
{
  "jsonrpc": "2.0",
  "result": {
    "name": "Hugo",
    "description": "Default operations agent. Attach resources to give Agent Smith something to work with.",
    "version": "0.1.0-alpha",
    "supportedInterfaces": [
      {
        "url": "http://127.0.0.1:8800/v1.0/a2a/default/Hugo",
        "protocolBinding": "JSONRPC",
        "protocolVersion": "1.0"
      }
    ],
    "capabilities": {
      "streaming": true,
      "pushNotifications": false,
      "stateTransitionHistory": false
    },
    "provider": {
      "organization": "Hugo",
      "url": "https://github.com/jchristn/AgentSmith"
    },
    "skills": [
      {
        "id": "agent-smith",
        "name": "Agent Smith",
        "description": "Hugo (Agent Smith) investigates and operates the systems attached to it: SQLite (demo-db). Send a plain-language request; changes may require human approval.",
        "tags": [
          "operations",
          "investigation"
        ]
      },
      {
        "id": "sql",
        "name": "SQLite",
        "description": "Investigate and operate SQLite resources: demo-db (ReadOnly).",
        "tags": [
          "sql"
        ]
      }
    ],
    "defaultInputModes": [
      "text/plain",
      "application/json"
    ],
    "defaultOutputModes": [
      "text/plain"
    ],
    "securitySchemes": {
      "hugo": {
        "httpAuthSecurityScheme": {
          "scheme": "bearer",
          "description": "Hugo session token from POST /v1.0/token. Credentials may also send x-access-key and x-secret-key headers."
        }
      }
…
```

## Known differences from the OpenAPI document

The OpenAPI document is generated from route metadata and is right about paths, methods, and most fields. In `0.1.0-alpha` it differs from the wire in these places:

- Create routes answer 201, and `POST /v1.0/api/tool-invocations` and `POST /v1.0/api/resources/{id}/invoke` answer 202 while an approval is pending, but OpenAPI declares only 200 for them. `POST /v1.0/api/system/master-key/rotate` answers 202.
- Fields that accept any JSON (`arguments` on tool calls, `context` on render requests) appear in OpenAPI as an object with a `valueKind` property. Send a plain JSON object.
- The OpenAI-compatible request and response use snake_case (`stream_options`, `include_usage`, `max_tokens`, `finish_reason`, `owned_by`), while OpenAPI shows camelCase names.
- `GET /v1.0/api/server-info` sends `a2aUrl`; OpenAPI names it `a2AUrl`.
- `PUT /v1.0/api/secrets/{id}` and `PUT /v1.0/api/tenants/{tenantId}/prompt-templates/{id}` require `name` (and, for secrets, the unchanged `kind`) even though other updates are partial.
- `GET /v1.0/api/model-endpoints/{id}/models` answers 500 when the provider is unreachable; `validate` reports the same condition as a 200 with `success: false`.
- The 404 for an unknown route (`RouteNotFound`) has no `requestId`.
- A query filter with an unrecognized enum value (for example `GET /v1.0/api/approvals?status=Bogus`) is ignored rather than rejected, so the list comes back unfiltered.
