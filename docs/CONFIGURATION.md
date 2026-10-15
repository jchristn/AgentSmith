# Configuration

Hugo reads its configuration from three places, and knowing which one wins saves a lot of head scratching. The settings file (`hugo.json`) holds almost everything. `HUGO_*` environment variables override a handful of file settings, mostly the ones a container platform needs to inject (ports, database connection, keys). A small set of tenant-independent runtime settings lives in the database instead of the file, so that every replica sees the same value and an operator can change it from the dashboard without touching a mounted file.

Everything in this document comes from the code in `src/Hugo.Server/Settings`, `src/Hugo.Core/Settings/TelemetrySettings.cs`, `src/Hugo.Core/Database/DatabaseSettings.cs`, `HugoSettings.ApplyEnvironment`, `SettingsService`, and `ClusterSettingsService`. Where the plan's Appendix C disagrees (it says `Telemetry.Enable` and `Rest.AllowedOrigins`, for example), the code is right and this page follows the code.

## Where the settings file comes from

At startup the server picks the settings file in this order:

1. `HUGO_SETTINGS_FILE`, when set.
2. The first command-line argument.
3. `hugo.json` in the working directory.
4. `hugo.json` next to the executable. In that case the working directory is switched to the executable's directory, so relative paths such as `data/hugo.db` and `logs/` land beside the binary. That is what happens with `dotnet run --project src/Hugo.Server` from the repository root.

If the chosen file does not exist, Hugo starts with defaults. Unless the file is read-only, the server then writes the merged settings back to the file, so a fresh deployment ends up with a complete `hugo.json` that lists every field. When the write fails (a read-only mount, for example) Hugo marks the file read-only for the rest of the run instead of failing. Set `SettingsFileReadOnly` to `true`, or `HUGO_SETTINGS_READONLY=true`, to stop the write entirely; the settings API then reports file-backed fields as `ConfigMap` and refuses to edit them.

Environment overrides are applied after the file is loaded and are never written back to it. Validation runs last. A bad value stops startup with `[Hugo] invalid settings: ...` on stderr and exit code 1, naming the field. The checks worth knowing about: `Auth.SigningKey` must be at least 16 characters, `Auth.AdminApiKey` must be empty or at least 16 characters, `Rest.ExternalBaseUrl` must be an absolute URL, `Database.Host` is required for PostgreSQL, and `Mcp.InternalPort` must differ from `Rest.Port`.

The Docker image ships its own file at `docker/hugo/hugo.json`, mounted at `/app/config/hugo.json` and named by `HUGO_SETTINGS_FILE`. It binds to `0.0.0.0` and keeps the database, master key file, and logs under `/app/data`.

## Changing settings at runtime

`GET /v1.0/api/settings` (system administrators, `Read` on `Settings`) returns every field with its current value, where the value came from (`File`, `ConfigMap`, `Environment`, or `Database`), whether a change applies `Live` or needs a restart (`RestartRequired`), whether it is editable, and the allowed range. Secret fields come back as `********`; sending the mask back leaves the stored value alone. `PUT /v1.0/api/settings` takes `{ "values": [ { "path": "Approvals.ExpirySeconds", "value": "600" } ] }`, validates every value first, and writes file-backed fields to `hugo.json` and database-backed fields to the database. A field that an environment variable overrides cannot be changed through the API; the response says `OverriddenByEnvironment`, and the fix is to change the environment.

Restart-required changes are saved but take effect only after `POST /v1.0/api/settings/restart` (or its alias `POST /v1.0/api/system/restart`). The process exits with code 75 and relies on the container runtime to start it again, which the Compose stack does through `restart: unless-stopped`. Outside a container a restart simply stops the server. The dashboard's Settings page shows which fields are pending a restart.

Settings that do not appear in the settings API (marked "file only" below) can still be set in `hugo.json`; they just are not editable from the dashboard.

## Settings reference

The tables list every setting the server reads. "Apply" is `Live` when the running server picks up the change immediately and `Restart` when it needs a restart. Ranges are enforced: values outside them are clamped by the settings classes or rejected by the settings API.

### Top level

