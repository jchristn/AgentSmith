# Hugo MCP API

Hugo is an MCP server. Point Claude Code, Cursor, Codex, or any other MCP client at it and the client's model gets a curated set of tools over your infrastructure, together with instructions that explain each resource and what it may do there. The model drives; Hugo holds the credentials, classifies every call as Read, Write, or Destructive, enforces the access mode of the binding, routes risky calls to a human, scrubs secrets from what comes back, and records everything.

This document covers the whole MCP surface of `0.1.0-alpha`: the endpoint and its transports, authentication and profile resolution, sessions, the `initialize` answer, how the tool list is built for each caller, every Hugo tool (meta, delegation, and management), the connector tools by family, the prompts, the resources, the approval flow, and the errors. The 111 connector tools of the infrastructure families are documented one by one in [docs/MCP_CONNECTOR_TOOLS.md](docs/MCP_CONNECTOR_TOOLS.md); the seven tools for external MCP servers and remote A2A agents are documented here. Client setup recipes are in [docs/MCP_CLIENTS.md](docs/MCP_CLIENTS.md).

Every example below was recorded against a running server. IDs differ on yours.

## Endpoint

| Method | Path | Purpose |
|---|---|---|
| POST | `/mcp` | JSON-RPC requests for the caller's resolved profile |
| POST | `/mcp/{profileName}` | The same, with the profile pinned in the path |
| GET | `/mcp`, `/mcp/{profileName}` | Optional server-to-client SSE stream of a session |
| DELETE | `/mcp`, `/mcp/{profileName}` | End a session |

MCP shares the REST port (8800 in the Docker stack). Internally Hugo runs a Voltaic MCP server on a loopback port and proxies these paths to it after authenticating the request; you never talk to the internal port. The transport is Streamable HTTP. Send `Accept: application/json, text/event-stream`: short requests come back as a JSON body, and requests that run long (a tool call waiting for approval, a slow query) come back as an SSE stream that carries keep-alive comments and progress notifications before the final `event: message`.

`Mcp.Enable` (default true) turns the surface on. With it off, every request answers 404 with JSON-RPC error -32601 `McpDisabled`.

## Authentication

MCP uses the REST schemes: `Authorization: Bearer <session token>` (or `x-token`), credential keys in `x-access-key` and `x-secret-key`, a signed request, or the administrator API key. A standing client configuration should use a credential, because session tokens expire after an hour. Create the credential bound to the profile the client should use and lock it there:

```bash
curl -s -X POST "$HUGO/v1.0/api/tenants/default/credentials" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"name": "claude-code", "agentProfileId": "agp_...", "lockToProfile": true}'
```

A request without valid credentials gets HTTP 401 with `WWW-Authenticate: Bearer realm="hugo"` (plus `error="invalid_token"` when credentials were sent and rejected) and a JSON-RPC body:

```json
{"jsonrpc":"2.0","id":1,"error":{"code":-32001,"message":"Unauthorized: authentication is required (Authorization: Bearer, or x-access-key with x-secret-key)."}}
```

## Which profile a caller gets

Every MCP request acts as one agent profile. Hugo resolves it in this order (decision D-018):

1. the profile in the path, `/mcp/{profileName}`;
2. the `x-hugo-profile` header;
3. the profile the credential is bound to;
4. the user's default profile;
5. the tenant's default profile (`Hugo` unless changed).

A credential with `lockToProfile` set can only act as its bound profile. The caller also needs Execute on the resolved profile, and the profile needs `exposeMcp`. Problems with the profile are HTTP errors from the proxy with a JSON-RPC body, never tool errors, so a misconfigured client fails at connect time rather than halfway through a task:

| HTTP | JSON-RPC code | Message starts with | Cause |
|---|---|---|---|
| 400 | -32602 | `TenantRequired` | A system administrator without a tenant (send `x-tenant-guid`) |
| 401 | -32001 | `Unauthorized` | No credentials, or invalid ones |
| 403 | -32003 | `ProfileLocked` | A locked credential asked for another profile |
| 403 | -32003 | `McpNotExposed` | The profile has `exposeMcp` off |
| 403 | -32003 | `Forbidden` | No Execute on the profile |
| 404 | -32602 | `ProfileNotFound` | No such profile in the tenant |
| 404 | -32601 | `McpDisabled` | `Mcp.Enable` is false |
| 502 | -32603 | `McpUnavailable` | The internal MCP server did not answer |
| 503 | -32603 | `DatabaseBusy`, `DatabaseUnavailable` | The database cannot serve the request right now |

