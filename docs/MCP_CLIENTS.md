# Connecting MCP clients

Hugo serves MCP on the same port as its REST API, at `/mcp` and `/mcp/{profileName}`, over Streamable HTTP. This page shows how to point Claude Code (and any other MCP client) at it. For the tools you will see once connected, read [MCP_CONNECTOR_TOOLS.md](MCP_CONNECTOR_TOOLS.md).

## What you need

- A running Hugo server, for example `http://127.0.0.1:8800`.
- A credential. The recommended setup is an access key and secret created for the agent profile you want the client to use, with **Lock to profile** turned on. The credential then always acts as that profile, whatever path or header the client sends. Create one in the dashboard (Credentials, then New), or over REST:

  ```bash
  curl -s -X POST http://127.0.0.1:8800/v1.0/api/tenants/default/credentials \
    -H "Authorization: Bearer $HUGO_TOKEN" -H "Content-Type: application/json" \
    -d '{"name":"claude-code","agentProfileId":"<profile id>","lockToProfile":true}'
  ```

  The response carries `accessKey` and `secretKey`. The secret is shown once.

A session token (`Authorization: Bearer ...` from `POST /v1.0/token`) also works, but it expires after an hour, so it suits quick tests better than a standing configuration.

## Which profile the client gets

Hugo picks the profile for each request in this order:

1. The profile in the path, `/mcp/{profileName}` (a name or an id).
2. The `x-hugo-profile` header.
3. The profile the credential is bound to.
4. The user's default profile, then the tenant's default profile (`Hugo`).

A credential locked to its profile is refused (403 `ProfileLocked`) on any other profile. A profile with **Expose over MCP** turned off answers 403 `McpNotExposed`.

## Claude Code over HTTP

Add Hugo with the access key headers:

```bash
claude mcp add --transport http hugo http://127.0.0.1:8800/mcp \
  --header "x-access-key: $HUGO_ACCESS_KEY" \
  --header "x-secret-key: $HUGO_SECRET_KEY"
```

Or put the same thing in a project `.mcp.json` (or pass a file with `--mcp-config`):

```json
{
  "mcpServers": {
    "hugo": {
      "type": "http",
      "url": "http://127.0.0.1:8800/mcp",
      "headers": {
        "x-access-key": "${HUGO_ACCESS_KEY}",
        "x-secret-key": "${HUGO_SECRET_KEY}"
      }
    }
  }
}
```

With a session token instead, use `"Authorization": "Bearer ${HUGO_TOKEN}"`. To pick a profile that the credential is not bound to, use `http://127.0.0.1:8800/mcp/<profile name>` as the URL or add an `x-hugo-profile` header.

Once connected, Claude Code receives the profile's compiled instructions (Agent Smith's persona, the safety rules, and every bound resource) in the MCP `initialize` answer, and `/mcp` in Claude Code lists the tools. A quick non-interactive check:

```bash
claude -p "Use the hugo tools to list the tables in shop-db and count the rows in each." \
  --mcp-config ./hugo-mcp.json --allowedTools "mcp__hugo"
```

## Claude Code over stdio

Clients that only speak stdio can use the `hugo` CLI as a relay. It reads the credential from `HUGO_ACCESS_KEY` and `HUGO_SECRET_KEY` (or the login cache) and forwards stdio to Streamable HTTP:

```bash
claude mcp add hugo -- hugo mcp stdio --server http://127.0.0.1:8800 --profile Hugo
```

```json
{
  "mcpServers": {
    "hugo": {
      "command": "hugo",
      "args": ["mcp", "stdio", "--server", "http://127.0.0.1:8800", "--profile", "Hugo"],
      "env": { "HUGO_ACCESS_KEY": "...", "HUGO_SECRET_KEY": "..." }
    }
  }
}
```

## Approvals

A Write or Destructive call on a binding that needs approval waits on the server for up to `Approvals.McpWaitSeconds` (90 by default). Clients that send a progress token get a progress notification every `Mcp.ProgressIntervalSeconds` (10 by default) while it waits. If nobody decides in time, the call returns a normal (non-error) result:

```json
{ "status": "approval_pending", "approvalId": "apr_...", "summary": "...", "next": "call hugo_await_approval with this approvalId" }
```

Approve or reject it in the dashboard (Approvals) or over REST (`POST /v1.0/api/approvals/{id}/approve`), then call `hugo_await_approval` with the `approvalId` to get the original call's result.

## Errors

- Authentication failures are HTTP 401 with `WWW-Authenticate: Bearer realm="hugo"` (and `error="invalid_token"` when credentials were sent but rejected).
- Profile problems are HTTP errors with a JSON-RPC error body whose message starts with a stable code: `ProfileNotFound` (404), `ProfileLocked`, `McpNotExposed`, `Forbidden` (403).
- Everything a tool does wrong is a tool result with `isError: true`. The text starts with a stable code (`readonly_binding:`, `invalid_argument:`, `binding_not_found:`, `forbidden:`, `unmapped_tool:`, and the connector codes), and `structuredContent` carries `{ "error": "<code>", "message": "..." }`.

## Protocol notes

- Hugo negotiates any revision Voltaic 2.3.0 supports, from 2024-11-05 through the stateless 2026-07-28 revision. Session-based clients get an `Mcp-Session-Id` header on `initialize`; a session is bound to the principal and profile that opened it, and a session used on another profile answers 404 so the client starts a new one.
- Sessions live in server memory and end after `Mcp.SessionIdleTimeoutSeconds` (900 by default) or a restart. Clients reinitialize on 404, as the specification requires.
- Responses are JSON, or a server-sent event stream when the server sends notifications (progress) while a request runs. Event streams end with the response and close the connection.