| Setting | Default | Range | Apply | Environment | Notes |
|---|---|---|---|---|---|
| `NodeId` | null (host name) | | Restart | `HUGO_NODE_ID` | Node name reported by health and server info. File only. |
| `SettingsFileReadOnly` | `false` | | Restart | `HUGO_SETTINGS_READONLY` | Never write the settings file. File only. |
| `CreatedUtc` | first write | | | | Informational timestamp written with the file. |

### Rest

| Setting | Default | Range | Apply | Environment | Notes |
|---|---|---|---|---|---|
| `Rest.Hostname` | `127.0.0.1` | | Restart | `HUGO_REST_HOSTNAME` | Bind address. Containers use `0.0.0.0` (or `*`). |
| `Rest.Port` | `8800` | 1 to 65535 | Restart | `HUGO_REST_PORT` | One port serves REST, MCP, A2A, and the OpenAI-compatible routes. |
| `Rest.Ssl` | `false` | | Restart | | TLS on the listener. Terminate TLS at a proxy or ingress instead. |
| `Rest.ExternalBaseUrl` | null | absolute URL | Live | `HUGO_EXTERNAL_BASE_URL` | Base URL used in A2A agent cards and server info. Empty uses each request's scheme (`X-Forwarded-Proto` when present) and `Host` header. |
| `Rest.CorsOrigins` | `[]` | | Restart | | Allowed CORS origins. Empty allows any origin. File only. |
| `Rest.DatabaseRetryAfterSeconds` | `2` | 1 to 300 | Live | | `Retry-After` on 503 `DatabaseBusy` and `DatabaseUnavailable` responses. |

### Database

| Setting | Default | Range | Apply | Environment | Notes |
|---|---|---|---|---|---|
| `Database.Type` | `Sqlite` | `Sqlite`, `Postgresql` | Restart | `HUGO_DB_TYPE` | Hugo's own storage. SQLite and PostgreSQL only (D-003). |
| `Database.Filename` | `data/hugo.db` | | Restart | `HUGO_DB_FILENAME` | SQLite file. |
| `Database.Host` | `127.0.0.1` | | Restart | `HUGO_DB_HOST`, `HUGO_DB_SERVER` | PostgreSQL host. Both variables set the same field. |
| `Database.Port` | `5432` | 1 to 65535 | Restart | `HUGO_DB_PORT` | |
| `Database.DatabaseName` | `hugo` | | Restart | `HUGO_DB_DATABASE` | |
| `Database.Username` | `postgres` | | Restart | `HUGO_DB_USERNAME` | |
| `Database.Password` | null | | Restart | `HUGO_DB_PASSWORD` | Secret; masked by the settings API. |
| `Database.Schema` | null | | Restart | `HUGO_DB_SCHEMA` | PostgreSQL schema. |
| `Database.TablePrefix` | null | | Restart | | Table name prefix. File only. |
| `Database.ManageSchema` | `true` | | Restart | | When false, Hugo verifies the tables instead of creating and migrating them, and health pings the database instead of reading the schema version. File only. |
| `Database.MaxPoolSize` | `100` | 1 to 1000 | Restart | `HUGO_DB_MAX_POOL_SIZE` | Pooled connections. |
| `Database.MinPoolSize` | `0` | 0 to 1000 | Restart | | Idle connections kept open (PostgreSQL). |
| `Database.ConnectionTimeoutSeconds` | `15` | 1 to 600 | Restart | | Wait for a pooled connection (PostgreSQL). |
| `Database.CommandTimeoutSeconds` | `30` | 0 to 3600 | Restart | | 0 means no limit (PostgreSQL). |
| `Database.ConnectionIdleLifetimeSeconds` | `300` | 1 to 3600 | Restart | | Idle pooled connection lifetime (PostgreSQL). |
| `Database.AdditionalOptions` | null | | Restart | | Extra connection string options, appended verbatim. File only. |

### Logging

| Setting | Default | Range | Apply | Notes |
|---|---|---|---|---|
| `Logging.ConsoleLogging` | `true` | | Restart | |
| `Logging.FileLogging` | `false` | | Restart | Writes dated files under the log directory. |
| `Logging.LogDirectory` | `logs/` | | Restart | |
| `Logging.LogFilename` | `hugo.log` | | Restart | |
| `Logging.MinimumSeverity` | `1` | 0 (debug) to 7 (emergency) | Live | |

### Auth

