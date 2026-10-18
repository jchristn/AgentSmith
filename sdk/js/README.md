# Hugo JavaScript and TypeScript SDK

Audit summaries return total and per-event counts for matching records: `await client.audit.summary({ eventType: "AuthorizationDenied" })`. They accept the audit list filters and ignore pagination.

`@jchristn/hugo-sdk` is the TypeScript client for the Hugo REST API. It is built on `fetch`, ships ESM and CommonJS builds with type declarations, and works on Node 18 and later (and in Deno, Bun, and browsers that provide `fetch` and WebCrypto). Version `0.1.0-alpha`, matching the server.

Every client (identity, secrets, request history, audit, settings, events, connectors, resources, knowledge, agent profiles, bindings, ask, prompt layers, model endpoints, tool invocations, approvals, lockdown, conversations and chat streaming, and analytics) is verified against a live server by the harness. Models are permissive: fields the server adds later are kept on the returned objects.

## Install

```bash
npm install @jchristn/hugo-sdk
```

From this repository:

```bash
cd sdk/js
npm install
npm run build
```

## Quick start

```ts
import { HugoClient, HugoError } from "@jchristn/hugo-sdk";

const hugo = new HugoClient("http://127.0.0.1:8800");

const token = await hugo.login("anderson@hugo.local", "password", "default");
if (token.mustChangePassword) {
  // Advisory only: the account still has its seeded or reset password. Every route works either way.
  await hugo.changePassword("password", "a-new-long-password");
}

// Store credential material in the vault (values are write-only). Password secrets use the field "password".
const secret = await hugo.secrets.create({ name: "web-01-password", kind: "Password", values: { password: "s3cret" } });

// Create an SSH resource that reads the secret through its "password" slot (authMethod defaults to PrivateKey).
const resource = await hugo.resources.create({
  name: "web-01",
  connectorType: "ssh",
  settings: { host: "web-01.example.com", port: 22, username: "ops", authMethod: "Password" },
  secretBindings: [{ slotName: "password", secretId: secret.id }],
});

// Bind it to a profile and look at what an MCP caller would receive.
const profile = await hugo.agentProfiles.create({ name: "ops" });
await hugo.agentProfiles.bindings.create(profile.id, { resourceId: resource.id, alias: "web-01", accessMode: "ReadOnly" });
const instructions = await hugo.agentProfiles.compiledInstructions(profile.id, "Mcp");

// Would this call be allowed? Nothing is executed.
const outcome = await hugo.toolInvocations.dryRun({ profileId: profile.id, resource: "web-01", toolName: "ssh_exec", arguments: { command: "uptime" } });
console.log(`Dry run decision: ${outcome.decision} (${outcome.effect}: ${outcome.effectReason})`);
// Dry run decision: Allowed (Read: 'uptime' is read-only)

// Chat with streaming (the profile needs an inference endpoint: set inferenceEndpointId).
const conversation = await hugo.conversations.create({ agentProfileId: profile.id });
for await (const event of hugo.conversations.streamMessage(conversation.id, "Is nginx healthy on web-01?")) {
  if (event.type === "assistant.delta") process.stdout.write(event.payload?.text ?? "");
  if (event.type === "approval.required") console.log("\napproval needed:", event.payload);
  if (event.type === "turn.completed") break;
}

// Or delegate a task and wait for the answer.
const answer = await hugo.agentProfiles.ask(profile.id, "Summarize disk usage on web-01.");
console.log(answer.status, answer.finalText);
```

`examples/quickstart.mjs` runs the same flow against a live server and cleans up (`npm run build` first; set `HUGO_SDK_SERVER`, `HUGO_EMAIL`, `HUGO_PASSWORD`, and `HUGO_TENANT`).

## Authentication

| Method | What it sends | Use it for |
|---|---|---|
| `login(email, password, tenantId?)` | `POST /v1.0/token` with `x-email`, `x-password`, `x-tenant-id`; stores the bearer token and tenant | Users. The token refreshes automatically near expiry (opt out with `{ autoRefresh: false }`). |
| `loginWithAccessKey(accessKey, secretKey)` | `POST /v1.0/token` with `x-access-key`, `x-secret-key`; stores the token | Credentials in `DirectHeader`, `SessionExchange`, or `Hybrid` mode. `SessionExchange` keys work only here (401 on other routes). |
| `useToken(token, tenantId?)` | `Authorization: Bearer ...` | A token you already have. |
| `useAccessKey(accessKey, secretKey, tenantId?)` | `x-access-key`, `x-secret-key` on every request | Credentials in `DirectHeader` or `Hybrid` mode. |
| `useSignedRequests(accessKey, secretKey, tenantId)` | `x-access-key`, `x-date`, `x-nonce`, `x-signature`, `x-tenant-id` | Credentials in `SignedRequest` or `Hybrid` mode. The secret never leaves the process. |
| `useAdminApiKey(apiKey, tenantId?)` | `x-api-key` and `x-tenant-id` | System administrators, when the server sets `HUGO_ADMIN_API_KEY`. |

