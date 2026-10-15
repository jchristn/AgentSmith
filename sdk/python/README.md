# Hugo Python SDK

`hugo-sdk` is the Python client for the Hugo REST API. It ships a blocking `HugoClient` and an `AsyncHugoClient` with the same surface, both built on `httpx`. Responses are typed dataclasses that ignore unknown fields, errors are raised as `HugoError`, and server-sent events (the tenant event stream and chat streaming) come back as iterators.

Version `0.1.0-alpha`, mirrored from `src/Directory.Build.props`. Python 3.9 or later.

> **Preview:** only conversations and chat streaming are preview; they are verified against the server when the Phase 7 routes land. Every other client (identity, secrets, request history, events, connectors, resources and knowledge, agent profiles and bindings, prompt layers, model endpoints, tool invocations, approvals, lockdown) is verified against the live server by the harness.

## Install

```bash
cd sdk/python
python3 -m venv .venv
.venv/bin/pip install -e ".[test]"
```

## Quick start

```python
from hugo_sdk import HugoClient, HugoError

with HugoClient("http://127.0.0.1:8800") as client:
    issued = client.login("anderson@hugo.local", "password")
    if issued.must_change_password:
        client.change_password("password", "a-new-strong-password")

    secret = client.secrets.create({"name": "web-01-key", "kind": "Password", "values": {"password": "s3cret"}})

    resource = client.resources.create({
        "name": "web-01", "connectorType": "ssh", "environment": "prod",
        "settings": {"host": "web-01.example.com", "port": 22, "username": "deploy", "authMethod": "Password"},
        "secretBindings": [{"slotName": "password", "secretId": secret.id}],
    })
    profile = client.agent_profiles.create({"name": "ops", "displayName": "Ops"})
    client.agent_profiles.create_binding(profile.id, {"resourceId": resource.id, "alias": "web-01", "accessMode": "ReadWriteWithApproval"})
    print(client.agent_profiles.compiled_instructions(profile.id, surface="Mcp").text)
    outcome = client.tool_invocations.dry_run("ssh_exec", {"command": "uptime"}, profile_id=profile.id, resource="web-01")
    print(outcome.decision, outcome.effect)
```

The same flow, with cleanup, is in `examples/quickstart.py`. MCP clients connect to `/mcp` and A2A callers to `/v1.0/a2a/{tenantId}/{profileName}`; `client.info()` returns both URLs.

Always use `127.0.0.1` rather than `localhost` for a local server. On Windows `localhost` resolves to `::1` first and stalls before falling back.

## Authentication

| Method | What it sends |
|---|---|
| `client.login(email, password, tenant_id=None)` | `POST /v1.0/token` with `x-email`, `x-password`, `x-tenant-id`; stores the bearer token and tenant |
| `client.login_with_access_key(access_key, secret_key)` | Exchanges credential keys for a session token |
| `client.use_token(token, tenant_id=None)` | `Authorization: Bearer` with a token you already have |
| `client.use_access_key(access_key, secret_key)` | `x-access-key` and `x-secret-key` on every request (DirectHeader or Hybrid credentials) |
| `client.use_signed_requests(access_key, secret_key, tenant_id)` | HMAC-SHA256 signed requests (SignedRequest or Hybrid credentials) |
| `client.use_admin_api_key(api_key, tenant_id=None)` | System administrator `x-api-key` (when the server sets `HUGO_ADMIN_API_KEY`) |

Signed requests send `x-access-key`, `x-date` (`yyyy-MM-ddTHH:mm:ssZ`), `x-nonce`, `x-tenant-id`, and `x-signature`, the lowercase hex HMAC-SHA256 of `METHOD\nPATH\nQUERY\nTENANT\nDATE\nNONCE\nhex(SHA256(body))` with the secret key. `hugo_sdk.canonicalize` and `hugo_sdk.sign` expose the algorithm.

Session helpers: `refresh()`, `logout()`, `validate_token()`, `token_details()`, `reauthenticate(password)`, `change_password(current, new)`, and `lookup_tenants(email)`. After `login`, the client refreshes the token automatically when it is within `refresh_skew_seconds` (default 60) of expiry; pass `auto_refresh=False` to turn that off.

