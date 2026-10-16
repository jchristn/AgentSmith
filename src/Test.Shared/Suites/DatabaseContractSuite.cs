namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Database;
    using Hugo.Core.Database.Ado;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;
    using Hugo.Core.Responses;
    using Hugo.Core.Security;
    using Hugo.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;

    /// <summary>
    /// Database contract tests for every Phase 1 and Phase 2 entity (P1.11 to P1.15, P1.42, P2.3, P2.4), run against
    /// every selected provider.
    /// </summary>
    public static class DatabaseContractSuite
    {
        /// <summary>
        /// Build the suite for one provider.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build(DatabaseTypeEnum provider)
        {
            string suiteId = "db-" + provider.ToString().ToLowerInvariant();
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(new TestCaseDescriptor(suiteId, "migrate-twice", "Migrations are idempotent and reach the latest version (" + provider + ")", ct => MigrateTwiceAsync(provider, ct)));
            cases.Add(new TestCaseDescriptor(suiteId, "snake-case-columns", "Tables use snake_case columns and the catalog lists every table (" + provider + ")", ct => SnakeCaseAsync(provider, ct)));
            cases.Add(new TestCaseDescriptor(suiteId, "tenant-crud", "Tenant create, read, update, enumerate, delete (" + provider + ")", ct => TenantCrudAsync(provider, ct)));
            cases.Add(new TestCaseDescriptor(suiteId, "user-crud", "User fields round-trip, email is unique per tenant, reads are tenant scoped (" + provider + ")", ct => UserCrudAsync(provider, ct)));
            cases.Add(new TestCaseDescriptor(suiteId, "credential-crud", "Credential fields round-trip, access key lookup, owner listing (" + provider + ")", ct => CredentialCrudAsync(provider, ct)));
            cases.Add(new TestCaseDescriptor(suiteId, "session-lifecycle", "Session create, token lookup, re-authentication time, revoke, prune (" + provider + ")", ct => SessionLifecycleAsync(provider, ct)));
            cases.Add(new TestCaseDescriptor(suiteId, "rbac-entities", "Roles, permissions with normalized type tables, maps, and assignments round-trip (" + provider + ")", ct => RbacEntitiesAsync(provider, ct)));
            cases.Add(new TestCaseDescriptor(suiteId, "audit-records", "Audit records: create, filter, page, bulk delete, prune (" + provider + ")", ct => AuditAsync(provider, ct)));
            cases.Add(new TestCaseDescriptor(suiteId, "request-history", "Request history with headers: create, read, filter, summary, delete (" + provider + ")", ct => RequestHistoryAsync(provider, ct)));
            cases.Add(new TestCaseDescriptor(suiteId, "nonces-and-rate-limits", "Nonces reject replays; rate limit buckets count and prune (" + provider + ")", ct => NoncesAsync(provider, ct)));
            cases.Add(new TestCaseDescriptor(suiteId, "cluster-settings", "Runtime settings upsert and list (" + provider + ")", ct => ClusterSettingsAsync(provider, ct)));
            cases.Add(new TestCaseDescriptor(suiteId, "prompt-templates", "Prompt templates: built-in layer visible to every tenant, tenant layer wins by name, versions round-trip (" + provider + ")", ct => PromptTemplatesAsync(provider, ct)));
            cases.Add(new TestCaseDescriptor(suiteId, "secrets", "Secrets and secret values: create, read, replace values, enumerate field names, rotation queries, delete (" + provider + ")", ct => SecretsAsync(provider, ct)));
            cases.Add(new TestCaseDescriptor(suiteId, "master-key-versions", "Master key versions upsert and current flag (" + provider + ")", ct => MasterKeysAsync(provider, ct)));
            cases.Add(new TestCaseDescriptor(suiteId, "cascades", "User, credential, and tenant cascades delete dependents in one transaction (" + provider + ")", ct => CascadesAsync(provider, ct)));
            cases.Add(new TestCaseDescriptor(suiteId, "seed-idempotent", "Seeding twice changes nothing and built-in edits are reverted (" + provider + ")", ct => SeedAsync(provider, ct)));
            return new TestSuiteDescriptor(suiteId, "Database contract (" + provider + ")", cases);
        }

        private static async Task MigrateTwiceAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            DatabaseSettings settings = TestEnvironment.IsolatedSettings(provider);
            int firstVersion = 0;
            using (DatabaseDriverBase first = await DatabaseDriverFactory.CreateAndInitializeAsync(settings, token).ConfigureAwait(false))
            {
                Assert.True(await first.PingAsync(token).ConfigureAwait(false), "Ping failed after first initialization.");
                firstVersion = first.SchemaVersion;
                Assert.Equal(SchemaBuilder.LatestVersion, firstVersion, "Schema version after first initialization");
                Assert.Equal(SchemaBuilder.MigrationCount, first.MigrationsApplied, "Migrations applied on a new database");
            }
            using (DatabaseDriverBase second = await DatabaseDriverFactory.CreateAndInitializeAsync(settings, token).ConfigureAwait(false))
            {
                Assert.Equal(firstVersion, second.SchemaVersion, "Schema version after second initialization");
                Assert.Equal(0, second.MigrationsApplied, "Migrations applied on the second initialization");
                Assert.Equal(firstVersion, await second.ReadSchemaVersionAsync(token).ConfigureAwait(false), "Stored schema version");
            }
        }

        private static async Task SnakeCaseAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (DatabaseDriverBase db = await TestEnvironment.CreateDriverAsync(provider, token).ConfigureAwait(false))
            {
                AdoDatabaseDriver ado = (AdoDatabaseDriver)db;
                foreach (string table in TableCatalog.AllTables)
                {
                    List<string> columns = await ado.QueryAsync("SELECT * FROM " + ado.Table(table) + " WHERE 1 = 0;", null, reader =>
                    {
                        List<string> names = new List<string>();
                        for (int i = 0; i < reader.FieldCount; i++) names.Add(reader.GetName(i));
                        return String.Join(",", names);
                    }, token).ConfigureAwait(false);
                    Assert.Equal(0, columns.Count, "Rows in an empty WHERE 1 = 0 query on " + table);
                }
                foreach (string table in TableCatalog.AllTables)
                {
                    Assert.True(table == table.ToLowerInvariant() && !table.Contains('-'), "Table " + table + " is not snake_case.");
                }
                // Spot check a few snake_case columns by selecting them explicitly.
                await ado.ScalarAsync("SELECT COUNT(*) FROM " + ado.Table("users") + " WHERE must_change_password = " + ado.Dialect.BooleanLiteral(true) + " AND tenant_id IS NOT NULL AND last_update_utc IS NOT NULL;", null, token).ConfigureAwait(false);
                await ado.ScalarAsync("SELECT COUNT(*) FROM " + ado.Table("secret_values") + " WHERE field_name IS NOT NULL AND key_version > 0;", null, token).ConfigureAwait(false);
                await ado.ScalarAsync("SELECT COUNT(*) FROM " + ado.Table("auth_sessions") + " WHERE reauthenticated_utc IS NULL;", null, token).ConfigureAwait(false);
            }
        }

        private static async Task TenantCrudAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (DatabaseDriverBase db = await TestEnvironment.CreateDriverAsync(provider, token).ConfigureAwait(false))
            {
                Tenant tenant = new Tenant();
                tenant.Name = "Acme Platform";
                tenant.Description = "Contract test tenant";
                tenant.Region = "us-east";
                tenant.Lockdown = true;
                await db.Tenants.CreateAsync(tenant, token).ConfigureAwait(false);
                Assert.True(tenant.Id.StartsWith("ten_", StringComparison.Ordinal), "Tenant identifier prefix.");

                Tenant read = Assert.NotNull(await db.Tenants.ReadAsync(tenant.Id, token).ConfigureAwait(false), "Tenant");
                Assert.Equal("Acme Platform", read.Name, "Name");
                Assert.Equal("us-east", read.Region, "Region");
                Assert.True(read.Lockdown, "Lockdown round-trips.");
                Assert.True(read.Active, "Tenant should be active by default.");
                Assert.Equal(DateTimeKind.Utc, read.CreatedUtc.Kind, "CreatedUtc kind");

                read.Name = "Acme Renamed";
                read.Lockdown = false;
                await db.Tenants.UpdateAsync(read, token).ConfigureAwait(false);
                Tenant byName = Assert.NotNull(await db.Tenants.ReadByNameAsync("Acme Renamed", token).ConfigureAwait(false), "Tenant by name");
                Assert.Equal(tenant.Id, byName.Id, "Id");
                Assert.True(!byName.Lockdown, "Lockdown cleared.");

                EnumerationQuery query = new EnumerationQuery();
                query.Search = "renamed";
                EnumerationResult<Tenant> page = await db.Tenants.EnumerateAsync(query, token).ConfigureAwait(false);
                Assert.Equal(1L, page.TotalRecords, "Search total");

                Assert.True(await db.Tenants.DeleteAsync(tenant.Id, token).ConfigureAwait(false), "Delete returned false.");
                Assert.True(!await db.Tenants.ExistsAsync(tenant.Id, token).ConfigureAwait(false), "Tenant still exists after delete.");
            }
        }

        private static async Task UserCrudAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (DatabaseDriverBase db = await TestEnvironment.CreateDriverAsync(provider, token).ConfigureAwait(false))
            {
                User user = new User();
                user.TenantId = "ten_a";
                user.Email = "neo@hugo.local";
                user.FirstName = "Thomas";
                user.LastName = "Anderson";
                user.PasswordHash = PasswordHasher.Hash("password", 10000);
                user.IsTenantAdmin = true;
                user.MustChangePassword = true;
                user.PreferredLocale = "en-US";
                await db.Users.CreateAsync(user, token).ConfigureAwait(false);

                User read = Assert.NotNull(await db.Users.ReadByEmailAsync("ten_a", "neo@hugo.local", token).ConfigureAwait(false), "User by email");
                Assert.True(read.MustChangePassword, "MustChangePassword round-trips.");
                Assert.Equal("en-US", read.PreferredLocale, "Locale");
                Assert.True(read.IsTenantAdmin && !read.IsAdmin, "Flags");
                Assert.True(PasswordHasher.Verify("password", read.PasswordHash), "Password hash round-trips.");
                Assert.True(await db.Users.ReadAsync("ten_b", user.Id, token).ConfigureAwait(false) == null, "User visible in another tenant.");
                Assert.True(await db.Users.ReadByEmailAsync("ten_b", "neo@hugo.local", token).ConfigureAwait(false) == null, "Email visible in another tenant.");

                User duplicate = new User();
                duplicate.TenantId = "ten_a";
                duplicate.Email = "neo@hugo.local";
                duplicate.PasswordHash = "x";
                bool rejected = false;
                try
                {
                    await db.Users.CreateAsync(duplicate, token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is System.Data.Common.DbException)
                {
                    rejected = true;
                }
                Assert.True(rejected, "Duplicate email in one tenant was accepted.");

                User other = new User();
                other.TenantId = "ten_b";
                other.Email = "neo@hugo.local";
                other.PasswordHash = "x";
                await db.Users.CreateAsync(other, token).ConfigureAwait(false);

                read.MustChangePassword = false;
                await db.Users.UpdateAsync(read, token).ConfigureAwait(false);
                User updated = Assert.NotNull(await db.Users.ReadAsync("ten_a", user.Id, token).ConfigureAwait(false), "Updated user");
                Assert.True(!updated.MustChangePassword, "MustChangePassword cleared.");
                Assert.Equal(1L, (await db.Users.EnumerateAsync("ten_a", new EnumerationQuery(), token).ConfigureAwait(false)).TotalRecords, "Users in tenant A");
            }
        }

        private static async Task CredentialCrudAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (DatabaseDriverBase db = await TestEnvironment.CreateDriverAsync(provider, token).ConfigureAwait(false))
            {
                Credential credential = new Credential();
                credential.TenantId = "ten_a";
                credential.UserId = "usr_owner";
                credential.Name = "ci";
                credential.AccessKey = SecretGenerator.NewAccessKey();
                credential.SecretKeyEncrypted = "ciphertext";
                credential.SecretKeyLast4 = "abcd";
                credential.AuthMode = CredentialAuthModeEnum.SignedRequest;
                credential.AgentProfileId = "agp_x";
                credential.LockToProfile = true;
                credential.ExpiresUtc = DateTime.UtcNow.AddDays(1);
                await db.Credentials.CreateAsync(credential, token).ConfigureAwait(false);

                Credential read = Assert.NotNull(await db.Credentials.ReadByAccessKeyAsync(credential.AccessKey, token).ConfigureAwait(false), "By access key");
                Assert.Equal("usr_owner", read.UserId, "Owner");
                Assert.Equal(CredentialAuthModeEnum.SignedRequest, read.AuthMode, "Auth mode");
                Assert.Equal("agp_x", read.AgentProfileId, "Agent profile");
                Assert.True(read.LockToProfile, "Lock to profile");
                Assert.True(read.ExpiresUtc.HasValue, "Expiry");
                Assert.Equal(1, (await db.Credentials.ListByUserAsync("ten_a", "usr_owner", token).ConfigureAwait(false)).Count, "Owner listing");
                Assert.True(await db.Credentials.ReadAsync("ten_b", credential.Id, token).ConfigureAwait(false) == null, "Credential visible in another tenant.");
                await db.Credentials.TouchLastUsedAsync(credential.Id, token).ConfigureAwait(false);
                Credential touched = Assert.NotNull(await db.Credentials.ReadAsync(credential.Id, token).ConfigureAwait(false), "Touched");
                Assert.True(touched.LastUsedUtc.HasValue, "Last used recorded.");
            }
        }

        private static async Task SessionLifecycleAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (DatabaseDriverBase db = await TestEnvironment.CreateDriverAsync(provider, token).ConfigureAwait(false))
            {
                AuthSession session = new AuthSession();
                session.TenantId = "ten_a";
                session.UserId = "usr_a";
                session.PrincipalType = PrincipalTypeEnum.User;
                session.AuthScheme = AuthSchemeEnum.PasswordHeaders;
                session.TokenId = SecretGenerator.Alphanumeric(40);
                session.ExpiresUtc = DateTime.UtcNow.AddHours(1);
                await db.Sessions.CreateAsync(session, token).ConfigureAwait(false);

                AuthSession read = Assert.NotNull(await db.Sessions.ReadByTokenIdAsync(session.TokenId, token).ConfigureAwait(false), "Session by token");
                Assert.True(!read.ReauthenticatedUtc.HasValue, "No re-authentication yet.");
                read.ReauthenticatedUtc = DateTime.UtcNow;
                await db.Sessions.UpdateAsync(read, token).ConfigureAwait(false);
                AuthSession reauth = Assert.NotNull(await db.Sessions.ReadAsync("ten_a", session.Id, token).ConfigureAwait(false), "Session");
                Assert.True(reauth.ReauthenticatedUtc.HasValue, "Re-authentication time round-trips.");

                Assert.True(await db.Sessions.RevokeAsync(session.Id, "test", token).ConfigureAwait(false), "Revoke");
                Assert.True(!await db.Sessions.RevokeAsync(session.Id, "again", token).ConfigureAwait(false), "Second revoke is a no-op.");
                AuthSession revoked = Assert.NotNull(await db.Sessions.ReadAsync(session.Id, token).ConfigureAwait(false), "Revoked session");
                Assert.True(revoked.RevokedUtc.HasValue && !revoked.Active, "Revoked flags.");

                AuthSession expired = new AuthSession();
                expired.TenantId = "ten_a";
                expired.TokenId = SecretGenerator.Alphanumeric(40);
                expired.ExpiresUtc = DateTime.UtcNow.AddDays(-3);
                await db.Sessions.CreateAsync(expired, token).ConfigureAwait(false);
                Assert.True(await db.Sessions.PruneExpiredAsync(DateTime.UtcNow.AddDays(-1), token).ConfigureAwait(false) >= 1, "Expired session pruned.");
                Assert.True(await db.Sessions.ReadAsync(expired.Id, token).ConfigureAwait(false) == null, "Expired session remains.");
            }
        }

        private static async Task RbacEntitiesAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (DatabaseDriverBase db = await TestEnvironment.CreateDriverAsync(provider, token).ConfigureAwait(false))
            {
                Role role = new Role();
                role.TenantId = "ten_a";
                role.Name = "Responder";
                await db.Roles.CreateAsync(role, token).ConfigureAwait(false);
                Assert.Equal(role.Id, Assert.NotNull(await db.Roles.ReadByNameAsync("ten_a", "Responder", token).ConfigureAwait(false), "Role by name").Id, "Role id");
                Assert.True(await db.Roles.ReadAsync("ten_b", role.Id, token).ConfigureAwait(false) == null, "Role visible in another tenant.");

                Permission permission = new Permission();
                permission.TenantId = "ten_a";
                permission.Name = "Responder.Secrets";
                permission.ResourceTypes = new List<string> { "Secret", "Resource" };
                permission.OperationTypes = new List<string> { "Read", "Reveal" };
                permission.PermissionType = PermissionTypeEnum.Deny;
                await db.Permissions.CreateAsync(permission, token).ConfigureAwait(false);
                Permission readPermission = Assert.NotNull(await db.Permissions.ReadAsync("ten_a", permission.Id, token).ConfigureAwait(false), "Permission");
                Assert.Equal(PermissionTypeEnum.Deny, readPermission.PermissionType, "Permission type");
                Assert.True(readPermission.ResourceTypes.Contains("Secret") && readPermission.ResourceTypes.Contains("Resource"), "Resource types round-trip through the child table.");
                Assert.True(readPermission.OperationTypes.Contains("Reveal"), "Operation types round-trip through the child table.");
                readPermission.OperationTypes = new List<string> { "Read" };
                await db.Permissions.UpdateAsync(readPermission, token).ConfigureAwait(false);
                Assert.Equal(1, Assert.NotNull(await db.Permissions.ReadAsync(permission.Id, token).ConfigureAwait(false), "Updated permission").OperationTypes.Count, "Operation types replaced");

                RolePermissionMap map = new RolePermissionMap();
                map.TenantId = "ten_a";
                map.RoleId = role.Id;
                map.PermissionId = permission.Id;
                await db.RolePermissionMaps.CreateAsync(map, token).ConfigureAwait(false);
                Assert.Equal(1, (await db.RolePermissionMaps.ListByRoleAsync(role.Id, token).ConfigureAwait(false)).Count, "Maps by role");

                UserRoleAssignment assignment = new UserRoleAssignment();
                assignment.TenantId = "ten_a";
                assignment.UserId = "usr_a";
                assignment.RoleId = role.Id;
                assignment.RoleName = role.Name;
                assignment.ResourceScope = ResourceScopeEnum.Resource;
                assignment.ResourceId = "sec_1";
                assignment.InheritsToChildren = false;
                await db.UserRoleAssignments.CreateAsync(assignment, token).ConfigureAwait(false);
                UserRoleAssignment readAssignment = (await db.UserRoleAssignments.ListByUserAsync("ten_a", "usr_a", token).ConfigureAwait(false)).Single();
                Assert.Equal(ResourceScopeEnum.Resource, readAssignment.ResourceScope, "Assignment scope");
                Assert.Equal("sec_1", readAssignment.ResourceId, "Assignment resource");
                Assert.True(!readAssignment.InheritsToChildren, "Inherits flag");
                Assert.Equal(0, (await db.UserRoleAssignments.ListByUserAsync("ten_b", "usr_a", token).ConfigureAwait(false)).Count, "Assignments in another tenant");

                CredentialScopeAssignment scope = new CredentialScopeAssignment();
                scope.TenantId = "ten_a";
                scope.CredentialId = "crd_a";
                scope.RoleName = "Viewer";
                await db.CredentialScopeAssignments.CreateAsync(scope, token).ConfigureAwait(false);
                Assert.Equal("Viewer", (await db.CredentialScopeAssignments.ListByCredentialAsync("ten_a", "crd_a", token).ConfigureAwait(false)).Single().RoleName, "Credential scope");
            }
        }

        private static async Task AuditAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (DatabaseDriverBase db = await TestEnvironment.CreateDriverAsync(provider, token).ConfigureAwait(false))
            {
                for (int i = 0; i < 3; i++)
                {
                    AuditRecord record = new AuditRecord();
                    record.TenantId = "ten_a";
                    record.EventType = i == 0 ? AuditEventTypeEnum.SecretRevealed : AuditEventTypeEnum.AuthorizationDenied;
                    record.PrincipalId = "usr_a";
                    record.AuthorizationResult = AuthorizationResultEnum.DeniedImplicit;
                    record.Description = "event " + i;
                    record.StatusCode = 403;
                    await db.Audit.CreateAsync(record, token).ConfigureAwait(false);
                }
                AuditRecord other = new AuditRecord();
                other.TenantId = "ten_b";
                other.EventType = AuditEventTypeEnum.AuthorizationDenied;
                await db.Audit.CreateAsync(other, token).ConfigureAwait(false);

                AuditFilter filter = new AuditFilter();
                filter.TenantId = "ten_a";
                Assert.Equal(3L, (await db.Audit.EnumerateAsync(filter, token).ConfigureAwait(false)).TotalCount, "Tenant A audit");
                filter.PageSize = 1;
                filter.PageNumber = 99;
                AuditSummary summary = await db.Audit.SummarizeAsync(filter, token).ConfigureAwait(false);
                Assert.Equal(3L, summary.TotalCount, "Summary ignores paging and excludes other tenants");
                Assert.Equal(2L, summary.EventCounts["AuthorizationDenied"], "Denied event count");
                Assert.Equal(1L, summary.EventCounts["SecretRevealed"], "Reveal event count");
                filter.PrincipalId = "missing";
                summary = await db.Audit.SummarizeAsync(filter, token).ConfigureAwait(false);
                Assert.True(summary.TotalCount == 0 && summary.EventCounts.Count == 0, "Empty summary");
                filter.PrincipalId = "usr_a";
                filter.Result = AuthorizationResultEnum.Permitted;
                Assert.Equal(0L, (await db.Audit.SummarizeAsync(filter, token).ConfigureAwait(false)).TotalCount, "Result filter");
                filter.Result = AuthorizationResultEnum.DeniedImplicit;
                filter.ToUtc = DateTime.UtcNow.AddMinutes(-1);
                Assert.Equal(0L, (await db.Audit.SummarizeAsync(filter, token).ConfigureAwait(false)).TotalCount, "Upper time bound");
                filter.ToUtc = null;
                filter.FromUtc = DateTime.UtcNow.AddMinutes(1);
                Assert.Equal(0L, (await db.Audit.SummarizeAsync(filter, token).ConfigureAwait(false)).TotalCount, "Lower time bound");
                filter.FromUtc = DateTime.UtcNow.AddMinutes(-1);
                filter.EventType = AuditEventTypeEnum.SecretRevealed;
                Assert.Equal(1L, (await db.Audit.SummarizeAsync(filter, token).ConfigureAwait(false)).TotalCount, "Combined summary filters");
                filter.PageNumber = 1;
                filter.EventType = AuditEventTypeEnum.SecretRevealed;
                AuditPage revealed = await db.Audit.EnumerateAsync(filter, token).ConfigureAwait(false);
                Assert.Equal(1L, revealed.TotalCount, "Reveal events");
                Assert.True(await db.Audit.ReadAsync("ten_b", revealed.Objects[0].Id, token).ConfigureAwait(false) == null, "Audit record visible in another tenant.");

                AuditFilter deny = new AuditFilter();
                deny.TenantId = "ten_a";
                deny.EventType = AuditEventTypeEnum.AuthorizationDenied;
                Assert.Equal(2, await db.Audit.DeleteManyAsync(deny, token).ConfigureAwait(false), "Bulk delete count");
                Assert.True(await db.Audit.PruneAsync(DateTime.UtcNow.AddMinutes(1), token).ConfigureAwait(false) >= 2, "Prune");
            }
        }

        private static async Task RequestHistoryAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (DatabaseDriverBase db = await TestEnvironment.CreateDriverAsync(provider, token).ConfigureAwait(false))
            {
                RequestHistoryEntry entry = new RequestHistoryEntry();
                entry.TenantId = "ten_a";
                entry.Method = "GET";
                entry.Path = "/v1.0/api/health";
                entry.Url = "/v1.0/api/health?x=1";
                entry.StatusCode = 200;
                entry.DurationMs = 1.5;
                entry.TraceId = "0123456789abcdef0123456789abcdef";
                entry.CorrelationId = "corr-1";
                entry.RequestBody = "{}";
                entry.RequestBodyBytes = 2;
                RequestHistoryHeader header = new RequestHistoryHeader();
                header.Direction = HeaderDirectionEnum.Request;
                header.Name = "accept";
                header.Value = "application/json";
                entry.Headers.Add(header);
                entry.Headers.Add(new RequestHistoryHeader
                {
                    Direction = HeaderDirectionEnum.Response,
                    Name = "x-test",
                    Value = "quote ' and semicolon ; remain data"
                });
                await db.RequestHistory.CreateAsync(entry, token).ConfigureAwait(false);

                RequestHistoryEntry read = Assert.NotNull(await db.RequestHistory.ReadAsync("ten_a", entry.Id, token).ConfigureAwait(false), "Entry");
                Assert.Equal(2, read.Headers.Count, "Both header directions round-trip through the child table");
                Assert.Equal("application/json", read.Headers.Single(h => h.Direction == HeaderDirectionEnum.Request).Value, "Request header value");
                Assert.Equal("quote ' and semicolon ; remain data", read.Headers.Single(h => h.Direction == HeaderDirectionEnum.Response).Value, "Parameterized response header value");
                Assert.True(read.Headers.All(h => h.RequestHistoryEntryId == entry.Id), "Headers reference their parent entry.");
                read.Headers.RemoveAll(h => h.Direction == HeaderDirectionEnum.Response);
                read.Headers[0].Value = "text/plain";
                await db.RequestHistory.UpdateAsync(read, token).ConfigureAwait(false);
                read = Assert.NotNull(await db.RequestHistory.ReadAsync("ten_a", entry.Id, token).ConfigureAwait(false), "Updated entry");
                Assert.Equal(1, read.Headers.Count, "Header replacement removes deleted child rows");
                Assert.Equal("text/plain", read.Headers[0].Value, "Updated header value");
                Assert.Equal("0123456789abcdef0123456789abcdef", read.TraceId, "Trace id");
                Assert.Equal("corr-1", read.CorrelationId, "Correlation id");
                Assert.True(await db.RequestHistory.ReadAsync("ten_b", entry.Id, token).ConfigureAwait(false) == null, "Entry visible in another tenant.");

                RequestHistoryFilter filter = new RequestHistoryFilter();
                filter.TenantId = "ten_a";
                Assert.Equal(1L, (await db.RequestHistory.EnumerateAsync(filter, token).ConfigureAwait(false)).TotalCount, "Filtered count");
                filter.FromUtc = DateTime.UtcNow.AddHours(-1);
                filter.ToUtc = DateTime.UtcNow.AddMinutes(1);
                RequestHistorySummary summary = await db.RequestHistory.SummarizeAsync(filter, token).ConfigureAwait(false);
                Assert.Equal(1L, summary.TotalCount, "Summary count");
                Assert.True(summary.Buckets.Count > 1, "Summary is gap filled.");
                Assert.True(await db.RequestHistory.DeleteAsync(entry.Id, token).ConfigureAwait(false), "Delete");
                AdoDatabaseDriver ado = (AdoDatabaseDriver)db;
                object? orphanCount = await ado.ScalarAsync("SELECT COUNT(*) FROM " + ado.Table("request_history_headers") + " WHERE request_history_entry_id = @entry_id;",
                    command => AdoDatabaseDriver.Add(command, "entry_id", entry.Id), token).ConfigureAwait(false);
                Assert.Equal(0L, Convert.ToInt64(orphanCount), "Deleting history removes its header children");
            }
        }

        private static async Task NoncesAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (DatabaseDriverBase db = await TestEnvironment.CreateDriverAsync(provider, token).ConfigureAwait(false))
            {
                Assert.True(await db.Nonces.TryUseAsync("access_x:n1", DateTime.UtcNow.AddMinutes(5), token).ConfigureAwait(false), "First use");
                Assert.True(!await db.Nonces.TryUseAsync("access_x:n1", DateTime.UtcNow.AddMinutes(5), token).ConfigureAwait(false), "Replay accepted.");
                Assert.True(await db.Nonces.TryUseAsync("access_x:n2", DateTime.UtcNow.AddSeconds(-1), token).ConfigureAwait(false), "Second nonce");
                Assert.True(await db.Nonces.PruneExpiredAsync(DateTime.UtcNow, token).ConfigureAwait(false) >= 1, "Prune nonces");

                DateTime window = DateTime.UtcNow.AddMinutes(2);
                Assert.Equal(1, await db.RateLimits.IncrementAsync("bucket-a", window, token).ConfigureAwait(false), "First hit");
                Assert.Equal(2, await db.RateLimits.IncrementAsync("bucket-a", window, token).ConfigureAwait(false), "Second hit");
                await db.RateLimits.IncrementAsync("bucket-old", DateTime.UtcNow.AddMinutes(-1), token).ConfigureAwait(false);
                Assert.True(await db.RateLimits.PruneExpiredAsync(DateTime.UtcNow, token).ConfigureAwait(false) >= 1, "Prune buckets");
            }
        }

        private static async Task ClusterSettingsAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (DatabaseDriverBase db = await TestEnvironment.CreateDriverAsync(provider, token).ConfigureAwait(false))
            {
                await db.ClusterSettings.UpsertAsync("Audit.RetentionDays", "30", "test", token).ConfigureAwait(false);
                await db.ClusterSettings.UpsertAsync("Audit.RetentionDays", "60", "test", token).ConfigureAwait(false);
                ClusterSetting setting = Assert.NotNull(await db.ClusterSettings.ReadByKeyAsync("Audit.RetentionDays", token).ConfigureAwait(false), "Setting");
                Assert.Equal("60", setting.SettingValue, "Upserted value");
                Assert.Equal(1, (await db.ClusterSettings.ListAllAsync(token).ConfigureAwait(false)).Count, "Settings count");
            }
        }

        private static async Task PromptTemplatesAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (DatabaseDriverBase db = await TestEnvironment.CreateDriverAsync(provider, token).ConfigureAwait(false))
            {
                PromptTemplate builtIn = new PromptTemplate();
                builtIn.TenantId = null;
                builtIn.Name = "builtin/persona.default";
                builtIn.Content = "built-in";
                builtIn.IsBuiltIn = true;
                PromptTemplateVariable variable = new PromptTemplateVariable();
                variable.Name = "persona.name";
                variable.Required = true;
                builtIn.Variables.Add(variable);
                await db.PromptTemplates.CreateAsync(builtIn, token).ConfigureAwait(false);

                PromptTemplate visible = Assert.NotNull(await db.PromptTemplates.ReadAsync("ten_a", builtIn.Id, token).ConfigureAwait(false), "Built-in via tenant");
                Assert.Equal("built-in", visible.Content, "Content");
                Assert.Equal(1, visible.Variables.Count, "Variables round-trip");

                PromptTemplate own = new PromptTemplate();
                own.TenantId = "ten_a";
                own.Name = "builtin/persona.default";
                own.Content = "tenant";
                await db.PromptTemplates.CreateAsync(own, token).ConfigureAwait(false);

                PromptTemplate resolved = Assert.NotNull(await db.PromptTemplates.ReadByNameAsync("ten_a", "builtin/persona.default", token).ConfigureAwait(false), "Resolved");
                Assert.Equal("tenant", resolved.Content, "Tenant layer wins");
                PromptTemplate other = Assert.NotNull(await db.PromptTemplates.ReadByNameAsync("ten_b", "builtin/persona.default", token).ConfigureAwait(false), "Other tenant");
                Assert.Equal("built-in", other.Content, "Other tenant sees the built-in layer");
                Assert.True(await db.PromptTemplates.ReadAsync("ten_b", own.Id, token).ConfigureAwait(false) == null, "Tenant template visible in another tenant.");

                PromptTemplateVersion version = new PromptTemplateVersion();
                version.TenantId = "ten_a";
                version.TemplateId = own.Id;
                version.Version = 1;
                version.Content = "tenant";
                version.CreatedBy = "test";
                await db.PromptTemplateVersions.CreateAsync(version, token).ConfigureAwait(false);
                Assert.Equal("tenant", Assert.NotNull(await db.PromptTemplateVersions.ReadByVersionAsync(own.Id, 1, token).ConfigureAwait(false), "Version").Content, "Version content");
                Assert.Equal(1, await db.PromptTemplateVersions.DeleteByTemplateAsync(own.Id, token).ConfigureAwait(false), "Versions deleted");
            }
        }

        private static async Task SecretsAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (DatabaseDriverBase db = await TestEnvironment.CreateDriverAsync(provider, token).ConfigureAwait(false))
            {
                Secret secret = new Secret();
                secret.TenantId = "ten_a";
                secret.Name = "aws-prod";
                secret.Kind = SecretKindEnum.AccessKeyPair;
                secret.KeyVersion = 1;
                secret.Fingerprint = "****WXYZ";
                secret.Values.Add(Value("accessKeyId", "c1", 1));
                secret.Values.Add(Value("secretAccessKey", "c2", 1));
                await db.Secrets.CreateAsync(secret, token).ConfigureAwait(false);
                Assert.True(secret.Id.StartsWith("sec_", StringComparison.Ordinal), "Secret identifier prefix.");

                Secret read = Assert.NotNull(await db.Secrets.ReadAsync("ten_a", secret.Id, token).ConfigureAwait(false), "Secret");
                Assert.Equal(SecretKindEnum.AccessKeyPair, read.Kind, "Kind");
                Assert.Equal(2, read.Values.Count, "Values");
                Assert.Equal("accessKeyId,secretAccessKey", String.Join(",", read.Fields), "Fields");
                Assert.True(read.Values.All(v => v.TenantId == "ten_a" && v.SecretId == secret.Id), "Value ownership.");
                Assert.True(await db.Secrets.ReadAsync("ten_b", secret.Id, token).ConfigureAwait(false) == null, "Secret visible in another tenant.");
                Assert.True(await db.Secrets.ReadByNameAsync("ten_b", "aws-prod", token).ConfigureAwait(false) == null, "Secret name visible in another tenant.");

                EnumerationResult<Secret> page = await db.Secrets.EnumerateAsync("ten_a", new EnumerationQuery(), null, token).ConfigureAwait(false);
                Assert.Equal(1L, page.TotalRecords, "Enumerate");
                Assert.Equal(2, page.Objects[0].Fields.Count, "Field names on enumerate");
                Assert.Equal(0, page.Objects[0].Values.Count, "Ciphertexts are not loaded on enumerate");
                Assert.Equal(0L, (await db.Secrets.EnumerateAsync("ten_b", new EnumerationQuery(), null, token).ConfigureAwait(false)).TotalRecords, "Other tenant enumerate");

                read.Values = new List<SecretValue> { Value("accessKeyId", "c3", 2) };
                read.Description = "rotated";
                await db.Secrets.UpdateAsync(read, token).ConfigureAwait(false);
                Secret replaced = Assert.NotNull(await db.Secrets.ReadAsync(secret.Id, token).ConfigureAwait(false), "Replaced");
                Assert.Equal(1, replaced.Values.Count, "Values replaced");
                Assert.Equal("rotated", replaced.Description, "Description");

                Assert.Equal(1L, await db.Secrets.CountValuesAsync(token).ConfigureAwait(false), "Value count");
                Assert.Equal(1L, await db.Secrets.CountValuesNotAtVersionAsync(1, token).ConfigureAwait(false), "Values not at version 1");
                List<SecretValue> pending = await db.Secrets.ReadValuesNotAtVersionAsync(1, 10, token).ConfigureAwait(false);
                pending[0].Ciphertext = "c4";
                pending[0].KeyVersion = 1;
                Assert.Equal(1, await db.Secrets.UpdateCiphertextsAsync(pending, token).ConfigureAwait(false), "Ciphertexts updated");
                Assert.Equal(0L, await db.Secrets.CountValuesNotAtVersionAsync(1, token).ConfigureAwait(false), "Values not at version 1 after rotation");
                Assert.Equal(1, Assert.NotNull(await db.Secrets.ReadAsync(secret.Id, token).ConfigureAwait(false), "After rotation").KeyVersion, "Secret key version follows its values");

                Assert.True(await db.Secrets.DeleteAsync(secret.Id, token).ConfigureAwait(false), "Delete");
                Assert.Equal(0L, await db.Secrets.CountValuesAsync(token).ConfigureAwait(false), "Values deleted with the secret");
            }
        }

        private static async Task MasterKeysAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (DatabaseDriverBase db = await TestEnvironment.CreateDriverAsync(provider, token).ConfigureAwait(false))
            {
                MasterKeyVersion v1 = new MasterKeyVersion();
                v1.KeyVersion = 1;
                v1.Canary = "a";
                v1.IsCurrent = true;
                await db.MasterKeys.UpsertAsync(v1, token).ConfigureAwait(false);
                MasterKeyVersion v2 = new MasterKeyVersion();
                v2.KeyVersion = 2;
                v2.Canary = "b";
                await db.MasterKeys.UpsertAsync(v2, token).ConfigureAwait(false);
                await db.MasterKeys.SetCurrentAsync(2, token).ConfigureAwait(false);
                List<MasterKeyVersion> all = await db.MasterKeys.ReadAllAsync(token).ConfigureAwait(false);
                Assert.Equal(2, all.Count, "Versions");
                Assert.True(!all[0].IsCurrent && all[1].IsCurrent, "Current flag moved to version 2.");
                v1.Canary = "c";
                await db.MasterKeys.UpsertAsync(v1, token).ConfigureAwait(false);
                Assert.Equal("c", Assert.NotNull(await db.MasterKeys.ReadAsync(1, token).ConfigureAwait(false), "Version 1").Canary, "Upsert replaced the canary");
            }
        }

        private static async Task CascadesAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (DatabaseDriverBase db = await TestEnvironment.CreateDriverAsync(provider, token).ConfigureAwait(false))
            {
                Tenant tenant = new Tenant();
                tenant.Name = "cascade";
                await db.Tenants.CreateAsync(tenant, token).ConfigureAwait(false);
                User user = new User();
                user.TenantId = tenant.Id;
                user.Email = "cascade@hugo.local";
                user.PasswordHash = "x";
                await db.Users.CreateAsync(user, token).ConfigureAwait(false);
                Credential credential = new Credential();
                credential.TenantId = tenant.Id;
                credential.UserId = user.Id;
                credential.Name = "c";
                credential.AccessKey = SecretGenerator.NewAccessKey();
                credential.SecretKeyEncrypted = "x";
                await db.Credentials.CreateAsync(credential, token).ConfigureAwait(false);
                CredentialScopeAssignment scope = new CredentialScopeAssignment();
                scope.TenantId = tenant.Id;
                scope.CredentialId = credential.Id;
                scope.RoleName = "Viewer";
                await db.CredentialScopeAssignments.CreateAsync(scope, token).ConfigureAwait(false);
                UserRoleAssignment assignment = new UserRoleAssignment();
                assignment.TenantId = tenant.Id;
                assignment.UserId = user.Id;
                assignment.RoleName = "Viewer";
                await db.UserRoleAssignments.CreateAsync(assignment, token).ConfigureAwait(false);
                AuthSession session = new AuthSession();
                session.TenantId = tenant.Id;
                session.CredentialId = credential.Id;
                session.TokenId = SecretGenerator.Alphanumeric(40);
                session.ExpiresUtc = DateTime.UtcNow.AddHours(1);
                await db.Sessions.CreateAsync(session, token).ConfigureAwait(false);

                Assert.True(await db.Users.DeleteCascadeAsync(tenant.Id, user.Id, token).ConfigureAwait(false) >= 5, "User cascade row count");
                Assert.True(await db.Credentials.ReadAsync(credential.Id, token).ConfigureAwait(false) == null, "Credential remains after user delete.");
                Assert.True(await db.CredentialScopeAssignments.ReadAsync(scope.Id, token).ConfigureAwait(false) == null, "Scope remains after user delete.");
                Assert.True(await db.UserRoleAssignments.ReadAsync(assignment.Id, token).ConfigureAwait(false) == null, "Assignment remains after user delete.");
                Assert.True(await db.Sessions.ReadAsync(session.Id, token).ConfigureAwait(false) == null, "Session remains after user delete.");

                Secret secret = new Secret();
                secret.TenantId = tenant.Id;
                secret.Name = "s";
                secret.Values.Add(Value("token", "c", 1));
                await db.Secrets.CreateAsync(secret, token).ConfigureAwait(false);
                Assert.True(await db.Tenants.DeleteCascadeAsync(tenant.Id, token).ConfigureAwait(false) >= 3, "Tenant cascade row count");
                Assert.True(await db.Secrets.ReadAsync(secret.Id, token).ConfigureAwait(false) == null, "Secret remains after tenant delete.");
                Assert.Equal(0L, await db.Secrets.CountValuesAsync(token).ConfigureAwait(false), "Secret values remain after tenant delete");
                Assert.True(!await db.Tenants.ExistsAsync(tenant.Id, token).ConfigureAwait(false), "Tenant remains.");
            }
        }

        private static async Task SeedAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (DatabaseDriverBase db = await TestEnvironment.CreateDriverAsync(provider, token).ConfigureAwait(false))
            {
                SeedOptions options = new SeedOptions();
                options.PasswordHashIterations = 10000;
                DefaultSeeder seeder = new DefaultSeeder(db);
                Tenant tenant = await seeder.SeedAsync(options, token).ConfigureAwait(false);
                Assert.Equal("default", tenant.Name, "Default tenant name");
                User admin = Assert.NotNull(await db.Users.ReadByEmailAsync(tenant.Id, "admin@hugo.local", token).ConfigureAwait(false), "System administrator");
                Assert.True(admin.IsAdmin && admin.MustChangePassword, "Administrator flags.");
                User anderson = Assert.NotNull(await db.Users.ReadByEmailAsync(tenant.Id, "anderson@hugo.local", token).ConfigureAwait(false), "Thomas Anderson");
                Assert.True(anderson.IsTenantAdmin && !anderson.IsAdmin && anderson.MustChangePassword, "Anderson flags.");
                Assert.Equal("Thomas", anderson.FirstName, "First name");
                Assert.True(PasswordHasher.Verify("password", anderson.PasswordHash), "Seed password.");

                EnumerationQuery all = new EnumerationQuery();
                all.MaxResults = 1000;
                long roles = (await db.Roles.EnumerateAsync(null, all, token).ConfigureAwait(false)).TotalRecords;
                long permissions = (await db.Permissions.EnumerateAsync(null, all, token).ConfigureAwait(false)).TotalRecords;
                long users = (await db.Users.EnumerateAsync(null, all, token).ConfigureAwait(false)).TotalRecords;
                Assert.Equal((long)BuiltInRoles.All.Count, roles, "Built-in roles");
                foreach (string name in new[] { "TenantAdmin", "SecurityAdmin", "Auditor", "Operator", "Builder", "ResourceAdmin", "Editor", "Viewer", "ProfileUser", "TenantMember", "Custom" })
                {
                    Assert.True(BuiltInRoles.Find(name) != null, "Built-in role " + name + " missing.");
                }

                Role tampered = Assert.NotNull(await db.Roles.ReadAsync("rol_builtin_operator", token).ConfigureAwait(false), "Operator role");
                tampered.Description = "tampered";
                await db.Roles.UpdateAsync(tampered, token).ConfigureAwait(false);
                Permission grant = Assert.NotNull(await db.Permissions.ReadAsync("prm_builtin_operator_read", token).ConfigureAwait(false), "Operator read permission");
                grant.ResourceTypes = new List<string> { "All" };
                await db.Permissions.UpdateAsync(grant, token).ConfigureAwait(false);

                await seeder.SeedAsync(options, token).ConfigureAwait(false);
                Assert.Equal(roles, (await db.Roles.EnumerateAsync(null, all, token).ConfigureAwait(false)).TotalRecords, "Roles after second seed");
                Assert.Equal(permissions, (await db.Permissions.EnumerateAsync(null, all, token).ConfigureAwait(false)).TotalRecords, "Permissions after second seed");
                Assert.Equal(users, (await db.Users.EnumerateAsync(null, all, token).ConfigureAwait(false)).TotalRecords, "Users after second seed");
                Assert.True(Assert.NotNull(await db.Roles.ReadAsync("rol_builtin_operator", token).ConfigureAwait(false), "Operator role").Description != "tampered", "Built-in role edit reverted.");
                Assert.True(!Assert.NotNull(await db.Permissions.ReadAsync("prm_builtin_operator_read", token).ConfigureAwait(false), "Operator permission").ResourceTypes.Contains("All"), "Built-in permission edit reverted.");
                Permission reveal = Assert.NotNull(await db.Permissions.ReadAsync("prm_builtin_tenantadmin_reveal", token).ConfigureAwait(false), "TenantAdmin reveal permission");
                Assert.True(reveal.OperationTypes.Contains("Reveal") && reveal.ResourceTypes.Contains("Secret"), "TenantAdmin holds Reveal on Secret.");
            }
        }

        private static SecretValue Value(string field, string ciphertext, int keyVersion)
        {
            SecretValue value = new SecretValue();
            value.FieldName = field;
            value.Ciphertext = ciphertext;
            value.KeyVersion = keyVersion;
            return value;
        }
    }
}
