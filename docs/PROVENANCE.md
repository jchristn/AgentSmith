# Provenance

Hugo's foundation is forked from Garrison (decision D-032). This file records what was taken, from where, and what changed, so a future reader can trace any file back to its origin.

## Source

| Item | Value |
|---|---|
| Repository | `~/Code/Garrison` (https://github.com/jchristn/Garrison) |
| Branch and commit | `main` at `693ab8ece3dfba3839f2e4bcc1c4bb17745bfa2c` |
| Forked on | 2026-10-14 |
| License | MIT (same author) |

The fork was a copy followed by a rename: `Garrison` became `Hugo`, `GARRISON_` became `HUGO_`, `garrison.json` became `hugo.json`, and the ports moved from 8700, 8701, and 8702 to 8800, 8801, and 8802.

## What was forked

### `src/Garrison.Core` to `src/Hugo.Core`

| Area | Files | Notes |
|---|---|---|
| Data layer machinery | `Database/DatabaseDriverBase.cs`, `DatabaseDriverFactory.cs`, `DatabaseSettings.cs`, `DatabaseFailures.cs`, `DatabaseLockHandle.cs`, `SchemaMigration.cs`, `SchemaColumnAddition.cs`, `TableCatalog.cs`, `Sql/SqlDialect.cs`, `Sqlite/*`, `Postgresql/*`, `Telemetry/*`, `Ado/AdoDatabaseDriver.cs`, `Ado/AdoConverters.cs`, `Ado/AdoEnumerationSql.cs` | The driver's method surface was trimmed to Hugo's entities. |
| Entity data access | `Ado/Ado{Tenant,User,Credential,AuthSession,Role,Permission,RolePermissionMap,UserRoleAssignment,CredentialScopeAssignment,AuditRecord,RequestHistoryEntry,PromptTemplate,PromptTemplateVersion,ClusterSetting,RequestNonce,RateLimitBucket}Methods.cs` and their interfaces | Columns renamed to snake_case; Hugo columns added (see below). |
| Schema | `Ado/SchemaBuilder.cs` | Rewritten: Hugo has its own migration 1 (identity, security, history, settings, prompt templates) and migration 2 (secrets vault). Garrison's 27 migrations were not carried over. |
| Models, requests, responses | The identity, audit, request history, settings, prompt template, token, and server info types | Garrison-specific fields removed; Hugo fields added. |
| Enumeration and serialization | `Enumeration/*`, `Serialization/Serializer.cs` | Unchanged apart from the rename. |
| Security | `PasswordHasher`, `PermissionEvaluator`, `BuiltInRole`, `BuiltInPermission`, `BuiltInRoles`, `RequestContext`, `RequestSigner`, `SecretGenerator`, `TokenPayload`, `TokenService`, `AuthorizationDecision`, `PermissionGrant` | `BuiltInRoles` rewritten for plan section 15.4; `PermissionEvaluator` gained the Reveal rule; `SecretCipher` rewritten (see below). |
| Telemetry | `HugoMetrics`, `HugoMetricCatalog`, `HugoTelemetry`, `HugoTracing`, `LabelGuard`, `MetricDescriptor`, `GaugeRegistration`, `MetricsScrape`, `TelemetryBuckets`, `TelemetryHttpHandler` | Catalog trimmed to the metrics Hugo emits today, plus the `hugo_auth_requests`, `hugo_authz_requests`, `hugo_secret_operations`, `hugo_secret_redactions`, and `hugo_background_jobs` families. |
| Templates | `Templates/*` | The Codex filter was removed, and the known variable roots and sample context follow plan section 10.3. |
| Services | `AuditService`, `DefaultSeeder`, `SeedOptions`, `ConflictException`, `NotFoundException`, `ValidationException`, and two methods of `ConfigurationValidator` | The seeder now creates the Hugo accounts; the audit service scrubs descriptions. |

### `src/Garrison.Server` to `src/Hugo.Server`

| Area | Files | Notes |
|---|---|---|
| Host | `Program.cs`, `Bootstrapper.cs`, `GarrisonServer.cs` (now `HugoServer.cs`), `ServerContext.cs`, `Modules/IServerModule.cs`, `Modules/IdentityModule.cs`, `Modules/ConfigurationModule.cs` | Backplane, control node, leader election, liveness listener, and the runtime and protocol modules removed. |
| Routes | `RouteRegistrar`, `RouteHelpers`, `OperationScope`, `OperationScopeMap`, `RouteAuthorizationEnum`, `OpenApiSchemaFactory`, `RequestBodyException`, `ConfigurationSupport`, `ExternalUrls`, and the `Core`, `Tenant`, `User`, `Credential`, `Role`, `Permission`, `Assignment`, `Audit`, `RequestHistory`, `Settings`, `ServerInfo`, `Observability`, `Secret`, and `PromptTemplate` registrars | Garrison's REST paths and JSON shapes are kept for the identity and administration routes so the forked dashboard keeps working. |
| Services | `AuthCache`, `AuthCacheEntry`, `AuthenticationService`, `AuthorizationService`, `AuthRateLimiter`, `CachedGrants`, `ClusterSettingDefinition`, `ClusterSettingsService`, `FileSettingDescriptor`, `IdentityService`, `IRequestObserver`, `ObservabilityService`, `PrometheusBuildInfo`, `BuildInfoVersion`, `RequestHistoryCaptureService`, `RequestTelemetry`, `RestartCoordinator`, `RetentionService`, `SettingsService` | Run principals and the backplane removed; see Hugo changes. |
| Settings | `AuthSettings`, `LoggingSettings`, `RequestHistorySettings`, `RestSettings`, and `ServerSettings` (now `HugoSettings`) | Hugo adds `SecretsSettings`, `AgentSettings`, `ApprovalsSettings`, `ConnectorsSettings`, `ModelsSettings`, `McpSettings`, `A2ASettings`, and `OpenAISettings`. |

### `src/Test.Shared`

`Infrastructure/Assert.cs`, `HttpTestHelpers.cs`, `TestResponse.cs`, `TestAssertionException.cs`, `TestEnvironment.cs`, `TestServerHost.cs`, and `Suites/IdentitySuite.cs`, `IdentityHttpCases.cs`, `IdentityUnitCases.cs`, `TemplateSuite.cs`. The database contract suite was rewritten for Hugo's entities.

## Foundation helper verification (P0.16)

The recorded fork commit above was compared with Hugo again on 2026-10-15:

- `Templates/TemplateRenderer.cs` retains Garrison's implementation in the Hugo namespace. The broader
  template engine keeps Hugo's documented variable roots and removes the Codex-specific filter. All
  eleven original `TemplateSuite` cases are present; the analyzer case uses Hugo profile/binding roots.
- `Telemetry/LabelGuard.cs` matches Garrison after the namespace rename. Its original
  `TelemetryCoverageSuite.LabelGuardAsync` case is ported into `helper-ports`, with additional reset,
  empty-value, remembered-count, and throwing-callback checks.
- `StrictEnumConverterFactory` was named in the original P0.16 scope but is absent from Garrison's recorded fork tree and
  current tree. There is no original helper or test to port from those sources. Hugo now supplies its
  own `Serialization/StrictEnumConverterFactory.cs` and `StrictEnumConverter.cs`, with explicit
  acceptance coverage in `helper-ports`, tracked separately as P0.17.

The strict factory is applied to Hugo-owned API enum types in `Serializer.Options` and its readable
variant. It accepts declared names without case sensitivity, writes canonical names, and rejects
numeric tokens, numeric strings, unknown names, and undefined output values. Nullable enums retain
normal JSON null handling. File settings keep their existing converter, including historical numeric
enum values, and third-party enum types retain their previous serializer behavior. Explicit
property-level converters retain their own contracts. Compatibility coverage loads the shipped
`src/Hugo.Server/hugo.json`, checks its numeric logging severity, and round-trips file settings.

## What was removed

Everything that models Garrison's agent-harness runtime: runs, run events, nodes, node pools, workers, executors, harness profiles, deployments, pipelines and reviews, manifests, artifacts and artifact stores, the Codex credential refresh, approvals as Garrison models them, conversations, the MCP server registry and MCP sessions, coordination leases, leader election, the event backplane, control nodes, usage quotas, model prices, workspace templates, the A2A routes, the Garrison MCP server and cluster proxy, WebSockets, and the Garrison seed prompts and harness profiles. The approval, A2A, and MCP pieces return in later Hugo phases, built for Hugo's domain.

## Hugo changes on top of the fork

- **Schema.** Columns are snake_case (plan section 7.1). Tables were renamed (`authsessions` to `auth_sessions`, `userroles` to `roles`, and so on). New columns: tenant `parent_tenant_id`, `region`, `default_agent_profile_id`, `lockdown`; user `must_change_password`, `default_agent_profile_id`, `preferred_locale`; credential `owner_user_id`, `agent_profile_id`, `lock_to_profile`; session `reauthenticated_utc`; request history `trace_id`, `correlation_id`. New tables: `secrets`, `secret_values`, `master_key_versions`.
- **Secrets vault (plan section 12).** `SecretCipher` was rewritten: AES-256-GCM with HKDF-SHA256 subkeys per purpose, associated data `tenantId|secretId|fieldName`, a frame with a format byte and a key version byte, and a previous key for rotation. New: master key loading and the canary check, multi-field secrets, the reveal flow with re-authentication, import parsers, the secret scrubber, and the rotation job.
- **Identity.** First-boot seed of the `default` tenant, `admin@hugo.local`, and Thomas Anderson (`anderson@hugo.local`), both with must-change-password. Password change routes and enforcement. Separate session lifetimes for users and administrators, and an absolute refresh limit. Tenant lookup and re-authentication routes. Session routes. Transactional user and credential cascades. The `x-tenant-guid` header as an alias of `x-tenant-id`. Conflicting authentication schemes are rejected. The administrator API key is disabled unless configured.
- **Authorization.** Hugo's resource types and the Reveal operation. Built-in roles per plan section 15.4. Routes registered outside the registrar fail closed to administrators, and a self-check lists them.
- **Server.** An in-process event hub and `GET /v1.0/api/events`. `x-request-id` on every response. Restart exits with code 75. Settings from `hugo.json` with `HUGO_*` overrides that reject invalid values.
