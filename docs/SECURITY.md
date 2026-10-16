# Security

Hugo holds the keys to production systems: SSH private keys, database passwords, cloud access keys, kubeconfigs. Everything in its security model follows from that. Tenants are hard walls, every request is authenticated and authorized against a static permission map, secrets are encrypted at rest and never handed to a model, and the few ways to see a plaintext secret are narrow, rate limited, and audited.

What follows is how the code in this repository behaves, not how the plan imagined it. The safety rules that decide whether a specific tool call may run (effects, access modes, approvals) are covered separately in [SAFETY_MODEL.md](SAFETY_MODEL.md). Settings mentioned here are listed with their defaults and ranges in [CONFIGURATION.md](CONFIGURATION.md).

## Tenancy

A tenant is the isolation boundary. Users, credentials, roles, assignments, secrets, resources, agent profiles, conversations, approvals, audit records, and request history all belong to exactly one tenant. Built-in roles, the Global prompt template layer, and system tables are the only shared things. The default tenant's ID is the fixed string `default`.

The tenant of a request comes from the authenticated principal, never from something the caller chooses. Flat routes such as `/v1.0/api/resources` take the tenant from the request context. Tenant-scoped routes such as `/v1.0/api/tenants/{tenantId}/users` take it from the path, and the authorization layer refuses a path tenant that differs from the caller's own tenant. System administrators are the exception: they may act on another tenant through the path or a `tenantId` query parameter, and that bypass is audited. A `tenantId` query parameter from anyone else is ignored.

Cross-tenant access returns 404, not 403. If you ask for something in another tenant, Hugo behaves as if it does not exist, so the response does not confirm that an ID is real. A 403 means the target exists in your tenant and you lack the permission; its body names the permission you needed:

```json
{
  "error": "Forbidden",
  "code": "AuthorizationDenied",
  "message": "No grant covers Update on Resource.",
  "requestId": "4f0c2e...",
  "requiredPermission": "Update",
  "resourceType": "Resource",
  "tenantId": "default"
}
```

The list of permissions that were evaluated is never included.

## Authentication schemes

Every route except health, the token and tenant lookup routes, the agent card, `/metrics`, and the OpenAPI document requires authentication. The MCP endpoints are registered without route-level authentication because the MCP proxy authenticates each request itself with the same schemes, answering 401 with a bearer challenge. Hugo accepts one scheme per request. Presenting two (a bearer token and an access key, say) is rejected outright rather than guessed at.

| Scheme | Headers | Where it works |
|---|---|---|
| Bearer session | `Authorization: Bearer <token>`, or `x-token: <token>` | Every authenticated route, MCP, A2A, OpenAI-compatible. If both headers are sent they must carry the same token. |
| User login | `x-email`, `x-password`, `x-tenant-id` (or a JSON body with `email`, `password`, `tenantId`) | `POST /v1.0/token` only. Exchanges a password for a session. |
| Access key and secret | `x-access-key`, `x-secret-key` | Every authenticated route, and `POST /v1.0/token` to exchange the pair for a session. |
| Signed request | `x-access-key`, `x-date`, `x-nonce`, `x-signature` | Every authenticated route. The secret never crosses the wire. |
| Administrator API key | `x-api-key` | Every authenticated route, as a system administrator. Disabled unless `Auth.AdminApiKey` (or `HUGO_ADMIN_API_KEY`) is set. |

`x-tenant-id` is accepted everywhere (`x-tenant-guid` is the AUTHENTICATION.md name for the same header). With a session or a credential it must match the principal's tenant or the request fails; it is mainly useful at login, where an email can exist in several tenants. `POST /v1.0/tenants/lookup` with `{ "email": "..." }` returns the active tenants an email can sign in to, which is how the dashboard's login form finds the tenant.

Both seeded accounts, and any user whose password an administrator resets, carry `mustChangePassword`. Since decision D-048 the flag is advisory: the dashboard prompts for a new password and the prompt can be skipped, and no route is blocked. That makes changing the seeded `password` your job, not Hugo's; do it before the server is reachable from anywhere but your own machine.

### Sessions

