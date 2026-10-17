# CLAUDE.md

Guidance for agents working in this repository. Read `archive/HUGO_PLAN.md` first: it is the build plan, the decision log, and the progress tracker. Annotate tasks there as you complete them (format in section 1 of the plan).

## What Hugo is

A multi-tenant super agent server. Operators configure resources (SSH hosts, object storage, databases, log services, caches, clouds, clusters, HTTP APIs), store their credential material encrypted, and compose resources into agent profiles. Profiles are reachable over MCP, A2A, REST, the SDKs, an OpenAI-compatible endpoint, and dashboard chat. The default profile is `Hugo`, whose persona calls itself Agent Smith and addresses the operator as Mr. Anderson. The GitHub repository is `https://github.com/jchristn/AgentSmith`.

## Layout

| Path | Contents |
|---|---|
| `src/Hugo.Core` | Models, enums, settings models, data layer (Sqlite, Postgresql), security primitives, secret cipher, template renderer, instruction compiler, services. |
| `src/Hugo.Connectors.Abstractions` | The connector contract (`IConnector`, `IConnectorTool`, descriptors, effects). No dependency on Hugo.Core. |
| `src/Hugo.Connectors.*` | One project per service family (Ssh, Storage, Sql, Logging, Redis, Aws, Kubernetes, Docker, Http, Observability, Agents). |
| `src/Hugo.Server` | Watson 7 host, routes, auth, tool invocation pipeline, agent runtime, MCP (Voltaic), A2A, OpenAI-compatible surface. |
| `src/Hugo.Cli` | `hugo` .NET tool (stdio MCP relay, login, whoami). |
| `src/Test.*` | Touchstone suites and runners. |
| `dashboard/` | React 19 + Vite 6 operator console. |
| `sdk/{csharp,python,js}` | SDKs, each with a README and a test harness. |
| `docker/` | Compose stack, `update.{sh,bat}`, `factory/`, and `testbed/` (connector test targets). |
| `docs/` | Architecture, configuration, connectors, security, safety model, prompts, telemetry, i18n. |

## Build and test

```bash
dotnet build src/Hugo.sln
dotnet run --project src/Test.Automated -f net10.0                         # Touchstone suites (Sqlite by default)
HUGO_TEST_PROVIDERS=sqlite,postgresql dotnet run --project src/Test.Automated -f net10.0
HUGO_TESTBED=1 dotnet run --project src/Test.Automated -f net10.0 -- --suites testbed
dotnet test src/Test.Xunit -f net10.0
dotnet test src/Test.Nunit -f net10.0
docker compose -f docker/testbed/compose.all.yaml up -d --wait             # all connector test targets
cd dashboard && npm install && npm run lint && npm run build && npm test
scripts/check-em-dash.sh
```

## Non-negotiable rules

These come from `~/Code/Agents/requirements` and apply to every change.

### C# code style (follow strictly)

- The namespace declaration is at the top, and `using` statements are INSIDE the namespace block.
- Microsoft and standard system library usings first, alphabetical, then other usings, alphabetical.
- All public members, constructors, and public methods have XML documentation. Private members and private methods have none.
- Public members use explicit getters and setters with backing variables when the value requires range or null validation.
- Private member variables are `_PascalCase` (`_FooBar`, never `_fooBar`).
- No tuples unless absolutely, absolutely necessary.
- Async calls use `.ConfigureAwait(false)`.
- Every async method accepts a `CancellationToken`, unless the class owns a `CancellationToken` or `CancellationTokenSource` member.
- Async methods check for cancellation at appropriate places.
- Never use `var`. Use the actual type.
- Manually prepared SQL strings are intentional. Every value is a parameter; never concatenate values into SQL.
- Keep READMEs accurate when you change behavior.
- Compile and leave zero errors and zero warnings.
- Exactly one class or one enum per file. No nested classes or enums.
- A method returning `IEnumerable` gets an async variant with a `CancellationToken`.
- No constants for things a developer may want to configure: use a public member with a private backing field and a sensible default.
- Document defaults, minimums, maximums, and what values mean.
- Use specific exception types with meaningful, contextual messages; document them with `/// <exception>`; use exception filters where appropriate.
- Implement `IDisposable`/`IAsyncDisposable` with the full pattern (`protected virtual void Dispose(bool disposing)`).
- Classic `using (...) { }` statements, not using declarations.
- Nullable reference types enabled; validate inputs with guard clauses; document nullability and thread safety.
- Prefer LINQ where readable; `.Any()` over `.Count() > 0`; `.FirstOrDefault()` with null checks.
- No `Console.WriteLine` in library code. No partial classes.
- No `JsonElement`/`JsonNode` DOM navigation for fixed contracts: define a typed class.

### Architecture rules

- Watson 7 (`Watson` package) is the only HTTP stack. Routes are registered by per-feature registrar classes on `server.Routes.{PreAuthentication|PostAuthentication}.{Static|Parameter}`.
- Management API under `/v1.0/api/...`; tokens at `/v1.0/token`; MCP at `/mcp`; A2A at `/v1.0/a2a/{tenantId}/{profileName}`.
- Every tenant-owned query filters by tenant ID from the `RequestContext`. Cross-tenant reads return 404.
- Every route, MCP tool, and A2A method has an entry in the operation scope map (resource type and operation).
- IDs are PrettyId K-sortable strings with the prefixes in `Constants.cs`.
- Structured persistence: columns and child tables, never whole-object JSON blobs. The only unmanaged JSON columns are listed in plan section 7.6.
- The core never contains connector-specific code. Connectors depend only on `Hugo.Connectors.Abstractions` plus their client libraries.
- Every tool call from every surface goes through `ToolInvocationService`. No surface calls a connector directly.
- Secrets never appear in prompts, logs, request history, audit descriptions, or tool output (the scrubber runs on all of them).
- Loopback is always `127.0.0.1`, never `localhost`.

### Writing

- Never use em-dashes anywhere: code, comments, docs, Postman, commit messages. Restructure the sentence.
- Documentation is reviewed for human voice (`WRITING_DOCUMENTS.md`).

### Versioning

- The product version is `0.1.0-alpha`, defined once in `src/Directory.Build.props` and mirrored in the dashboard and SDK manifests.
- Never change a version number unless the user explicitly asks for that change.

### Git

- The user has authorized commits and pushes for plan execution. Commit messages describe the plan task IDs completed.