Session helpers: `refresh()`, `logout()`, `validateToken()`, `tokenDetails()`, `reauthenticate(password)` (required shortly before `secrets.reveal`), `changePassword(current, next)`, and `lookupTenants(email)`.

`mustChangePassword` on the login response, `tokenDetails()`, and users is advisory. Seeded and administrator-reset accounts carry it, the server blocks no route because of it, and `changePassword` clears it.

A credential's access comes only from its role assignments: pass `roleName` (for example `Viewer` or `Operator`) to `credentials.createWithSecret`, or add `credentialScopeAssignments`. A credential without one gets 403 everywhere. It carries its owner's administrator or tenant administrator bypass only when `inheritOwnerBypass` is true, which only a signed-in tenant or system administrator can set (403 `BypassRequiresAdministrator` otherwise); every change is audited as `CredentialBypassChanged`.

### Rate limits

`POST /v1.0/token`, `reauthenticate`, and other sign-in routes are rate limited per source IP and per identity (`Auth.RateLimitPerMinute`, default 10, in fixed one-minute windows). Over the limit the server answers 429 `RateLimited` with `Retry-After: 60`, and `HugoError.retryAfter` carries the header. Set `rateLimitRetries` to have the client wait and retry instead:

```ts
const hugo = new HugoClient("http://127.0.0.1:8800", { rateLimitRetries: 3, rateLimitMaxWaitSeconds: 90 });
```

Each retry waits `Retry-After` seconds (exponential backoff from one second when the header is missing), capped by `rateLimitMaxWaitSeconds` (default 90). The default is 0 retries.

Signed requests match the server's `RequestSigner`: the canonical string is `METHOD\nPATH\nQUERY\nTENANT\nDATE\nNONCE\nhex(SHA-256(body))`, where `PATH` is the raw path, `QUERY` is the raw query string exactly as sent, `TENANT` is the credential's tenant ID, and `DATE` is `yyyy-MM-ddTHH:mm:ssZ` UTC. The signature is lowercase hex HMAC-SHA256 with the secret key. The SDK uses WebCrypto (`globalThis.crypto.subtle`), falling back to `node:crypto`'s `webcrypto` on Node 18. `canonicalize`, `sign`, and `signRequest` are exported if you need to sign requests yourself.

## Clients

Identity and administration routes are tenant scoped (`/v1.0/api/tenants/{tenantId}/...`) and use `client.tenantId`, which `login` sets. Hugo domain routes are flat (`/v1.0/api/resources`, `/v1.0/api/agent-profiles`, ...) and act on the caller's own tenant.

A system administrator can address another tenant: set `client.tenantId` for tenant-scoped routes, and `client.tenantOverride` (or the `tenantOverride` constructor option) for flat routes, which then carry `?tenantId=<override>` (included in signed requests). `tenantOverride` is never added to `/v1.0/api/tenants...` paths.

