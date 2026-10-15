# Hugo JavaScript and TypeScript SDK

`@jchristn/hugo-sdk` is the TypeScript client for the Hugo REST API. It is built on `fetch`, ships ESM and CommonJS builds with type declarations, and works on Node 18 and later (and in Deno, Bun, and browsers that provide `fetch` and WebCrypto). Version `0.1.0-alpha`, matching the server.

> **Preview:** the domain clients (resources, connectors, agent profiles, model endpoints, tool invocations, approvals, lockdown, knowledge, conversations) are verified against the server when Phase 3, 5, and 6 routes land. Their models are taken from `HUGO_PLAN.md` sections 7.4, 7.5, and 14.2 and the connector contract, and they are permissive: fields the server adds later are kept on the returned objects. The identity, secrets, request history, audit, settings, and events clients are verified against the current server.

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
  await hugo.changePassword("password", "a-new-long-password");
}

// Store credential material in the vault (values are write-only).
const secret = await hugo.secrets.create({ name: "web-01-key", kind: "Password", values: { password: "s3cret" } });

// Preview: create a resource, bind it to a profile, and look at what an MCP caller would receive.
const [connector] = await hugo.connectors.list();
const resource = await hugo.resources.create({
  name: "web-01",
  connectorType: connector.type,
  settings: { host: "web-01.example.com", port: 22 },
  secretBindings: [{ slotName: "password", secretId: secret.id }],
});
const profile = await hugo.agentProfiles.create({ name: "ops" });
await hugo.agentProfiles.bindings.create(profile.id, { resourceId: resource.id, alias: "web-01", accessMode: "ReadOnly" });
const instructions = await hugo.agentProfiles.compiledInstructions(profile.id, "Mcp");