```json
{"jsonrpc":"2.0","id":1,"error":{"code":-32003,"message":"McpNotExposed: agent profile Docs is not exposed over MCP."}}
```

## Sessions and stateless requests

Hugo negotiates every revision Voltaic 2.3.0 supports: 2024-11-05, 2025-03-26, 2025-06-18, 2025-11-25, and the stateless 2026-07-28.

With the session-based revisions, the first request is `initialize`. The answer carries an `Mcp-Session-Id` header, and every later request must send it; without it Hugo answers 400 (-32600, "Missing MCP-Session-Id header"). A session belongs to the principal, tenant, and profile that opened it. Sessions live in server memory (D-046): they end after `Mcp.SessionIdleTimeoutSeconds` (900) without traffic, on `DELETE`, or when the server restarts. An unknown or expired session answers 404, and the client is expected to initialize again, which every conforming client does:

```json
{"jsonrpc":"2.0","error":{"code":-32001,"message":"Session not found. Send an initialize request without an MCP-Session-Id header to start a new session."},"id":1}
```

The 2026-07-28 revision needs no session. Each request carries the protocol version, client info, and client capabilities in `params._meta`, plus the routing headers `MCP-Protocol-Version`, `Mcp-Method`, and, for `tools/call`, `prompts/get`, and `resources/read`, `Mcp-Name`. Discovery is `server/discover`:

```bash
curl -s -X POST "$HUGO/mcp" -H "Authorization: Bearer $TOKEN" \
  -H "Accept: application/json, text/event-stream" -H "Content-Type: application/json" \
  -H "MCP-Protocol-Version: 2026-07-28" -H "Mcp-Method: tools/call" -H "Mcp-Name: hugo_whoami" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"hugo_whoami","arguments":{},
       "_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28",
                "io.modelcontextprotocol/clientInfo":{"name":"curl","version":"1.0"},
                "io.modelcontextprotocol/clientCapabilities":{}}}}'
```

Stateless results include `"resultType": "complete"` and the server info under `_meta`. The examples in this document use whichever form is shorter; the results are the same in both modes.

## initialize

Hugo answers `initialize` itself, per caller, rather than letting the MCP library answer it the same way for everyone. `serverInfo` is `Hugo` with the product version, and `instructions` is the compiled instruction text of the resolved profile for the MCP surface: the persona, the safety rules, the MCP preamble, and one section per bound resource. Clients that pass `instructions` to their model (Claude Code does) start every conversation knowing what they are connected to. The same text is available as the `hugo_get_instructions` tool, the `hugo_briefing` prompt, the `hugo://profile/instructions` resource, and `GET /v1.0/api/agent-profiles/{id}/compiled-instructions?surface=Mcp`.

`params` must carry `protocolVersion`, a `capabilities` object, and `clientInfo` with `name` and `version`; anything less is -32602.

```bash
curl -s -i -X POST "$HUGO/mcp" -H "Authorization: Bearer $TOKEN" \
  -H "Accept: application/json, text/event-stream" -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"curl","version":"1.0"}}}'
```

```text
HTTP/1.1 200 OK
Content-Type: application/json
MCP-Session-Id: e5e5d879-0443-41af-b774-05d3416a6fdb
x-request-id: c75d42a22892472e97df49a65a42aca7

{"jsonrpc":"2.0","result":{"protocolVersion":"2025-06-18",
 "capabilities":{"logging":{},"completions":{},"prompts":{"listChanged":true},"resources":{"subscribe":true,"listChanged":true},"tools":{"listChanged":true}},
 "serverInfo":{"name":"Hugo","version":"0.1.0-alpha"},
 "instructions":"You are Agent Smith, an operations agent working for Mr. Anderson in the \"default\" environment. ... (about 12 KB)"},"id":1}
```

When the profile has more bindings than `Instructions.CompactBindingThreshold` (3) and its `instructionDetail` is Auto, each resource section is the connector's compact text and tells the model to call `hugo_describe_resource` for the full guidance (decision D-043). That keeps an eight-resource profile near 10 KB of instructions instead of 50 KB.

