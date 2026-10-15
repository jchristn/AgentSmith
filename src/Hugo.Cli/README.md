# hugo (Hugo.Cli)

`hugo` is the command line for the [Hugo](https://github.com/jchristn/AgentSmith) super agent server. It does three jobs:

- **`hugo mcp stdio`** relays the MCP stdio transport to Hugo's Streamable HTTP MCP endpoint. Use it with MCP clients that can only launch stdio servers, or when you'd rather keep the token out of the client's config.
- **`hugo login` / `hugo logout`** sign in and keep a session token in `~/.hugo/credentials.json`.
- **`hugo whoami` / `hugo profiles list`** show who you are signed in as and which agent profiles you can use.

## Install

```bash
dotnet tool install -g Hugo.Cli
hugo --version
```

You need the .NET 8 or .NET 10 runtime. To update, run `dotnet tool update -g Hugo.Cli`. To remove it, run `dotnet tool uninstall -g Hugo.Cli`.

## Sign in

```bash
# Email and password (the password is read without echo and never stored)
hugo login --server http://127.0.0.1:8800 --email anderson@example.com

# Access key: the keys are exchanged for a session token, and both are stored so the
# stdio relay can renew the session when it expires (add --no-store-key to store only the token)
hugo login --server https://hugo.example.com --access-key AK... --tenant ten_...

hugo whoami
hugo profiles list
hugo logout
```

`hugo login` writes `~/.hugo/credentials.json` with mode 0600 (its directory gets 0700). The file holds one entry per server, and the most recent sign-in becomes the default server. Passwords are never written. When standard input is not a terminal, `login` reads the email (unless `--email` is given) and the password or secret key as lines from standard input, and `--password-stdin` reads the password from the first line:

```bash
printf '%s\n' "$HUGO_PASSWORD" | hugo login --email anderson@example.com --password-stdin
```

If the account must change its password, `login` and `whoami` say so on stderr. Change it in the dashboard, then sign in again.

## Credentials and precedence

Every command that talks to the server picks its credentials as a set from the first source that has any:

1. Flags: `--token`, or `--access-key` with `--secret-key`.
2. Environment: `HUGO_TOKEN`, or `HUGO_ACCESS_KEY` with `HUGO_SECRET_KEY`.
3. The login cache entry for the chosen server.

Sources are never mixed, so a cached token for one identity is never paired with keys for another. The other settings resolve the same way:

| Setting | Flag | Environment | Fallback |
|---|---|---|---|
| Server | `--server` | `HUGO_SERVER` | The last server you signed in to, then `http://127.0.0.1:8800` |
| Tenant | `--tenant` | `HUGO_TENANT` | The cached entry's tenant |
| Profile (`mcp stdio`) | `--profile` | `HUGO_PROFILE` | None: the server resolves the profile |
| Credential file | `--credentials-file` | `HUGO_CREDENTIALS_FILE` | `~/.hugo/credentials.json` |

Paths with spaces work. With an access key (from any source), an expired session token is renewed once on a 401. A token renewed from cached keys is written back to the cache. Tokens obtained from flag or environment keys are never written to disk.

## Using Hugo from Claude Code

### HTTP (preferred when the client supports it)

Claude Code speaks Streamable HTTP directly, so you don't need the relay:

```json
{
  "mcpServers": {
    "hugo": {
      "type": "http",
      "url": "http://127.0.0.1:8800/mcp",
      "headers": { "Authorization": "Bearer ${HUGO_TOKEN}" }
    }
  }
}
```

Use `http://127.0.0.1:8800/mcp/{profileName}` to pin a profile, for example `/mcp/Hugo`.

### stdio through `hugo mcp stdio`

```bash
hugo login --server http://127.0.0.1:8800 --access-key AK...
claude mcp add hugo -- hugo mcp stdio --server http://127.0.0.1:8800 --profile Hugo
```

Or in `.mcp.json`:

```json
{
  "mcpServers": {
    "hugo": {
      "type": "stdio",
      "command": "hugo",
      "args": ["mcp", "stdio", "--server", "http://127.0.0.1:8800", "--profile", "Hugo"]
    }
  }
}
```

Without a login cache (CI, containers), pass the keys through the environment:

```json
{
  "mcpServers": {
    "hugo": {
      "type": "stdio",
      "command": "hugo",
      "args": ["mcp", "stdio"],
      "env": {
        "HUGO_SERVER": "https://hugo.example.com",
        "HUGO_ACCESS_KEY": "${HUGO_ACCESS_KEY}",
        "HUGO_SECRET_KEY": "${HUGO_SECRET_KEY}",
        "HUGO_PROFILE": "Hugo"
      }
    }
  }
}
```

Credentials with a bound profile are the recommended way to configure a harness.

## What the stdio relay does

- It reads one JSON-RPC message per line from stdin and POSTs it to `{server}/mcp` (or `/mcp/{profile}`, percent-encoded) with `Authorization: Bearer`, `Accept: application/json, text/event-stream`, and, once the session exists, `MCP-Session-Id` and `MCP-Protocol-Version`.
- It writes everything the server sends back to stdout, one compact message per line: JSON responses, and every event of an event-stream response (progress, log messages, server requests such as `roots/list` or sampling, then the response). Stdout carries protocol messages only. Diagnostics, including `--verbose` output, go to stderr.
- Requests run concurrently, so a long tool call doesn't block other requests. `initialize`, notifications, and your answers to server requests are forwarded in order. Notifications produce no output.
- After `notifications/initialized` it opens the GET stream for server-initiated messages (`--no-listen` turns this off). A server that answers 405 simply has no such stream.
- A 401 renews the session token once when an access key is available. Otherwise the request is answered with a JSON-RPC error (`-32603`, `data.httpStatus: 401`) and stderr explains what to do.
- If the server answers 404 on an existing session, the relay replays your `initialize` on a new session and retries the request.
- When a response stream breaks before its response, the relay resumes it with `Last-Event-ID`. Under the stateless `2026-07-28` revision it re-issues the request instead.
- Under `2026-07-28` (requests that carry `_meta["io.modelcontextprotocol/protocolVersion"]`), it sends `MCP-Protocol-Version`, `Mcp-Method`, `Mcp-Name` (Base64-encoded when the value isn't plain ASCII), and `Mcp-Param-{Name}` for top-level tool parameters annotated with `x-mcp-header`, and no session header.
- Transport failures (server unreachable, unexpected status) are answered as JSON-RPC errors carrying the request's ID, so the client never waits forever. A line that isn't JSON gets a `-32700` error.
- When stdin closes, the relay gives requests still in flight up to 2 seconds, deletes the session (`DELETE` with `MCP-Session-Id`), and exits 0.

## Commands

| Command | Purpose |
|---|---|
| `hugo mcp stdio` | Relay MCP over stdio to a Hugo server |
| `hugo login` | Sign in and cache a session token |
| `hugo logout` | Revoke the cached token (`DELETE /v1.0/token`) and remove the entry; `--no-revoke` only forgets it |
| `hugo whoami` | Show the principal behind the current credentials (`GET /v1.0/token/details`); `--json` prints JSON |
| `hugo profiles list` | List agent profiles and their MCP endpoints (`GET /v1.0/api/agent-profiles`); `--json` prints the raw body |
| `hugo --version` | Print the version |

Every command takes `--help`, and `hugo help <command>` prints the same text.

Exit codes: `0` success, `1` error (server, network, credentials, files), `2` invalid command line.

`hugo profiles list` explains when a server doesn't have the profile list route yet (HTTP 404). In that case, pass the profile name your administrator gives you to `hugo mcp stdio --profile`.