A session token is opaque. New tokens use AES-256-GCM with a random 12-byte nonce and a 16-byte authentication tag. HKDF-SHA256 derives the `hugo-sessions-v1` subkey from the versioned master key; the issuer is authenticated associated data. A `hugo1.` prefix identifies the versioned encrypted frame. Tokens carry a session ID, and every use is checked against the session row, so revoking a session (`DELETE /v1.0/token`, `DELETE /v1.0/api/sessions/{id}`) takes effect at once. New sessions use the current master-key version. Keep the previous master key configured until its outstanding sessions expire; removing it invalidates those tokens. The rotation endpoint re-encrypts stored secrets and credentials with the already configured current key; it does not replace the process's key configuration.

Pre-upgrade AES-256-CBC/HMAC-SHA256 tokens remain readable using `Auth.SigningKey` until their normal expiry or revocation. That setting only validates legacy tokens; changing it invalidates legacy tokens while leaving master-key sessions valid. Keep the existing signing key during an upgrade to preserve those sessions.

User sessions last `Auth.UserSessionMinutes` (60), system administrator sessions `Auth.AdminSessionMinutes` (30). Credential sessions last the user lifetime, capped by the credential's own expiry. `POST /v1.0/token/refresh` issues a fresh token for the same principal until `Auth.SessionAbsoluteMaxHours` (12) after the original sign-in; after that the principal has to authenticate again.

### Credentials

A credential is a non-interactive principal owned by a user: an access key (`access_...`) plus a secret key (`secret_...`) that is shown once at creation (`POST /v1.0/api/tenants/{tenantId}/credentials`) and once at each rotation (`.../credentials/{id}/rotate`). Hugo stores the secret encrypted, not hashed, because signed requests need the plaintext to compute the HMAC.

A credential can be bound to an agent profile (`agentProfileId`), which makes it the natural thing to give a harness such as Claude Code: the harness talks to that profile without naming it. With `lockToProfile: true` the credential can act only as its bound profile, on every surface.

The `authMode` field controls which forms a credential accepts:

| Mode | Direct `x-secret-key` | Signed requests | Exchange at `POST /v1.0/token` |
|---|---|---|---|
| `Hybrid` (default) | yes | yes | yes |
| `DirectHeader` | yes | no | yes |
| `SignedRequest` | no | yes | no |
| `SessionExchange` | yes | yes | yes |

`SessionExchange` currently behaves exactly like `Hybrid`; the code does not restrict it to the token exchange.

### Signed requests

A signed request proves possession of the secret without sending it. Build the canonical string (`src/Hugo.Core/Security/RequestSigner.cs`) by joining these seven values with newlines:

```
METHOD            uppercase, for example POST
PATH              path without the query, for example /v1.0/api/resources
QUERY             raw query string without the leading ?, or empty
TENANT            the credential's tenant ID
DATE              the x-date value, ISO 8601 UTC
NONCE             the x-nonce value
BODY_SHA256       lowercase hex SHA-256 of the body bytes (of the empty string when there is no body)
```

The signature is the lowercase hex HMAC-SHA256 of that string, keyed with the secret key, sent in `x-signature`. Hugo rejects a request whose `x-date` is more than `Auth.SignedRequestSkewSeconds` (300) from its own clock, and remembers each nonce for twice the skew, so a captured request cannot be replayed. A request may not carry both `x-signature` and `x-secret-key`.

### Rate limits

`POST /v1.0/token`, `POST /v1.0/tenants/lookup`, and `POST /v1.0/token/reauthenticate` allow `Auth.RateLimitPerMinute` (10) attempts per source IP and, separately, per identity (email or access key) per minute. Every attempt counts, successful or not. Over the limit the answer is 429 `RateLimited` with `Retry-After: 60`. Secret reveals have their own limit, described below.

### Passwords

Passwords are hashed with PBKDF2-HMAC-SHA256 and a per-user salt, `Auth.PasswordHashIterations` (600,000) iterations. This is a deliberate departure from the unsalted SHA-256 in AUTHENTICATION.md (D-009). When the iteration count is raised, older hashes are upgraded the next time the user signs in.

## Authorization

Every REST route, MCP tool, and A2A method is mapped to exactly one resource type and operation in the operation scope map, and a startup self-check logs any route that slipped through without one. Unmapped authenticated routes fail closed: only system administrators reach them. [REST_API.md](../REST_API.md), [MCP_API.md](../MCP_API.md), and [A2A_API.md](../A2A_API.md) list the permission each endpoint needs.