| Setting | Default | Range | Apply | Environment | Notes |
|---|---|---|---|---|---|
| `Auth.Issuer` | `hugo` | | Restart | | Token issuer. |
| `Auth.SigningKey` | built-in placeholder | 16 characters or more | Restart | `HUGO_AUTH_SIGNING_KEY` | Keys the session token cipher. Startup warns while the placeholder is in use. Changing it invalidates every session. |
| `Auth.UserSessionMinutes` | `60` | 1 to 10080 | Live | | Session lifetime for tenant users and credentials. |
| `Auth.AdminSessionMinutes` | `30` | 1 to 10080 | Live | | Session lifetime for system administrators. |
| `Auth.SessionAbsoluteMaxHours` | `12` | 1 to 720 | Live | | Refresh is refused past this many hours after sign-in. |
| `Auth.AdminApiKey` | empty | empty or 16 characters or more | Live | `HUGO_ADMIN_API_KEY` | Enables the `x-api-key` system administrator scheme. Empty disables it. |
| `Auth.SignedRequestSkewSeconds` | `300` | 10 to 3600 | Live | | Allowed clock skew for signed requests. |
| `Auth.RateLimitPerMinute` | `10` | 1 to 100000 | Live | | Attempts per source IP and per identity per minute on `POST /v1.0/token`, `POST /v1.0/tenants/lookup`, and `POST /v1.0/token/reauthenticate`. |
| `Auth.PasswordHashIterations` | `600000` | 600000 to 10000000 | Live | | PBKDF2-HMAC-SHA256 iterations for new hashes; older hashes are upgraded at sign-in. The settings API advertises a minimum of 10000, but the settings class clamps anything lower to 600000. |
| `Auth.CacheSeconds` | `5` | 0 to 60 | Live | | Database-backed. How long authentication lookups are cached. Changes made through the API drop the cached entry at once; 0 turns the cache off. |

### Secrets

| Setting | Default | Range | Apply | Environment | Notes |
|---|---|---|---|---|---|
| (master key) | none | base64, 32 bytes or more | Restart | `HUGO_MASTER_KEY` | The production way to supply the key. Never copied into settings. |
| (previous key) | none | base64, 32 bytes or more | Restart | `HUGO_MASTER_KEY_PREVIOUS` | Kept readable during rotation. |
| `Secrets.MasterKey` | null | base64, 32 bytes or more | Restart | | Development only; startup warns loudly. |
| `Secrets.MasterKeyFile` | `data/master.key` | | Restart | `HUGO_MASTER_KEY_FILE` | Development fallback. Created with a random key when missing. Empty disables it, and then a missing `HUGO_MASTER_KEY` stops startup. |
| `Secrets.MasterKeyVersion` | `1` | 1 to 255 | Restart | `HUGO_MASTER_KEY_VERSION` | Version new ciphertexts are written under. |
| `Secrets.PreviousMasterKeyVersion` | `0` | 0 to 255 | Restart | `HUGO_MASTER_KEY_PREVIOUS_VERSION` | 0 means `MasterKeyVersion` minus 1. |
| `Secrets.RevealReauthMinutes` | `5` | 1 to 60 | Live | | Window after `POST /v1.0/token/reauthenticate` in which a session may reveal secrets. |
| `Secrets.RevealRateLimitPerMinute` | `10` | 1 to 1000 | Live | | Reveals per source IP and per principal per minute. |
| `Secrets.RotationBatchSize` | `200` | 1 to 5000 | Live | | Rows re-encrypted per transaction during master key rotation. |

The order in which the master key is found is `HUGO_MASTER_KEY`, then `Secrets.MasterKey`, then the key file. [SECURITY.md](SECURITY.md) covers rotation.

### Request history and audit

| Setting | Default | Range | Apply | Notes |
|---|---|---|---|---|
| `RequestHistory.Enabled` | `true` | | Live | Database-backed; the file value is only the initial default. |
| `RequestHistory.RetentionDays` | `30` | 1 to 3650 | Live | Database-backed; the file value is only the initial default. |
| `RequestHistory.MaxRequestBodyBytes` | `65536` | 0 to 1048576 | Live | |
| `RequestHistory.MaxResponseBodyBytes` | `65536` | 0 to 1048576 | Live | |
| `Audit.RetentionDays` | `365` | 1 to 3650 | Live | Database-backed. |

### Telemetry