## The tool list

`tools/list` is answered per caller as well, because two callers on the same server see different tools. The list is built in this order and filtered as it goes:

- **Meta tools** (`hugo_*`), always: whoami, instructions, resources, describe, approvals, and await.
- **`ask_agent_smith`**, only when the profile has an inference endpoint.
- **Management tools** (`hugo_admin_*`), only when the profile has `exposeManagementTools` on and the caller is a tenant or system administrator, or holds Create on Resource or Update on Binding (credentials are judged by RBAC alone, never by their owner's administrator flags). Each is still authorized like its REST twin when called.
- **Connector tools** for each connector family with at least one enabled binding. A connector tool appears once however many resources of its family are bound, and its `resource` argument is an `enum` of the caller's aliases for that family (decision D-017), so the model cannot even name a resource it is not allowed to touch.

Connector tools are filtered further (the tool visibility rules in [docs/SAFETY_MODEL.md](docs/SAFETY_MODEL.md)). When every binding of a family is ReadOnly, the family's Write and Destructive tools are left out. Tools a binding rule denies on every binding are left out. Tools that classify each call (`sql_query`, `ssh_exec`, `redis_command`, `k8s_apply` and others) stay listed, with a description that names the access modes. Tools flagged as needing a model (`sql_ask`, `sql_generate_context`) appear only when the profile has an inference endpoint. A tool that is not in the operation scope map is never listed (fail closed). The result carries `cacheScope: "private"` because it is specific to the caller.

Each definition has `name`, `title`, `description`, `inputSchema`, and annotations. `readOnlyHint` is true for tools whose effect is always Read; `destructiveHint` is true for connector tools whose base effect is Destructive; `openWorldHint` is true for connector tools and false for Hugo's own. The connector descriptions end with the bound resources and their access modes:

```json
{
  "name": "sql_query",
  "title": "Run SQL query",
  "description": "Run one SQL statement and return rows as a table plus structured columns and rows. Every call is classified: SELECT, SHOW, EXPLAIN, and WITH ... SELECT are reads; ... Resources: docs-db (ReadWriteWithApproval). Each call is classified as Read, Write, or Destructive; ReadOnly resources reject Write and Destructive calls.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "resource": { "type": "string", "description": "Resource to act on (one of the resources this profile has bound for the tool).", "enum": ["docs-db"] },
      "sql": { "type": "string", "description": "One SQL statement." },
      "parameters": { "type": "object", "description": "Named parameter values referenced as @name in the SQL. Values may be strings, numbers, booleans, or null.", "additionalProperties": true },
      "maxRows": { "type": "integer", "minimum": 1, "maximum": 100000, "description": "Optional lower row cap for this call." }
    },
    "required": ["resource", "sql"],
    "additionalProperties": false
  },
  "annotations": { "readOnlyHint": false, "destructiveHint": false, "openWorldHint": true }
}
```

`GET /v1.0/api/agent-profiles/{id}/tools?surface=mcp` returns the same list over REST, which is handy when you want to see what a client will get before configuring it.

## Calling a tool

Every `tools/call`, from every client, goes through Hugo's tool invocation pipeline, the same one chat, A2A, and REST use. The arguments are validated against the tool's input schema (unknown arguments are rejected), the call is classified, the binding's access mode, tool rules, and deny patterns decide, approvals are requested when needed, and the output is bounded and scrubbed before it reaches the client.

A successful result has the human-readable text in `content` and, when the tool returns structured data, the same data in `structuredContent`:

```json
{"jsonrpc":"2.0","result":{
  "content":[{"type":"text","text":"1 row in 0.74 ms.\n| one | version |\n| --- | --- |\n| 1 | 3.53.3 |\n"}],
  "structuredContent":{"statementType":"SELECT","columns":[{"name":"one","type":"INTEGER"},{"name":"version","type":"TEXT"}],"rows":[["1","3.53.3"]],"rowCount":1,"rowLimit":500,"truncated":false,"elapsedMs":0.74}},"id":1}
```

A call that policy refuses, or that fails, is still a successful JSON-RPC response: a tool result with `isError: true`, text that starts with a stable code, and `structuredContent` of `{ "error": "<code>", "message": "..." }`. The model can read it and adjust, which is the point:

```json
{"jsonrpc":"2.0","result":{
  "content":[{"type":"text","text":"readonly_binding: The resource is ReadOnly for this profile; Write calls are not allowed (statement type CREATE)."}],
  "structuredContent":{"error":"readonly_binding","message":"The resource is ReadOnly for this profile; Write calls are not allowed (statement type CREATE)."},
  "isError":true},"id":1}
```

| Code | Meaning |
|---|---|
| `invalid_argument` | Arguments do not match the input schema, or a required one is missing |
| `binding_not_found`, `not_found` | No enabled binding with that alias, or no such approval, profile, or resource |
| `readonly_binding` | Write or Destructive call on a ReadOnly binding |
| `tool_denied` | A binding tool rule denies the tool |
| `binding_deny_pattern` | A deny pattern matched the primary argument |
| `tenant_lockdown` | The tenant is locked down; only reads run |
| `classifier_rejected` | The connector's own safety rules refused the arguments |
| `rejected`, `expired`, `cancelled` | The approval was rejected, ran out of time, or was cancelled; the message names who decided and their note |
| `forbidden` | The caller lacks the permission a management tool needs |
| `not_supported` | `ask_agent_smith` without an inference endpoint, or an operation the target does not support |
| `unmapped_tool` | A tool with no authorization mapping (should not happen; fails closed) |
| `timeout`, `upstream_error`, and connector codes | The call ran and the target failed; see [docs/MCP_CONNECTOR_TOOLS.md](docs/MCP_CONNECTOR_TOOLS.md) |

Calling a tool that does not exist at all is the one JSON-RPC error a tool call produces: -32602, "Tool 'nope_tool' was not found."

## Meta tools

These seven tools exist on every profile. They are Hugo talking about itself, and they are what a model reaches for when it is unsure what it is connected to.

### hugo_whoami

Tenant, principal, profile (with where the profile came from: `Explicit` for the path or the `x-hugo-profile` header, `Credential`, `User`, or `Tenant`), persona, and a summary of the bound resources. Input: none (`{}`).

```json
{
  "tenantId": "default", "tenantName": "default",
  "principalType": "User", "principalId": "usr_mv927k84_nPn37LyjKMeaRuwEUJk", "principalName": "anderson@hugo.local",
  "profileId": "agp_mv927keb_YuOf8qpccRHoxCYgG7V", "profile": "Hugo", "profileSource": "Tenant",
  "personaName": "Agent Smith", "userAddress": "Mr. Anderson",
  "accessSummary": "1 resource: 1 ReadOnly.",
  "resources": [
    { "alias": "demo-db", "connectorType": "sqlite", "connector": "SQLite", "family": "sql", "environment": "dev",
      "accessMode": "ReadOnly", "description": "In-memory SQLite database for examples", "notes": "Empty on every connection." }
  ],
  "askAgentSmith": false,
  "managementTools": false
}
```

That object is the `structuredContent`; the text content is the same JSON.

### hugo_get_instructions

The compiled instructions, exactly as `initialize` sends them, for clients that drop `initialize.instructions`. Input: none. Output: the text, no structured content.

### hugo_list_resources

The bound resources: alias (the value to pass as `resource`), connector type and name, family, environment, access mode, description, and operator notes. Input: none. Output: `{ "profile": "Hugo", "resources": [ ... ] }` with the same resource objects as `hugo_whoami`.

### hugo_describe_resource

The full guidance for one bound resource: the heading line with connector, environment, and access mode, the connector's complete instructions (not the compact form), the operator notes, durable knowledge such as the database context, the limits, and the tools available on it. The compiled instructions point here when they only summarize a resource.

| Argument | Type | Required | Description |
|---|---|---|---|
| `resource` | string, enum of the caller's aliases | yes | Alias of a bound resource |

The result is Markdown text that begins like this:

```text
## docs-db (SQLite, environment: dev, access: ReadWriteWithApproval)

In-memory SQLite database

### SQLite resources

This resource is a SQLite database file on the Hugo server. You reach it through the `sql_*` tools, always naming the resource. ...
```

### hugo_list_approvals

The caller's pending and recent approvals, newest first.

| Argument | Type | Required | Description |
|---|---|---|---|
| `status` | Pending, Approved, Rejected, Expired, Cancelled | no | Only approvals with this status |
| `maxResults` | integer 1 to 100 | no | Default 25 |

```json
{"approvals":[{"approvalId":"apr_mv939bxr_lTeC7bnjmIJsMnEOlha","toolName":"sql_query","resource":"docs-db",
  "summary":"Run SQL on docs-db: CREATE TABLE notes (id INTEGER)","effect":"Write","status":"Pending",
  "createdUtc":"2026-10-15T05:18:52.335666Z","expiresUtc":"2026-10-15T05:33:52.335665Z"}],"totalRecords":1}
```

Decided approvals add `decidedBy` and `decisionNote`.

### hugo_await_approval

Wait for a pending approval and return the original call's result once a human decides it. The call to wait for comes from an earlier result with `status: approval_pending`.

| Argument | Type | Required | Description |
|---|---|---|---|
| `approvalId` | string | yes | `apr_...` from the pending result |
| `waitSeconds` | integer 0 to 600 | no | How long to wait. Default `Approvals.McpWaitSeconds` (90) |

The caller must be the principal that requested the approval or hold Read on Approval; anyone else gets `not_found`. While waiting, Hugo sends progress notifications every `Mcp.ProgressIntervalSeconds` (10) to clients that sent a progress token. If the approval is still pending when the wait ends, the result is the same non-error `approval_pending` result again. Once the approval is decided, the result is the original tool's result (approved and executed) or a tool error (`rejected`, `expired`, `cancelled`) such as `rejected: The approval was rejected by anderson@hugo.local: no`. An approved call:

```json
{"content":[{"type":"text","text":"Statement executed; 0 row(s) affected in 0.04 ms. "}],
 "structuredContent":{"statementType":"CREATE","columns":[],"rows":[],"rowCount":0,"rowLimit":500,"truncated":false,"recordsAffected":0,"elapsedMs":0.04}}
```

Known issue in `0.1.0-alpha`: if the approval is decided while a `hugo_await_approval` call is already waiting on it, Hugo hands the decision to that waiter but nobody executes the call, and the await returns `approval_pending` at the end of its wait while the invocation stays Pending. Approving before calling `hugo_await_approval` (or while nothing waits) works: the call runs on approval and `hugo_await_approval` returns its result at once.

### ask_agent_smith

Hand a whole task to Hugo's own agent loop instead of driving the tools yourself (decision D-012). Hugo runs a turn as the caller's principal on the resolved profile, with that profile's model, tools, and instructions, and returns the final answer with a compact trace of the tool calls it made. Listed only when the profile has an inference endpoint; on a profile without one, calling it is a `not_supported` tool error.

| Argument | Type | Required | Description |
|---|---|---|---|
| `message` | string, at least 1 character | yes | The task or question |
| `conversationId` | string | no | Continue an earlier conversation (from a previous answer) |

```json
{"content":[{"type":"text","text":"Mr. Anderson, demo-db answered. It is an in-memory SQLite database, so it holds no tables until something creates them. Nothing needs your attention.\n\nTool trace:\n- sql_list_tables on demo-db: Allowed, Read, ok, 0 ms\n\nconversationId: cnv_mv937yuw_vd5nchjibih0aLa1L9M"}],
 "structuredContent":{"status":"completed","answer":"Mr. Anderson, demo-db answered. ...","conversationId":"cnv_mv937yuw_vd5nchjibih0aLa1L9M","toolTrace":["sql_list_tables on demo-db: Allowed, Read, ok, 0 ms"]}}
```

(That answer came from a scripted test model; yours will be written by the profile's real model.) When a call inside the turn needs approval and nobody decides within `Approvals.McpWaitSeconds`, `status` is `approval_pending` with `approvalId` and a `next` hint. A failed turn is an `agent_failed` tool error. Hugo's own loop never offers `ask_agent_smith` or the `hugo_*` tools to its model, so delegation cannot recurse (D-047).

## Management tools

Management tools let an MCP client administer Hugo itself: look at profiles and resources, create a resource, change a binding's access mode. They are off by default. They appear only when the profile has `exposeManagementTools` set and the caller qualifies (an administrator user, or a principal with Create on Resource or Update on Binding), and every call is authorized against the same resource type and operation as the REST route it mirrors, so a tool showing up is not the same as being allowed to use it. Secrets are never accepted or returned: a resource references a secret by slot and ID.

| Tool | Permission | Arguments | Result |
|---|---|---|---|
| `hugo_admin_list_profiles` | Read on AgentProfile | `search` (string), `maxResults` (1 to 100, default 25) | The enumeration envelope of profiles, as `GET /v1.0/api/agent-profiles` |
| `hugo_admin_describe_profile` | Read on AgentProfile | `profile` (name or ID), required | `{ "profile": {...}, "bindings": [...] }` |
| `hugo_admin_list_resources` | Read on Resource | `search`, `maxResults`, `connectorType` | The enumeration envelope of all tenant resources, not only bound ones |
| `hugo_admin_describe_resource` | Read on Resource | `resource` (name or ID), required | `{ "resource": {...}, "referrers": [...] }`; settings shown, secrets never |
| `hugo_admin_list_bindings` | Read on Binding | `profile` (name or ID), required | `{ "profile": "Docs", "bindings": [...] }` |
| `hugo_admin_create_resource` | Create on Resource | `name` and `connectorType` required; `displayName`, `description`, `endpoint`, `environment`, `notes`, `defaultAccessModeCeiling`, `settings` (object), `secretBindings` (`[{ "slotName", "secretId" }]`) | A sentence plus the created resource |
| `hugo_admin_update_binding` | Update on Binding | `profile`, `binding` (alias or ID), and `accessMode` required; `requireApprovalForDestructive`, `enabled` | A sentence plus the updated binding |

```json
{"content":[{"type":"text","text":"Binding docs-db of profile Docs is now ReadOnly.\n\n{\"id\":\"bnd_mv939bwv_VayoJBTjO3RfcuiXPeA\", ... }"}],
 "structuredContent":{"id":"bnd_mv939bwv_VayoJBTjO3RfcuiXPeA","alias":"docs-db","accessMode":"ReadOnly","requireApprovalForDestructive":true, "...":"..."}}
```

`hugo_admin_create_resource` and `hugo_admin_update_binding` change configuration, so their annotations say `readOnlyHint: false`; the rest are read-only. They do not go through the approval queue, which is one more reason to leave them off on profiles that untrusted callers can reach.

## Connector tools

Connector tools are named `<family>_<operation>` and take a required `resource` argument. There are 118 of them in this release. The table summarizes each family; [docs/MCP_CONNECTOR_TOOLS.md](docs/MCP_CONNECTOR_TOOLS.md) documents every one of the infrastructure tools with its input schema, effect classification, output shape, and an example, and [docs/CONNECTORS.md](docs/CONNECTORS.md) explains how to configure each connector.

| Family | Connector types | Tools | What they cover |
|---|---|---|---|
| `ssh` | ssh | 8 | System overview, shell commands (classified per call), service status and control, reading, tailing, listing, and writing files |
| `storage` | s3, azure-blob, gcs | 8 | List, metadata, read, stats, presigned URLs, write, copy, delete |
| `sql` | sqlite, mysql, postgresql, sqlserver | 16 | Schemas, tables, relationships, schema crawl, guarded queries, explain, activity, locks, sizes, the agent pack, database intelligence, durable context, and question answering (`sql_ask` and `sql_generate_context` need a model) |
| `logs`, `swo` | papertrail, solarwinds-observability | 9 | Search, tail, volume, groups, systems, saved searches; SolarWinds entities and metrics |
| `redis` | redis | 13 | Info, scan, get, TTLs, memory, big keys, clients, slow log, latency, set, expire, delete, classified commands |
| `aws` | aws | 17 | Caller identity, EC2, Auto Scaling, ECS, ELB, RDS, Lambda, CloudWatch metrics and alarms, Logs Insights and tail, Route 53, SNS, SQS |
| `k8s` | kubernetes | 14 | List, get, describe, events, logs, top, rollout status, history, and restart, scale, cordon, exec, apply, delete |
| `docker` | docker | 8 | ps, inspect, logs, stats, images, events, exec, container actions |
| `http` | http | 2 | Health checks and allow-listed requests |
| `prom` | prometheus | 7 | Instant and range queries, series, label values, targets, rules, alerts |
| `loki` | loki | 4 | LogQL instant and range queries, labels, label values |
| `es` | elasticsearch | 5 | Cluster health, indexes, mappings, search, count |
| `mcp` | mcp | 4 | Tools and resources of an external MCP server (below) |
| `a2a` | a2a-agent | 3 | Cards, delegation, and tasks of a remote A2A agent (below) |

### External MCP servers (`mcp_*`)

An `mcp` resource attaches another MCP server, over Streamable HTTP or (when both the resource and `Connectors.McpAllowStdio` allow it) as a stdio child process of the Hugo host. Rather than re-exporting each remote tool under its own name, Hugo exposes four fixed tools that name the remote tool as an argument (decision D-044). The remote server's instructions and outputs are fenced as untrusted third-party content.

| Tool | Effect | Arguments | Result |
|---|---|---|---|
| `mcp_list_tools` | Read | `filter` (text), `includeSchemas` (default true), `refresh` (default false: use the cached list for `refreshIntervalMinutes`) | Listing text plus `{ server, endpoint, protocolVersion, instructions, discovered, allowed, matched, shown, incomplete, fetchedUtc, tools[{ name, title, description, effect, readOnlyHint, destructiveHint, idempotentHint, openWorldHint, inputSchema, schemaOmitted }] }` |
| `mcp_call_tool` | per call | `name` (required, exact remote name), `arguments` (object, default `{}`, passed through unchanged) | The remote content as text plus `{ tool, server, effect, isError, content[], structuredContent }`. A remote `isError` result becomes a tool error with code `upstream_error` |
| `mcp_list_resources` | Read | `filter` | `{ server, discovered, shown, incomplete, resources[{ uri, name, title, description, mimeType, size }] }`, or `not_supported` when the server has none |
| `mcp_read_resource` | Read | `uri` (required) | Text contents up to the output limits plus `{ uri, server, contents[] }`; binary contents are summarized |

`mcp_call_tool` takes its effect from the remote tool's annotations: `readOnlyHint` makes it Read, `destructiveHint` makes it Destructive, anything else is Write, and a tool Hugo has not discovered yet is treated as Destructive. So list before you call, or every call on an approval-gated binding will wait for a human. `includeTools` and `excludeTools` on the resource decide which remote tools agents may see and call.

### Remote A2A agents (`a2a_*`)

An `a2a-agent` resource is another agent reachable over A2A. Its card is the remote agent's own description, and Hugo treats what comes back from it as data rather than instructions.

| Tool | Effect | Arguments | Result |
|---|---|---|---|
| `a2a_get_card` | Read | `refresh` (default false) | Summary plus `{ name, description, version, provider, documentationUrl, cardUrl, endpoint, binding, streaming, streamingUsed, defaultInputModes, defaultOutputModes, configuredSkillId, configuredSkillFound, skillCount, skills[{ id, name, description, tags, examples }], fetchedUtc }` |
| `a2a_delegate` | Write | `message` (required; include everything, the remote agent cannot see this conversation), `contextId` (continue a conversation), `taskId` (answer an input-required task, with `contextId`), `data` (object sent as a data part) | Status, the reply fenced as untrusted content, and artifacts, plus `{ agent, status, terminal, needsInput, taskId, contextId, message, artifacts[], artifactCount, next }`. Failed, rejected, and canceled tasks are tool errors |
| `a2a_get_task` | Read | `taskId` (required), `historyLength` (0 to 50) | The same shape as `a2a_delegate`, for a task that timed out or needs re-reading |

`a2a_delegate` waits up to the resource's `timeoutSeconds` (120) for the remote task to finish or ask for input. A remote agent asking for input is an ordinary tool result with `needsInput: true`, not a Hugo approval.

## The approval flow

A call that needs a human does not fail. Here is what an MCP client sees, end to end, for a CREATE on a ReadWriteWithApproval binding:

1. The client calls `sql_query`. Hugo classifies the statement as Write, creates approval `apr_...` (visible in the dashboard inbox, `GET /v1.0/api/approvals`, and the `hugo://approvals/pending` resource), and holds the call open. The response switches to an SSE stream that carries keep-alives and, for clients that sent a progress token, a progress notification every `Mcp.ProgressIntervalSeconds`.
2. If someone approves within `Approvals.McpWaitSeconds` (90), the call runs and the client receives its normal result on the same request. If someone rejects, the client receives a `rejected` tool error.
3. If nobody decides in time, the client receives a non-error result instead:

```json
{"content":[{"type":"text","text":"Approval apr_mv92egct_TFexR8efZ0aNKkpJTPV is pending: Run SQL on demo-db: CREATE TABLE t (id INTEGER). It expires at 2026-10-15 05:09:51Z; next: call hugo_await_approval with this approvalId."}],
 "structuredContent":{"status":"approval_pending","approvalId":"apr_mv92egct_TFexR8efZ0aNKkpJTPV","summary":"Run SQL on demo-db: CREATE TABLE t (id INTEGER)","expiresUtc":"2026-10-15T05:09:51.7260680Z","next":"call hugo_await_approval with this approvalId"}}
```

4. A person decides in the dashboard or with `POST /v1.0/api/approvals/{id}/approve` (or `reject`). With nobody waiting, approving runs the call right away.
5. The client calls `hugo_await_approval` with the `approvalId` and receives the original call's result.

Approvals expire after `Approvals.ExpirySeconds` (900). Turning on tenant lockdown cancels every pending approval. The full rules, including who may approve, are in [docs/SAFETY_MODEL.md](docs/SAFETY_MODEL.md).

## Prompts

`prompts/list` returns three prompts. They exist so a person in an MCP client can start common work with one command.

| Prompt | Arguments | What it returns |
|---|---|---|
| `hugo_briefing` | none | The compiled instructions as a user message (description "Briefing for profile Hugo") |
| `hugo_investigate` | `symptom` (required), for example `checkout is slow` | An investigation brief naming every bound resource and asking for read-only checks first |
| `hugo_health_sweep` | none | A request to check every bound resource with the cheapest read and report a status table |

```json
{"description":"Investigate checkout is slow","messages":[{"role":"user","content":{"type":"text","text":"Investigate checkout is slow across my stack.\n\nResources you can use: demo-db (SQLite, ReadOnly).\n\nStart with read-only checks: recent errors in the logs, the health and load of the hosts and databases involved, and anything that changed recently. Call hugo_describe_resource for a resource's full guidance before you use it. Correlate what you find by time, name the most likely cause with the evidence, and propose a fix. Do not make changes unless I ask; any Write or Destructive call may need my approval."}}]}
```

`hugo_investigate` without `symptom` is -32602.

## Resources

| URI | MIME type | Contents |
|---|---|---|
| `hugo://profile/instructions` | text/markdown | The compiled instructions |
| `hugo://approvals/pending` | application/json | The caller's pending approvals, `{ "approvals": [...], "totalRecords": n }` |
| `hugo://resources/{alias}` | text/markdown | One bound resource's full guidance, the same text as `hugo_describe_resource` (template) |

`resources/list` returns the first two plus one entry per binding (`hugo://resources/demo-db`, titled `demo-db (SQLite, ReadOnly)`); `resources/templates/list` returns the `hugo://resources/{alias}` template. Reading an alias that is not bound is JSON-RPC error -32002. Both lists carry `cacheScope: "private"`.

```json
{"contents":[{"uri":"hugo://approvals/pending","mimeType":"application/json","text":"{\"approvals\":[],\"totalRecords\":0}"}]}
```

## Other methods

In a session, `ping` answers `{}` and `logging/setLevel` is accepted. The stateless 2026-07-28 revision has neither and answers -32601 for them. `completion/complete` is accepted in both modes and currently returns no suggestions.

## Configuring clients

Claude Code, a project `.mcp.json`, other HTTP clients, and the `hugo mcp stdio` relay for clients that can only launch stdio servers are covered with copy-paste configuration in [docs/MCP_CLIENTS.md](docs/MCP_CLIENTS.md). The short version for Claude Code:

```bash
claude mcp add --transport http hugo http://127.0.0.1:8800/mcp \
  --header "x-access-key: $HUGO_ACCESS_KEY" \
  --header "x-secret-key: $HUGO_SECRET_KEY"
```

Use a credential bound and locked to the profile you want Claude Code to have. It will then receive that profile's instructions and exactly that profile's tools, no matter what it asks for, and every call it makes will show up under Tool Invocations with the credential's name on it.