Resource types: `All`, `Tenant`, `Admin`, `User`, `Credential`, `Session`, `Role`, `Permission`, `Assignment`, `Audit`, `Settings`, `Secret`, `Resource`, `Connector`, `AgentProfile`, `Binding`, `PromptTemplate`, `ModelEndpoint`, `Conversation`, `ToolInvocation`, `Approval`, `RequestHistory`, `Knowledge`, `Plugin`, `Analytics`.

Operations: `All`, `Create`, `Read`, `Write` (Create, Update, and Delete), `Update`, `Delete`, `Execute`, `Admin`, `Reveal`.

`Reveal` is special. Neither `All` nor `Write` implies it; only a grant that names `Reveal` explicitly covers it. `Settings`, `Plugin`, and `Admin`, plus creating and deleting tenants, are platform operations that only system administrators can perform, whatever roles a tenant user holds.

### Roles and assignments

Roles bundle permissions; users receive roles through user role assignments, and credentials through credential scope assignments. An assignment has a scope: `Tenant` (everything in the tenant) or `Resource` (one object, such as a single agent profile or resource, with `inheritsToChildren` controlling whether it covers the object's children). Permissions are `Permit` or `Deny`, and a matching Deny always wins.

Hugo seeds these built-in roles. They are protected, shared by every tenant, and refreshed at startup with stable IDs (`rol_builtin_<name>`).

| Role | What it grants |
|---|---|
| `TenantAdmin` | All operations on everything in the tenant, plus `Reveal` on `Secret`. |
| `SecurityAdmin` | All operations on User, Credential, Session, Role, Permission, Assignment, Audit, and Tenant; Read on Settings and RequestHistory. |
| `Auditor` | Read on the security surfaces, Audit, RequestHistory, ToolInvocation, and Approval. |
| `Operator` | Read on Resource, Connector, AgentProfile, ToolInvocation, RequestHistory, Analytics, and Approval; Execute on AgentProfile; Create, Read, and Update on Conversation; Update on Approval. |
| `Builder` | Everything Operator has, plus Create, Read, Update, and Delete on Resource, Secret (no Reveal), AgentProfile, Binding, PromptTemplate, ModelEndpoint, and Knowledge. |
| `ResourceAdmin` | All operations on the one object it is assigned to (Resource scope). No Reveal. |
| `Editor` | Read and Write on the assigned object. |
| `Viewer` | Read on the assigned object. |
| `ProfileUser` | Read and Execute on the assigned agent profile, and Create, Read, and Update on conversations. The role for "can chat with this one profile and nothing else". |
| `TenantMember` | Read on the tenant and on agent profiles. |
| `Custom` | A marker for tenant-defined roles; no permissions. |

Custom roles are built from tenant permissions (`/v1.0/api/tenants/{tenantId}/permissions`) mapped onto roles (`.../role-permissions`), or by cloning a built-in role (`.../roles/{id}/clone`) and editing the copy. `GET /v1.0/tenants/{tenantId}/users/{id}/permissions` and the matching credential route show the effective grants of a principal, which is the fastest way to answer "why was this denied".

### Bypass rules and the owner ceiling

The evaluation order is:

1. A system administrator (the `x-api-key` principal, or a user with `isAdmin`) is permitted everything, in any tenant.
2. A target tenant other than the caller's own returns 404.
3. Platform operations are denied to everyone else.
4. A user with `isTenantAdmin` is permitted everything in their tenant.
5. Otherwise the principal's role grants are evaluated, deny first.

Credentials add one more rule: a credential is limited by its own scope assignments and also by its owner's grants (the owner ceiling). A credential can never do more than the user who owns it.

Bypasses are audited as `AuthorizationBypass` for every operation except Read, and every denial is persisted as `AuthorizationDenied`.

One behavior deserves attention before you hand out credentials, and it is an open review item (P16.3): a credential inherits its owner's bypass flags. In `AuthenticationService.BuildCredentialContextAsync`, the credential's request context copies the owner's `isAdmin` and `isTenantAdmin`. For a tenant administrator's credential, any credential scope assignments still narrow it (the authorization service checks them before applying the tenant-admin bypass), but a credential with no scope assignments acts with the full tenant-admin bypass. For a system administrator's credential, the bypass is applied before anything else, so scope assignments do not narrow it at all. Until that changes, create harness credentials from an account that holds only the roles the harness needs, or give a tenant administrator's credential an explicit narrow scope assignment.

## Secrets vault

Secrets are encrypted credential material: passwords, private keys, API tokens, access key pairs, service account JSON, kubeconfigs, connection strings, certificates. Resources and model endpoints refer to them by slot, so the same AWS key pair can serve two resources, and a secret cannot be deleted while something references it (409).

### Encryption

Each field value is encrypted with AES-256-GCM using a 12-byte random nonce and a 16-byte tag. The data key is derived from the master key with HKDF-SHA256, using a purpose string as the info: `hugo-secrets-v1` for secret values and `hugo-credentials-v1` for credential secret keys. The associated data is `tenantId|secretId|fieldName`, which binds each ciphertext to its row: a value copied into another row or tenant fails to decrypt instead of quietly working.

The stored frame, base64 encoded, is:

```
[1 byte format version][1 byte key version][12 byte nonce][16 byte tag][ciphertext]
```

The key version byte lets Hugo hold a current and a previous master key at the same time during rotation.

### The master key

The master key must be base64 and decode to at least 32 bytes (`openssl rand -base64 32`). Hugo looks for it in this order:

1. `HUGO_MASTER_KEY`. This is the production path, and the Docker stack's `start.sh` generates it into `docker/.env`.
2. `Secrets.MasterKey` in the settings file, for development only. Startup logs a warning.
3. The key file at `Secrets.MasterKeyFile` (`data/master.key`), created with a random key if it does not exist. Also development only, also warned about, and anyone who can read the file can decrypt every secret.

With none of these, startup stops. At every start Hugo also decrypts a canary row in `master_key_versions`. A key that cannot decrypt the canary for its version refuses to start, which protects you from booting with the wrong key and writing new secrets that the old ones can never be reconciled with.

Back the key up somewhere other than the server. If it is lost, every stored secret and credential secret is unrecoverable; you would have to re-enter every secret and rotate every credential.

The Compose file contains a fixed development key that it uses only when `docker/.env` has none. It is public. Do not run real secrets on it.

### Rotating the master key

Rotation re-encrypts every row under a new key version without downtime beyond one restart:

1. Generate a new key.
2. Set `HUGO_MASTER_KEY` to the new key, `HUGO_MASTER_KEY_PREVIOUS` to the old one, and `HUGO_MASTER_KEY_VERSION` to the next version (2 for the first rotation). `HUGO_MASTER_KEY_PREVIOUS_VERSION` defaults to the current version minus one.
3. Restart. Old rows stay readable through the previous key; new writes use the new one.
4. As a system administrator, call `POST /v1.0/api/system/master-key/rotate`. The job re-encrypts secret values and credential secrets in batches of `Secrets.RotationBatchSize` (200), one transaction per batch, and is resumable: it touches only rows whose key version differs from the current one. `GET /v1.0/api/system/master-key` reports progress (`total`, `completed`, `failed`, `remaining`).
5. When `remaining` is 0, remove `HUGO_MASTER_KEY_PREVIOUS` and restart.

The rotation is audited as `MasterKeyRotated`.

### Reading and revealing secrets

The API returns secret metadata (name, kind, field names, fingerprint, referrers) and never values. Updating values (`PUT .../secrets/{id}/values`) replaces only the fields you send; the mask placeholder is never stored.

Revealing plaintext is a deliberate, noisy act (D-011). `POST /v1.0/api/secrets/{id}/reveal` requires all of the following:

- the `Reveal` permission on `Secret`, which only `TenantAdmin` (and system administrators) hold by default;
- a user session (credentials and API keys cannot reveal) that re-entered its password through `POST /v1.0/token/reauthenticate` within the last `Secrets.RevealReauthMinutes` (5), otherwise 403 `ReauthenticationRequired`;
- a `reason` between 1 and 1024 characters;
- staying under `Secrets.RevealRateLimitPerMinute` (10) reveals per source IP and per principal, otherwise 429.

Every reveal is audited as `SecretRevealed` with the reason and the field names, and the response body is never captured in request history.

### The scrubber

Secrets reach connectors out of band for one call at a time; they are never placed in prompts. Whatever comes back still passes through the secret scrubber before it reaches a model, a log, request history, an audit description, or a tool result. The scrubber first recognizes complete quoted password/secret assignments, then private key blocks, AWS keys, bearer tokens, credentials embedded in connection URLs, and unquoted assignments. Later assignment rules can span earlier redactions, so an inner bearer token or key block cannot leave an outer password value partly exposed. It protects the resulting markers and recognized structural key names, then removes remaining plaintext values resolved for the current call (including base64 and URL-encoded forms). This order prevents a password literally equal to `password` from hiding a sensitive field name before its value can be redacted. Literal secrets elsewhere in a message are still removed. Each redaction becomes `[redacted:<kind>]` and is counted in the `hugo_secret_redactions_total` metric. A pattern that takes too long yields `[redacted:timeout]` rather than leaking the text.

All server severities, protocol callbacks, PolyPrompt client logs, exception chains, and structured properties pass through `HugoLog` before any configured sink. Sensitive property names such as `password`, `apiKey`, and `accessToken` are redacted even when their values were never registered. Quoted assignments with spaces are removed as a whole. SyslogLogging's event fires after its sinks, so library clients receive a separate private ingress with console, file, syslog, metrics, and tracing disabled. Hugo forwards a sanitized copy to the real sink and then to Radiant. An embedding host's retained logger settings affect only the sanitized output; direct writes by that host to its own raw logger remain outside Hugo's boundary. Library callbacks configured by Hugo are covered; arbitrary plugin or library writes directly to stdout or unrelated loggers are outside this boundary.

The process-wide scrubber registers configured keys, successfully verified passwords, encrypted/decrypted vault fields and credential keys, and model API keys. Session payloads and arbitrary failed-login inputs are excluded. Its storage is bounded by both `Logging.MaximumKnownSecretValues` and `Logging.MaximumKnownSecretCharacters`. At either limit the shared scrubber redacts every nonempty output as `[redacted:capacity]` until restart, rather than evicting secrets that might still be in use. Per-invocation connector scrubbers continue to cover the complete resolved secret set. Values shorter than the configured scrubber minimum (four characters by default) rely on sensitive-key/context redaction rather than unrestricted exact matching.

## Audit

Security-relevant events are written to the tenant's audit log (`GET /v1.0/api/tenants/{tenantId}/audit`), kept for `Audit.RetentionDays` (365). The event types are:

`AuthSuccess`, `AuthFailure`, `SessionIssued`, `SessionRevoked`, `SessionReauthenticated`, `CredentialCreated`, `CredentialRotated`, `CredentialUsed`, `AuthorizationDenied`, `AuthorizationBypass`, `RoleChanged`, `PermissionChanged`, `AssignmentChanged`, `ConfigurationChanged`, `PasswordChanged`, `SecretCreated`, `SecretUpdated`, `SecretRevealed`, `SecretDeleted`, `MasterKeyRotated`, `PromptTemplateChanged`, `LockdownChanged`, `SettingsChanged`, `RestartRequested`, `ServerRestarted`, `ResourceChanged`, `ProfileChanged`, `BindingChanged`, `ApprovalDecided`, `KnowledgeChanged`, `HostKeyPinned`, `HostKeyMismatch`, `ModelEndpointChanged`, `PluginLoaded`, `SettingLearned`.

Tool calls are not duplicated into audit. They have their own record (`GET /v1.0/api/tool-invocations`) with the principal, profile, binding, redacted arguments, effect, decision, and outcome, which is usually the more useful trail when you want to know what an agent did.

## Deployment hygiene

A few things are worth checking on any deployment that holds real credentials:

- `HUGO_MASTER_KEY` and `HUGO_AUTH_SIGNING_KEY` are set to generated values, and the master key is backed up.
- Both seeded accounts (`admin@hugo.local`, `anderson@hugo.local`) have new passwords. Hugo prompts for this at first sign-in but does not force it.
- `HUGO_ADMIN_API_KEY` is unset unless you need it.
- The REST port is not reachable from untrusted networks, or `Telemetry.ServeMetricsOnServicePort` is off, since `/metrics` is unauthenticated.
- `Rest.CorsOrigins` names the dashboard origin instead of allowing any origin.
- Upstream accounts are least privilege: a read-only database login, a scoped IAM policy, a namespace-scoped Kubernetes service account. [SAFETY_MODEL.md](SAFETY_MODEL.md) explains why Hugo's own classifiers are a second line of defense, not the first.