| Setting | Default | Range | Apply | Environment | Notes |
|---|---|---|---|---|---|
| `Telemetry.Enabled` | `true` | | Restart | `HUGO_TELEMETRY_ENABLE` | Master switch. |
| `Telemetry.ServiceName` | `hugo-server` | | Restart | | |
| `Telemetry.PrometheusEnable` | `true` | | Restart | | Separate Prometheus scrape listener. |
| `Telemetry.PrometheusHostname` | `*` | | Restart | | File only. |
| `Telemetry.PrometheusPort` | `9464` | 1 to 65535 | Restart | | |
| `Telemetry.PrometheusPath` | `/metrics` | | Restart | | File only. |
| `Telemetry.ServeMetricsOnServicePort` | `true` | | Live | | Also serve `GET /metrics` on the REST port, unauthenticated. Turn it off if the REST port is exposed beyond your network. |
| `Telemetry.OtlpEnable` | `false` | | Restart | | |
| `Telemetry.OtlpEndpoint` | `http://127.0.0.1:4317` | | Restart | `HUGO_OTLP_ENDPOINT` | Setting the variable also turns OTLP export on. |
| `Telemetry.MaxLabelValues` | `200` | 10 to 10000 | Restart | | Distinct values per metric label before new ones fold into `other`. File only. |
| `Telemetry.Exemplars` | `true` | | Restart | | Trace exemplars on duration histograms. File only. |

Metric and trace details live in [TELEMETRY.md](TELEMETRY.md).

### Observability links

The observability links live in the database so the dashboard's external services card works the same on every replica. Each has an environment variable that overrides it; when the variable is set, the field shows `Environment` and is not editable.

| Setting | Environment | Purpose |
|---|---|---|
| `Observability.GrafanaUrl` | `HUGO_GRAFANA_URL` | Grafana as reached from a browser. |
| `Observability.PrometheusUrl` | `HUGO_PROMETHEUS_URL` | Prometheus as reached from a browser. |
| `Observability.LokiUrl` | `HUGO_LOKI_URL` | Loki as reached from a browser. |
| `Observability.TempoUrl` | `HUGO_TEMPO_URL` | Tempo as reached from a browser. |
| `Observability.GrafanaInternalUrl` | `HUGO_GRAFANA_INTERNAL_URL` | Grafana as probed by the server. Empty uses the browser URL. |
| `Observability.PrometheusInternalUrl` | `HUGO_PROMETHEUS_INTERNAL_URL` | Same, for Prometheus. |
| `Observability.LokiInternalUrl` | `HUGO_LOKI_INTERNAL_URL` | Same, for Loki. |
| `Observability.TempoInternalUrl` | `HUGO_TEMPO_INTERNAL_URL` | Same, for Tempo. |

All default to empty, which means "not configured".

### Agent

| Setting | Default | Range | Apply | Notes |
|---|---|---|---|---|
| `Agent.MaxParallelToolCalls` | `4` | 1 to 64 | Live | Independent Read calls in one iteration that may run at once. Writes always run one at a time. |
| `Agent.ToolRetrievalThreshold` | `40` | 1 to 10000 | Live | Above this many visible tools, each turn retrieves the most relevant ones by embedding similarity. |
| `Agent.CompactionThresholdRatio` | `0.75` | 0.1 to 0.99 | Live | Fraction of the model's context window at which old turns are compacted into a summary. File only. |
| `Agent.DefaultMaxIterations` | `12` | 1 to 200 | Live | Present in the settings API but not read anywhere today; each profile carries its own `maxIterations` (default 12). |
| `Agent.DefaultMaxToolCallsPerTurn` | `24` | 1 to 500 | Live | Same situation: profiles carry `maxToolCallsPerTurn` (default 24). |

### Approvals

| Setting | Default | Range | Apply | Notes |
|---|---|---|---|---|
| `Approvals.ExpirySeconds` | `900` | 30 to 604800 | Live | A pending approval expires after this long. |
| `Approvals.McpWaitSeconds` | `90` | 0 to 600 | Live | How long an MCP tool call (and `ask_agent_smith`, and `POST /v1.0/api/agent-profiles/{id}/ask`) waits for a decision before answering that the approval is pending. |
| `Approvals.AllowSelfApproval` | `true` | | Live | Whether a requester may decide their own approval. A2A callers can never decide their own. |

### Connectors