`must_change_password` tells you the account still has its seeded or reset password. It is advisory: every route works, and the dashboard shows a skippable prompt.

## Clients

Tenant-scoped routes (`/v1.0/api/tenants/{tenantId}/...`) use `client.tenant_id`, which `login` sets. A system administrator can point it at any tenant. The flat domain routes (`/v1.0/api/resources`, `/v1.0/api/agent-profiles`, and so on) take the tenant from the token; a system administrator can set `client.tenant_override = "ten_..."` (or pass `tenant_override=` to the constructor) to add `?tenantId=` to every `/v1.0/api/...` request outside `/v1.0/api/tenants`. The parameter is part of the signed query string.

The tenant-scoped `prompt_templates` client and the layered `prompt_layers` client both exist because the server exposes both route families.

| Property | Routes |
|---|---|
| `system` | health, live, info, server info, observability status, master key status and rotation, restart |
| `tenants` | list, get, create, update, delete |
| `users` | CRUD, `me`, `update_me`, `change_password`, `permissions`, `reset_password` |
| `credentials` | CRUD (create returns the secret once), `rotate`, `permissions` |
| `roles`, `permissions`, `role_permissions` | CRUD, `roles.clone`, mappings by `role_id` |
| `user_role_assignments`, `credential_scope_assignments` | CRUD with `user_id` and `credential_id` filters |
| `sessions` | list, get, `revoke` |
| `audit` | list (event type, principal, result, time range, paging), get, delete, `delete_many` |
| `request_history` | list (method, status, path, time range, paging), `summary`, get, delete, `delete_many` |
| `secrets` | CRUD, `set_values`, `referrers`, `parse`, `reveal` |
| `prompt_templates` | CRUD, `versions`, `version`, `restore`, `clone`, `reset`, `render`, `render_ad_hoc` |
| `events` | `stream()` over `/v1.0/api/events` |
| `settings` | get, update, `test_database` (system administrators) |
| `connectors` | list, get (settings schema, secret slots, tools) |
| `resources` | CRUD (`list(connector_type=...)`), `test`, `test_unsaved`, `duplicate`, `referrers`, `invoke`, knowledge (`knowledge`, `knowledge_scope`, `get_knowledge`, `put_knowledge`, `delete_knowledge`, `knowledge_versions`) |
| `agent_profiles` | CRUD, `duplicate`, `card`, `compiled_instructions(id, surface)`, `compiled_instructions_text(id, surface)`, `tools(id, surface)`, bindings (`list_bindings`, `get_binding`, `create_binding`, `update_binding`, `delete_binding`), `get_tool_rules`, `set_tool_rules` |
| `prompt_layers` | Layered templates by key at `/v1.0/api/prompt-templates`: `list`, `get`, `save(key, content, change_note, layer)`, `reset`, `versions`, `builtin`, `diff`, `render` |
| `model_endpoints` | CRUD, `validate`, `models` |
| `tool_invocations` | list, get, `summary`, `dry_run`, `invoke` |
| `approvals` | list, get, `approve`, `reject`, `cancel`, `await_decision` |
| `lockdown` | `get`, `enable(reason)`, `disable` |
| `conversations` | CRUD, `messages`, `send_message`, `stream_message`, `cancel`, `export` (preview, Phase 7) |

Enumeration routes take `max_results`, `skip`, `ordering` (`CreatedDescending`, `CreatedAscending`, `NameAscending`, `NameDescending`), `search`, `active_only`, and `include_built_in`, and return an `EnumerationResult` with `objects`, `total_records`, and `end_of_results`. Audit and request history return a `PagedResult` with `page_number`, `page_size`, `total_count`, and `total_pages`.

Bodies are dictionaries with the server's camelCase field names (or model instances). Every model keeps the original JSON in `raw`, and `model["someField"]` reads it.

## Examples

### Errors

```python
from hugo_sdk import HugoError

try:
    client.secrets.get("sec_missing")
except HugoError as e:
    print(e.status, e.error, e.code, e.message, e.request_id, e.fields)
```