| Property | Routes |
|---|---|
| `tenants` | list, get, create, update, delete |
| `users` | CRUD, `me()`, `updateMe()`, `permissions(id)`, `resetPassword(id, newPassword, mustChangePassword?)` |
| `credentials` | CRUD, `createWithSecret()` (returns the secret once), `rotate(id)`, `permissions(id)` |
| `roles`, `permissions` | CRUD, `roles.clone(id, body)` |
| `rolePermissions` | `list({ roleId })`, `create(roleId, permissionId)`, `delete(id)` |
| `userRoleAssignments`, `credentialScopeAssignments` | CRUD (filter lists with `userId` or `credentialId`) |
| `sessions` | `list({ principalType, userId, credentialId })` (each session carries `principalName`), `get`, `revoke` |
| `audit` | `list(filter)`, `get`, `delete`, `deleteMany(filter)` |
| `secrets` | CRUD (update keeps omitted name, kind, and description), `list({ kind })`, `setValues`, `referrers`, `parse`, `reveal(id, reason)`. Kinds include `BearerToken` (field `token`, sent as `Authorization: Bearer`). |
| `promptDocuments` | Versioned reusable Markdown: `list`, `get`, `create`, `update`, `delete(id, expectedVersion)`, `revisions`, `revision`, `referrers` |
| `agentPromptDocuments` | Profile attachments: `list`, `create`, `update`, `delete(profileId, attachmentId, expectedVersion, expectedDocumentSetVersion)`, `reorder`, `preview` |
| `promptTemplates` | Tenant-scoped records: CRUD, `versions`, `version`, `restore`, `clone`, `reset`, `render`, `renderAdHoc` |
| `promptLayers` | Layered templates by key at `/v1.0/api/prompt-templates`: `list`, `get`, `save(key, content, changeNote?, layer)`, `reset`, `versions`, `builtin`, `diff`, `render` |
| `requestHistory` | `list(filter)`, `summary`, `get`, `delete`, `deleteMany` |
| `events` | `stream({ signal })`: async iterator of tenant events |
| `settings`, `system` | settings read and update; health, info, `observabilityStatus`, master key status and rotation, restart |
| `connectors` | `list`, `get(type)` (settings schema, secret slots, tools) |
| `resources` (+ `resources.knowledge`) | CRUD, `list({ connectorType, environment, health })`, `test`, `testUnsaved`, `duplicate`, `referrers`, `invoke`; knowledge `list`, `listScope`, `get`, `put`, `delete`, `versions` (the scope root entry uses key `_root`) |
| `agentProfiles` (+ `agentProfiles.bindings`) | CRUD (profiles carry `bindingCount`), `duplicate`, `compiledInstructions(id, surface?)`, `compiledInstructionsText`, `tools(id, surface?)`, `card`, `ask(id, message, conversationId?)`; bindings CRUD, `toolRules`, `setToolRules` |
| `modelEndpoints` | CRUD, `list({ kind, provider, health })`, `validate(id)`, `validateDraft(body)` (unsaved settings, stores nothing), `models(id)` (502 `ProviderUnreachable` or `ProviderTimeout` when the provider cannot be reached), `healthHistory(id, maxResults?)` |
| `toolInvocations` | `list({ ..., effect, fromUtc, toUtc })`, `get`, `summary`, `dryRun`, `invoke` (202 with `approvalId` while approval is pending) |
| `approvals` | `list({ status, ..., effect, fromUtc, toUtc })`, `get`, `approve`, `reject`, `cancel`, `awaitDecision(id, { timeoutMs, pollMs })` |
| `lockdown` | `get`, `enable(reason)`, `disable` |
| `conversations` | CRUD, `list({ agentProfileId, source })`, `messages(id, query?)` (a page of messages), `sendMessage(id, message)` (the turn result), `streamMessage(id, message)`, `cancel`, `export(id, "md" \| "json")` |
| `analytics` | `overview`, `tokens`, `tools`, `models`, each with `{ from, to, interval }` (`minute`, `hour`, or `day`; default the last seven days) |

List routes take `{ maxResults, skip, ordering, search, activeOnly, includeBuiltIn }` plus route filters and return `{ objects, totalRecords, endOfResults, ... }`. An unknown enum value in a filter (for example `effect: "Explosive"`) answers 400 `ValidationFailed` with the allowed values. Every collection also has `all(query)`, an async iterator over every page.

## Errors

Every non-success status throws `HugoError`:

```ts
try {
  await hugo.secrets.get("sec_missing");
} catch (e) {
  if (e instanceof HugoError) {
    console.log(e.status, e.error, e.code, e.serverMessage, e.requestId, e.fields);
  }
}
```

`status` is the HTTP status, `error` the HTTP-level name (`NotFound`), `code` the stable code (`RouteNotFound`, `ReauthenticationRequired`, `ValidationFailed`, `RateLimited`, `TurnInProgress`, ...), and `requestId` comes from the body or the `x-request-id` header. `isRouteNotFound` tells a missing route apart from a missing record. `retryAfter` holds the `Retry-After` header when the server sent one.

## Events and streaming

```ts
const controller = new AbortController();
for await (const event of hugo.events.stream({ signal: controller.signal })) {
  console.log(event.event, event.json());
  if (event.event === "secret.created") controller.abort();
}
```