| Setting | Default | Range | Apply | Environment | Notes |
|---|---|---|---|---|---|
| `Connectors.EnablePlugins` | `false` | | Restart | `HUGO_ENABLE_PLUGINS` | Load connector plugins from the plugin directory. |
| `Connectors.PluginDirectory` | `plugins` | | Restart | | |
| `Connectors.DefaultToolTimeoutSeconds` | `120` | 1 to 3600 | Live | | Per-call timeout for ordinary tools. |
| `Connectors.ModelToolTimeoutSeconds` | `600` | 1 to 3600 | Live | | Timeout for tools that call the profile's model (for example `sql_generate_context`). |
| `Connectors.ModelCallBudgetPerInvocation` | `60` | 0 to 10000 | Live | | Model calls one tool invocation may make before further calls fail with `rate_limited`. |
| `Connectors.MaxConcurrencyPerResource` | `4` | 1 to 256 | Live | | Concurrent tool calls per resource; extra calls wait. |
| `Connectors.SessionIdleMinutes` | `10` | 1 to 1440 | Live | | Idle pooled connector sessions (SSH clients, database connections) are closed after this. The pool reads the value at startup. |
| `Connectors.TestTimeoutSeconds` | `30` | 1 to 600 | Live | | Connection tests are cancelled after this (D-036). |
| `Connectors.McpAllowStdio` | `false` | | Restart | | Lets `mcp` resources launch local MCP servers over stdio (each resource must also opt in). File only. |
| `Connectors.AllowStdioMcp` | `false` | | Restart | | Shown in the settings API, but not read by the server. Use `Connectors.McpAllowStdio`. |
| `Connectors.HealthSweepMinutes` | `15` | 0 to 1440 | Live | | Reserved for a resource health sweep that is not implemented yet; the value has no effect today. |

### Instructions

| Setting | Default | Range | Apply | Notes |
|---|---|---|---|---|
| `Instructions.CompactBindingThreshold` | `3` | 0 to 1000 | Live | Profiles with `instructionDetail: Auto` switch to compact connector text above this many bindings (D-043). |
| `Instructions.MaxKnowledgeCharsPerResource` | `2000` | 0 to 100000 | Live | Characters of each resource's root knowledge entry included in compiled instructions. |
| `Instructions.CacheEntries` | `1024` | 0 to 100000 | Live | Compiled instruction cache size; 0 disables the cache. |

[PROMPTS.md](PROMPTS.md) explains what these do to the text a caller receives.

### Models

| Setting | Default | Range | Apply | Notes |
|---|---|---|---|---|
| `Models.HealthIntervalSeconds` | `300` | 0 to 86400 | Live | Background model endpoint health checks; 0 turns them off. |

### Protocol surfaces

| Setting | Default | Range | Apply | Notes |
|---|---|---|---|---|
| `Mcp.Enable` | `true` | | Restart | Serve `/mcp` and `/mcp/{profileName}`. |
| `Mcp.InternalPort` | `0` | 0 to 65535 | Restart | Loopback port of the internal MCP listener behind the proxy; 0 picks a free port. Must differ from `Rest.Port`. |
| `Mcp.SessionIdleTimeoutSeconds` | `900` | 60 to 604800 | Live | Idle MCP sessions are dropped after this. |
| `Mcp.MaxMessageSize` | `4194304` | 1024 to 268435456 | Restart | Largest MCP message in bytes. |
| `Mcp.ProgressIntervalSeconds` | `10` | 1 to 60 | Live | Progress notification interval while an MCP call waits for approval (sent when the client asked for progress). |
| `A2A.Enable` | `true` | | Restart | Serve the A2A routes. |
| `OpenAI.Enable` | `true` | | Restart | Serve the OpenAI-compatible routes. |

## Environment variables at a glance

The server reads these variables. Anything not listed here has to be set in `hugo.json`.