`e.is_route_not_found` is true when the server has no such route (404 `RouteNotFound`), which is how the harness detects preview routes the server does not have yet.

### Revealing a secret

```python
client.reauthenticate("my-password")                # within Secrets.RevealReauthMinutes (default 5)
values = client.secrets.reveal(secret.id, "rotating the deploy key").values
```

Without a recent re-authentication the server answers 403 `ReauthenticationRequired`. Reveals are audited and rate limited.

### Event stream

```python
for event in client.events.stream():
    print(event.event, event.json())    # stream.opened, heartbeat, secret.created, ...
```

### Async client

```python
import asyncio
from hugo_sdk import AsyncHugoClient

async def main():
    async with AsyncHugoClient("http://127.0.0.1:8800") as client:
        await client.login("anderson@hugo.local", "a-new-strong-password")
        page = await client.secrets.list(max_results=10)
        async for event in client.events.stream():
            print(event.event)
            break

asyncio.run(main())
```

### Tool calls and approvals

```python
check = client.tool_invocations.dry_run("http_request", {"method": "DELETE", "path": "/x"}, profile_id=profile.id, resource="api")
print(check.decision, check.effect, check.reason)     # Denied Destructive ... on a ReadOnly binding

outcome = client.tool_invocations.invoke("ssh_exec", {"command": "sudo systemctl restart nginx"}, profile_id=profile.id, resource="web-01")
if outcome.approval_id:                                # HTTP 202 while approval is pending
    decided = client.approvals.await_decision(outcome.approval_id, timeout=600)
```

`resource` is the binding alias on the profile. `resources.invoke(resource_id, tool_name, arguments)` calls a tool on a resource directly, without a profile.

### Prompt layers

```python
view = client.prompt_layers.save("persona.agent-smith", "You are {{profile.personaName}}.", change_note="shorter")
print(view.active_layer)                               # Tenant
for line in client.prompt_layers.diff("persona.agent-smith"):
    print(line.op, line.text)
client.prompt_layers.reset("persona.agent-smith")      # back to the Global layer
```

### Chat streaming (preview, Phase 7)

```python
conversation = client.conversations.create({"agentProfileId": profile.id})
for event in client.conversations.stream_message(conversation.id, "Is nginx healthy on web-01?"):
    if event.event == "assistant.delta":
        print(event.json(), end="")
    elif event.event == "approval.required":
        client.approvals.approve(event.json()["approvalId"], note="ok")
```

Chat events: `turn.started`, `assistant.delta`, `assistant.thinking`, `tool.proposed`, `tool.started`, `tool.completed`, `approval.required`, `approval.resolved`, `turn.completed`, `error`.

## Test harness

The harness in `tests/` (pytest) runs against a live server. It signs in as the system administrator, creates a fresh tenant and a tenant administrator, runs every test as that user (and the administrator where needed), and deletes the tenant at the end. The conversation and chat streaming tests skip with "route not available on this server yet (Phase 7 preview)" while the server answers 404; every other test must pass.

```bash
.venv/bin/python run_harness.py                              # http://127.0.0.1:8800
.venv/bin/python run_harness.py http://127.0.0.1:8801        # another server
HUGO_SDK_SERVER=http://127.0.0.1:8800 .venv/bin/python -m pytest -rs
```

| Variable | Default |
|---|---|
| `HUGO_SDK_SERVER` | `http://127.0.0.1:8800` |
| `HUGO_SDK_ADMIN_EMAIL` | `admin@hugo.local` |
| `HUGO_SDK_ADMIN_PASSWORD` | `password` |
| `HUGO_SDK_ADMIN_TENANT` | `default` |

On a fresh server the seeded administrator is flagged to change its password (advisory only). The harness changes it to a temporary password and back to `HUGO_SDK_ADMIN_PASSWORD` on the same session, which clears the flag and keeps reruns working.

`run_harness.py` prints a PASS, SKIP, or FAIL line per test and exits with 1 when anything fails. `tests/test_unit.py` needs no server (SSE parsing, request signing, errors, models).