The events stream and chat streaming share one SSE parser (`SseParser`, `parseSseStream`), also exported. Chat events are `turn.started`, `assistant.delta` (`payload.text`), `assistant.thinking`, `tool.proposed`, `tool.started`, `tool.completed`, `approval.required`, `approval.resolved`, `turn.completed`, and `error`; the stream ends with `stream.closed` (`conversationId`, `turnId`, `finishReason`, `title`). Without streaming, `sendMessage` returns the turn result (`finalText`, `messages`, `toolCalls`, token counts, `finishReason`). A second message while a turn is running answers 409 `TurnInProgress`.

## MCP and A2A

Hugo also serves MCP at `/mcp` and A2A at `/v1.0/a2a/{tenantId}/{profileName}`. `await hugo.info()` returns `mcpUrl` and `a2aUrl` for the server you are talking to.

## Test harness

The Vitest harness runs against a live server. Global setup signs in as the administrator, creates a fresh tenant and a tenant administrator, and deletes the tenant at the end, so it can run against any server.

```bash
npm install
npm run build        # the build test checks the ESM and CJS output
npm run typecheck
npm test
```

| Variable | Default | Meaning |
|---|---|---|
| `HUGO_SDK_SERVER` | `http://127.0.0.1:8800` | Server base URL |
| `HUGO_SDK_ADMIN_EMAIL` | `admin@hugo.local` | System administrator email |
| `HUGO_SDK_ADMIN_PASSWORD` | `password` | Its password. The advisory `mustChangePassword` flag on a fresh server is left alone. |
| `HUGO_SDK_ADMIN_TENANT` | `default` | The administrator's tenant |
| `HUGO_SDK_ADMIN_API_KEY` | (unset) | Enables the administrator API key test (also read from `HUGO_ADMIN_API_KEY`) |


Domain tests run against real routes: an HTTP resource pointed at the closed port `127.0.0.1:9`, profiles, bindings, dry runs, approvals (pending, reject, approve, cancel), lockdown, prompt layers, model endpoints, and the tenant override. Tests that need a model (`test/model.test.ts`: draft validation, health history, conversations, chat streaming, ask, and analytics) start `test/stub-llm.ts`, a small OpenAI-compatible server on a free `127.0.0.1` port, and point an `OpenAICompatible` model endpoint at it, so no real model is needed. Credentials the harness creates get an explicit role. Harness clients set `rateLimitRetries`, so the run works with the server's default `Auth.RateLimitPerMinute` (some tests wait out a one-minute window). Loopback addresses use `127.0.0.1`, never `localhost`.

## Managed agent documents

From `sdk/js`, run the local-file example with environment-provided test credentials:

```bash
npm run build
node examples/documents.mjs
# Optional custom local file:
node examples/documents.mjs /path/to/schema.md
```

Document methods accept an optional `AbortSignal`. The example uses `promptDocuments` and `agentPromptDocuments`; previews take `{ audience: "Authenticated" }` or `{ audience: "Public" }`.

Set `HUGO_EMAIL` and `HUGO_PASSWORD` for an isolated test tenant before running. `HUGO_SDK_SERVER` defaults to `http://127.0.0.1:8800`; `HUGO_TENANT` defaults to `default`. The test user needs document CRUD/Read and profile Create/Read/Update/Delete permissions. Hosted Reference previews also require the agent’s configured model to support tools. No inference request is sent by these examples.

The example reads `sdk/fixtures/LOG_MESSAGE_STRUCTURE.md` from the client machine, uploads its text as a managed copy with a unique filename, creates a temporary agent, attaches the document as Reference, lists attachments, previews delivery, switches to Inline, then detaches and removes its own records. Hugo never receives or opens the client’s filesystem path. Public delivery stays disabled.

Reference includes metadata in the system prompt and delivers content through `agent_read_document` when requested; that tool result consumes context. Inline includes the whole selected revision in system instructions. A reference cannot guarantee that a particular model consults it. `Latest` follows shared edits; `Pinned` selects an immutable revision.

Document edits/deletes use the document’s `version`, not `currentRevision`. Attachment mutations also require the current `documentSetVersion` from the attachment list. Refresh after changes, preserve omitted fields on partial updates, and handle 409 conflicts without blind retries. Delete attached documents only after detaching them; profile deletion removes its attachments while retaining reusable documents. Readiness issues in a preview must be addressed before relying on delivery. See [the operator guide](../../docs/AGENT_DOCUMENTS.md) for limits, permissions and public disclosure.