| Variable | Sets |
|---|---|
| `HUGO_SETTINGS_FILE` | Settings file path |
| `HUGO_SETTINGS_READONLY` | `SettingsFileReadOnly` (`true` to stop writes) |
| `HUGO_NODE_ID` | `NodeId` |
| `HUGO_REST_HOSTNAME`, `HUGO_REST_PORT`, `HUGO_EXTERNAL_BASE_URL` | `Rest.Hostname`, `Rest.Port`, `Rest.ExternalBaseUrl` |
| `HUGO_DB_TYPE`, `HUGO_DB_FILENAME`, `HUGO_DB_HOST` (or `HUGO_DB_SERVER`), `HUGO_DB_PORT`, `HUGO_DB_DATABASE`, `HUGO_DB_USERNAME`, `HUGO_DB_PASSWORD`, `HUGO_DB_SCHEMA`, `HUGO_DB_MAX_POOL_SIZE` | `Database.*` |
| `HUGO_AUTH_SIGNING_KEY` | `Auth.SigningKey` |
| `HUGO_ADMIN_API_KEY` | `Auth.AdminApiKey` |
| `HUGO_MASTER_KEY`, `HUGO_MASTER_KEY_PREVIOUS` | Master keys (read directly by the key loader) |
| `HUGO_MASTER_KEY_VERSION`, `HUGO_MASTER_KEY_PREVIOUS_VERSION`, `HUGO_MASTER_KEY_FILE` | `Secrets.MasterKeyVersion`, `Secrets.PreviousMasterKeyVersion`, `Secrets.MasterKeyFile` |
| `HUGO_OTLP_ENDPOINT` | `Telemetry.OtlpEndpoint` and turns on `Telemetry.OtlpEnable` |
| `HUGO_TELEMETRY_ENABLE` | `Telemetry.Enabled` |
| `HUGO_ENABLE_PLUGINS` | `Connectors.EnablePlugins` |
| `HUGO_GRAFANA_URL`, `HUGO_PROMETHEUS_URL`, `HUGO_LOKI_URL`, `HUGO_TEMPO_URL` and their `_INTERNAL_URL` forms | `Observability.*` |

A variable holding the wrong type (a word where a port belongs) stops startup with a message that names both the variable and the field. Variables whose names start with `HUGO_TESTBED` or `HUGO_TEST_` belong to the test suites and are described in [DEVELOPMENT.md](DEVELOPMENT.md).

## The Docker stack's `.env`

`docker/compose.yaml` takes its values from `docker/.env`, which `docker/start.sh` creates from `docker/.env.example` on first run and fills with a fresh master key and signing key. The file is git-ignored. Back it up: losing `HUGO_MASTER_KEY` makes every stored secret unreadable.

| Variable | Default | Used for |
|---|---|---|
| `HUGO_MASTER_KEY` | generated by `start.sh` | Passed to the server. If it is missing from `.env`, Compose falls back to a fixed development key that is published in `compose.yaml`; never run that way with real secrets. |
| `HUGO_AUTH_SIGNING_KEY` | generated by `start.sh` | Passed to the server, with a similar published development fallback. |
| `HUGO_DB_TYPE` | `Sqlite` | `Postgresql` also needs `docker compose --profile postgres up -d`. |
| `HUGO_DB_PASSWORD` | `hugo` | Password of the bundled PostgreSQL and of Hugo's connection to it. |
| `HUGO_REST_PORT` | `8800` | Host port for REST, MCP, A2A, and OpenAI-compatible routes. Also builds the default `HUGO_EXTERNAL_BASE_URL`. |
| `HUGO_DASHBOARD_PORT` | `8801` | Host port for the dashboard. |
| `HUGO_GRAFANA_PORT`, `HUGO_PROMETHEUS_PORT`, `HUGO_TEMPO_PORT`, `HUGO_LOKI_PORT` | `3000`, `9090`, `3200`, `3100` | Host ports of the observability stack, also used to build the `HUGO_*_URL` links. |
| `HUGO_EXTERNAL_BASE_URL` | `http://127.0.0.1:${HUGO_REST_PORT}` | Not in the example file, but honored if you add it. Set it when Hugo sits behind a proxy so agent cards carry the right URL. |
| `GF_SECURITY_ADMIN_USER`, `GF_SECURITY_ADMIN_PASSWORD` | `admin`, `admin` | Grafana login. Change them anywhere but your own machine. |

Every host port binds to `127.0.0.1`. Inside the stack the server always listens on 8800 and reaches its peers by service name (`hugo-postgres`, `hugo-alloy:4317`, `hugo-grafana:3000`, and so on). [DOCKER.md](DOCKER.md) covers networking, PostgreSQL, and upgrades; Kubernetes deployments are configured through the manifests described in [KUBERNETES.md](KUBERNETES.md).
