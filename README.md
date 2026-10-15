<p align="center"><img src="https://raw.githubusercontent.com/jchristn/AgentSmith/main/assets/logo.png" alt="Hugo" width="160" /></p>

# Hugo

Hugo is a multi-tenant super agent server. You give it access to the systems that run your production stack (SSH hosts, object storage, databases, log services, caches, cloud control planes, clusters, HTTP APIs), group that access into agent profiles, and reach those profiles from any agent or harness over MCP or A2A, from the REST API and SDKs, or by chatting in the dashboard.

The default profile is named Hugo. It calls itself Agent Smith, and it calls you Mr. Anderson.

> Hugo is under active development (`0.1.0-alpha`). The build plan and progress tracker is [`HUGO_PLAN.md`](HUGO_PLAN.md).

## Run it locally

```bash
dotnet build src/Hugo.sln
dotnet run --project src/Hugo.Server -f net10.0
curl http://127.0.0.1:8800/v1.0/api/health
```

The server reads `hugo.json` (the copy next to the executable when the working directory has none) and applies `HUGO_*` environment overrides. It listens on `127.0.0.1:8800` and keeps a SQLite database in `data/hugo.db`.

First boot creates the `default` tenant, a system administrator `admin@hugo.local`, and a tenant administrator `anderson@hugo.local` (Thomas Anderson). Both start with the password `password` and must change it before using anything else:

```bash
curl -X POST http://127.0.0.1:8800/v1.0/token -H "x-email: anderson@hugo.local" -H "x-password: password"
curl -X POST http://127.0.0.1:8800/v1.0/api/users/me/password -H "Authorization: Bearer <token>" \
  -d '{"currentPassword":"password","newPassword":"<new password>"}'
```

Secrets are encrypted with a master key from `HUGO_MASTER_KEY` (base64, 32 bytes or more; for example `openssl rand -base64 32`). Without it, a development key file is generated in `data/master.key` and the server logs a loud warning. Never run production that way.

## Test

```bash
dotnet run --project src/Test.Automated -f net10.0
HUGO_TEST_POSTGRES="Host=127.0.0.1;Port=55432;Database=hugo_test;Username=postgres;Password=postgres" \
  dotnet run --project src/Test.Automated -f net10.0 -- --providers postgresql
```

## License

MIT. See [`LICENSE.md`](LICENSE.md).