// Preview: chat with streaming.
const conversation = await hugo.conversations.create({ agentProfileId: profile.id });
for await (const event of hugo.conversations.streamMessage(conversation.id, "Is nginx healthy on web-01?")) {
  if (event.type === "assistant.delta") process.stdout.write(event.payload?.text ?? "");
  if (event.type === "approval.required") console.log("\napproval needed:", event.payload);
  if (event.type === "turn.completed") break;
}
```

`examples/quickstart.mjs` runs the same flow against a live server (`npm run build` first).

## Authentication

| Method | What it sends | Use it for |
|---|---|---|
| `login(email, password, tenantId?)` | `POST /v1.0/token` with `x-email`, `x-password`, `x-tenant-id`; stores the bearer token and tenant | Users. The token refreshes automatically near expiry (opt out with `{ autoRefresh: false }`). |
| `loginWithAccessKey(accessKey, secretKey)` | `POST /v1.0/token` with `x-access-key`, `x-secret-key`; stores the token | Credentials in `DirectHeader`, `SessionExchange`, or `Hybrid` mode. |
| `useToken(token, tenantId?)` | `Authorization: Bearer ...` | A token you already have. |
| `useAccessKey(accessKey, secretKey, tenantId?)` | `x-access-key`, `x-secret-key` on every request | Credentials in `DirectHeader` or `Hybrid` mode. |
| `useSignedRequests(accessKey, secretKey, tenantId)` | `x-access-key`, `x-date`, `x-nonce`, `x-signature`, `x-tenant-id` | Credentials in `SignedRequest` or `Hybrid` mode. The secret never leaves the process. |
| `useAdminApiKey(apiKey, tenantId?)` | `x-api-key` and `x-tenant-id` | System administrators, when the server sets `HUGO_ADMIN_API_KEY`. |

Session helpers: `refresh()`, `logout()`, `validateToken()`, `tokenDetails()`, `reauthenticate(password)` (required shortly before `secrets.reveal`), `changePassword(current, next)`, and `lookupTenants(email)`.

Signed requests match the server's `RequestSigner`: the canonical string is `METHOD\nPATH\nQUERY\nTENANT\nDATE\nNONCE\nhex(SHA-256(body))`, where `PATH` is the raw path, `QUERY` is the raw query string exactly as sent, `TENANT` is the credential's tenant ID, and `DATE` is `yyyy-MM-ddTHH:mm:ssZ` UTC. The signature is lowercase hex HMAC-SHA256 with the secret key. The SDK uses WebCrypto (`globalThis.crypto.subtle`), falling back to `node:crypto`'s `webcrypto` on Node 18. `canonicalize`, `sign`, and `signRequest` are exported if you need to sign requests yourself.

## Clients

Identity and administration routes are tenant scoped (`/v1.0/api/tenants/{tenantId}/...`) and use `client.tenantId`, which `login` sets. A system administrator can point the client at another tenant by setting `client.tenantId`.

| Property | Routes |
|---|---|
| `tenants` | list, get, create, update, delete |
| `users` | CRUD, `me()`, `updateMe()`, `permissions(id)`, `resetPassword(id, newPassword, mustChangePassword?)` |
| `credentials` | CRUD, `createWithSecret()` (returns the secret once), `rotate(id)`, `permissions(id)` |
| `roles`, `permissions` | CRUD, `roles.clone(id, body)` |
| `rolePermissions` | `list({ roleId })`, `create(roleId, permissionId)`, `delete(id)` |
| `userRoleAssignments`, `credentialScopeAssignments` | CRUD (filter lists with `userId` or `credentialId`) |
| `sessions` | `list`, `get`, `revoke` |
| `audit` | `list(filter)`, `get`, `delete`, `deleteMany(filter)` |
| `secrets` | CRUD, `setValues`, `referrers`, `parse`, `reveal(id, reason)` |
| `promptTemplates` | CRUD, `versions`, `version`, `restore`, `clone`, `reset`, `render`, `renderAdHoc` |
| `requestHistory` | `list(filter)`, `summary`, `get`, `delete`, `deleteMany` |
| `events` | `stream({ signal })`: async iterator of tenant events |
| `settings`, `system` | settings read and update; health, info, master key status and rotation, restart |
| `connectors`, `resources` (+ `resources.knowledge`) | Preview |
| `agentProfiles` (+ `agentProfiles.bindings`) | Preview: CRUD, duplicate, `compiledInstructions`, `tools`, `card`, bindings and tool rules |
| `modelEndpoints`, `toolInvocations`, `approvals`, `lockdown`, `conversations` | Preview: CRUD, validate, dry run, invoke, approve, reject, cancel, `awaitDecision`, chat streaming |

List routes take `{ maxResults, skip, ordering, search, activeOnly, includeBuiltIn }` plus route filters and return `{ objects, totalRecords, endOfResults, ... }`. Every collection also has `all(query)`, an async iterator over every page.

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

`status` is the HTTP status, `error` the HTTP-level name (`NotFound`), `code` the stable code (`RouteNotFound`, `PasswordChangeRequired`, `ReauthenticationRequired`, `ValidationFailed`, ...), and `requestId` comes from the body or the `x-request-id` header. `isRouteNotFound` tells a missing route apart from a missing record.

## Events and streaming

```ts
const controller = new AbortController();
for await (const event of hugo.events.stream({ signal: controller.signal })) {
  console.log(event.event, event.json());
  if (event.event === "secret.created") controller.abort();
}
```

The events stream and chat streaming share one SSE parser (`SseParser`, `parseSseStream`), also exported. Chat events are `turn.started`, `assistant.delta`, `assistant.thinking`, `tool.proposed`, `tool.started`, `tool.completed`, `approval.required`, `approval.resolved`, `turn.completed`, and `error`.

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
| `HUGO_SDK_ADMIN_PASSWORD` | `password` | Its password. On a fresh server the harness clears the first-login must-change flag by changing the password to a temporary value and back. |
| `HUGO_SDK_ADMIN_TENANT` | `default` | The administrator's tenant |
| `HUGO_SDK_ADMIN_API_KEY` | (unset) | Enables the administrator API key test (also read from `HUGO_ADMIN_API_KEY`) |
| `HUGO_SDK_CHAT` | (unset) | `1` runs a streaming chat turn (needs a model endpoint) |

Domain tests probe their routes first and skip with "route not available on this server yet (Phase 3/5/6 preview)" when the server answers 404. Loopback addresses use `127.0.0.1`, never `localhost`.
