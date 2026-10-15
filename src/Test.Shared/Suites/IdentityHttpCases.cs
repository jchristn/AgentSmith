namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;
    using Hugo.Core.Responses;
    using Hugo.Core.Security;
    using Hugo.Server.Routes;
    using Hugo.Server.Services;
    using Test.Shared.Infrastructure;

    /// <summary>
    /// Identity cases that run against an in-process server.
    /// </summary>
    public static class IdentityHttpCases
    {
        #region Public-Methods

        /// <summary>
        /// Every scoped route declares a resource type and operation, and identity routes are registered.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public static async Task ScopeMapAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                List<OperationScope> scopes = host.Server.Context.Scopes.All();
                foreach (OperationScope scope in scopes.Where(s => s.Authorization == RouteAuthorizationEnum.Scoped))
                {
                    Assert.True(scope.ResourceType.HasValue && scope.Operation.HasValue, scope.Method + " " + scope.Path + " has no resource type or operation.");
                }
                foreach (string path in new[] { "/v1.0/api/tenants", "/v1.0/api/tenants/{tenantId}/users", "/v1.0/api/tenants/{tenantId}/credentials", "/v1.0/api/tenants/{tenantId}/roles", "/v1.0/api/request-history", "/v1.0/api/settings", "/v1.0/api/server-info", "/v1.0/api/tenants/{tenantId}/audit" })
                {
                    Assert.True(scopes.Any(s => s.Path == path), "Route " + path + " is not registered.");
                }

                ServerInfoResponse info = await HttpTestHelpers.SendAsync<ServerInfoResponse>(host.AdminClient, HttpMethod.Get, "/v1.0/api/server-info", null, 200, token).ConfigureAwait(false);
                Assert.Equal(scopes.Count, info.RouteCount, "Route count");
                Assert.True(info.McpUrl != null && info.McpUrl.EndsWith("/mcp", StringComparison.Ordinal), "MCP URL missing.");
            }
        }

        /// <summary>
        /// Signed requests succeed and replays are rejected.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public static async Task SignedRequestAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                CredentialCreateResponse created = await CreateCredentialAsync(host, host.TenantId, null, "TenantAdmin", token).ConfigureAwait(false);
                string date = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                string nonce = Guid.NewGuid().ToString("N");
                string canonical = RequestSigner.Canonicalize("GET", "/v1.0/token/details", null, host.TenantId, date, nonce, null);
                string signature = RequestSigner.Sign(created.SecretKey, canonical);

                using (HttpClient client = new HttpClient())
                {
                    client.BaseAddress = new Uri(host.BaseUrl);
                    client.DefaultRequestHeaders.Add("x-access-key", created.AccessKey);
                    client.DefaultRequestHeaders.Add("x-date", date);
                    client.DefaultRequestHeaders.Add("x-nonce", nonce);
                    client.DefaultRequestHeaders.Add("x-signature", signature);
                    TokenDetailsResponse details = await HttpTestHelpers.SendAsync<TokenDetailsResponse>(client, HttpMethod.Get, "/v1.0/token/details", null, 200, token).ConfigureAwait(false);
                    Assert.Equal(created.Credential.Id, details.CredentialId, "Signed principal");
                    Assert.Equal(AuthSchemeEnum.AccessKeySignature, details.AuthScheme ?? AuthSchemeEnum.BearerToken, "Scheme");

                    TestResponse replay = await HttpTestHelpers.SendAsync(client, HttpMethod.Get, "/v1.0/token/details", null, token).ConfigureAwait(false);
                    Assert.Equal(401, replay.StatusCode, "Replay status");
                }

                using (HttpClient bad = new HttpClient())
                {
                    bad.BaseAddress = new Uri(host.BaseUrl);
                    bad.DefaultRequestHeaders.Add("x-access-key", created.AccessKey);
                    bad.DefaultRequestHeaders.Add("x-date", date);
                    bad.DefaultRequestHeaders.Add("x-nonce", Guid.NewGuid().ToString("N"));
                    bad.DefaultRequestHeaders.Add("x-signature", new string('0', 64));
                    TestResponse rejected = await HttpTestHelpers.SendAsync(bad, HttpMethod.Get, "/v1.0/token/details", null, token).ConfigureAwait(false);
                    Assert.Equal(401, rejected.StatusCode, "Bad signature status");
                }
            }
        }

        /// <summary>
        /// The credential secret is returned once and never readable afterwards.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public static async Task CredentialSecretOnceAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                CredentialCreateResponse created = await CreateCredentialAsync(host, host.TenantId, null, "Operator", token).ConfigureAwait(false);
                Assert.True(created.SecretKey.StartsWith("secret_", StringComparison.Ordinal), "Secret format.");
                Assert.True(created.AccessKey.StartsWith("access_", StringComparison.Ordinal), "Access key format.");

                TestResponse read = await HttpTestHelpers.SendAsync(host.AdminClient, HttpMethod.Get, "/v1.0/api/tenants/" + host.TenantId + "/credentials/" + created.Credential.Id, null, token).ConfigureAwait(false);
                Assert.Equal(200, read.StatusCode, "Read status");
                Assert.True(!read.Body.Contains(created.SecretKey, StringComparison.Ordinal), "Secret returned on read.");
                Assert.True(!read.Body.Contains("secretKeyEncrypted", StringComparison.OrdinalIgnoreCase), "Encrypted secret field returned.");
                Assert.True(read.Body.Contains(SecretGenerator.Last4(created.SecretKey), StringComparison.Ordinal), "Last four characters missing.");

                Credential stored = Assert.NotNull(await host.Server.Context.Database.Credentials.ReadAsync(created.Credential.Id, token).ConfigureAwait(false), "Stored credential");
                Assert.True(!stored.SecretKeyEncrypted.Contains(created.SecretKey, StringComparison.Ordinal), "Secret stored in plain text.");

                using (HttpClient client = host.CredentialClient(created.AccessKey, created.SecretKey))
                {
                    Assert.Equal(200, (await HttpTestHelpers.SendAsync(client, HttpMethod.Get, "/v1.0/token/details", null, token).ConfigureAwait(false)).StatusCode, "Credential works");
                }

                CredentialCreateResponse rotated = await HttpTestHelpers.SendAsync<CredentialCreateResponse>(host.AdminClient, HttpMethod.Post, "/v1.0/api/tenants/" + host.TenantId + "/credentials/" + created.Credential.Id + "/rotate", null, 200, token).ConfigureAwait(false);
                using (HttpClient old = host.CredentialClient(created.AccessKey, created.SecretKey))
                {
                    Assert.Equal(401, (await HttpTestHelpers.SendAsync(old, HttpMethod.Get, "/v1.0/token/details", null, token).ConfigureAwait(false)).StatusCode, "Old secret after rotation");
                }
                using (HttpClient fresh = host.CredentialClient(rotated.AccessKey, rotated.SecretKey))
                {
                    Assert.Equal(200, (await HttpTestHelpers.SendAsync(fresh, HttpMethod.Get, "/v1.0/token/details", null, token).ConfigureAwait(false)).StatusCode, "New secret after rotation");
                }
            }
        }

        /// <summary>
        /// Credential grants are capped by the owning user's grants.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public static async Task OwnerCeilingAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                string usersPath = "/v1.0/api/tenants/" + host.TenantId + "/users";
                User member = await CreateUserAsync(host, host.TenantId, "member@example.com", null, token).ConfigureAwait(false);
                CredentialCreateResponse broad = await CreateCredentialAsync(host, host.TenantId, member.Id, "SecurityAdmin", token).ConfigureAwait(false);
                using (HttpClient client = host.CredentialClient(broad.AccessKey, broad.SecretKey))
                {
                    TestResponse capped = await HttpTestHelpers.SendAsync(client, HttpMethod.Get, usersPath, null, token).ConfigureAwait(false);
                    Assert.Equal(403, capped.StatusCode, "Owner ceiling should deny");
                    Assert.True(capped.Body.Contains("Owner ceiling", StringComparison.Ordinal), "Denial reason should name the owner ceiling: " + capped.Body);
                }

                User auditor = await CreateUserAsync(host, host.TenantId, "auditor@example.com", "Auditor", token).ConfigureAwait(false);
                CredentialCreateResponse auditorBroad = await CreateCredentialAsync(host, host.TenantId, auditor.Id, "SecurityAdmin", token).ConfigureAwait(false);
                CredentialCreateResponse auditorNarrow = await CreateCredentialAsync(host, host.TenantId, auditor.Id, "TenantMember", token).ConfigureAwait(false);
                using (HttpClient client = host.CredentialClient(auditorBroad.AccessKey, auditorBroad.SecretKey))
                {
                    Assert.Equal(200, (await HttpTestHelpers.SendAsync(client, HttpMethod.Get, usersPath, null, token).ConfigureAwait(false)).StatusCode, "Both credential and owner permit listing users");
                    Assert.Equal(403, (await HttpTestHelpers.SendAsync(client, HttpMethod.Post, usersPath, new UserRequest { Email = "x@example.com", Password = "password123" }, token).ConfigureAwait(false)).StatusCode, "Owner (Auditor) caps user creation");
                }
                using (HttpClient client = host.CredentialClient(auditorNarrow.AccessKey, auditorNarrow.SecretKey))
                {
                    Assert.Equal(403, (await HttpTestHelpers.SendAsync(client, HttpMethod.Get, usersPath, null, token).ConfigureAwait(false)).StatusCode, "Narrow credential is narrower than its owner");
                }
            }
        }

        /// <summary>
        /// A credential is authorized by its own scope assignments capped by its owner, never by the owner's IsAdmin or
        /// IsTenantAdmin flags, unless an administrator opts it in with inheritOwnerBypass (audited).
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public static async Task CredentialLeastPrivilegeAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                string t = "/v1.0/api/tenants/" + host.TenantId;
                User admin = Assert.NotNull(await host.Server.Context.Database.Users.ReadByEmailAsync(host.TenantId, "admin@hugo.local", token).ConfigureAwait(false), "seeded administrator");

                // A system administrator's credential scoped to Operator is an Operator: no secrets, no users, no settings.
                CredentialCreateResponse operatorCredential = await CreateCredentialAsync(host, host.TenantId, admin.Id, "Operator", token).ConfigureAwait(false);
                using (HttpClient client = host.CredentialClient(operatorCredential.AccessKey, operatorCredential.SecretKey))
                {
                    TokenDetailsResponse details = await HttpTestHelpers.SendAsync<TokenDetailsResponse>(client, HttpMethod.Get, "/v1.0/token/details", null, 200, token).ConfigureAwait(false);
                    Assert.True(!details.IsAdmin && !details.IsTenantAdmin, "no bypass flags on the credential");
                    Assert.Equal(403, (await HttpTestHelpers.SendAsync(client, HttpMethod.Get, "/v1.0/api/secrets", null, token).ConfigureAwait(false)).StatusCode, "Operator credential reading secrets");
                    Assert.Equal(403, (await HttpTestHelpers.SendAsync(client, HttpMethod.Get, t + "/users", null, token).ConfigureAwait(false)).StatusCode, "Operator credential listing users");
                    Assert.Equal(403, (await HttpTestHelpers.SendAsync(client, HttpMethod.Get, "/v1.0/api/settings", null, token).ConfigureAwait(false)).StatusCode, "Operator credential reading platform settings");
                    Assert.Equal(200, (await HttpTestHelpers.SendAsync(client, HttpMethod.Get, "/v1.0/api/approvals", null, token).ConfigureAwait(false)).StatusCode, "Operator credential reading approvals");
                }

                // A credential without any scope assignment is denied, even when its owner is a system administrator.
                CredentialCreateResponse bare = await CreateCredentialAsync(host, host.TenantId, admin.Id, null, token).ConfigureAwait(false);
                using (HttpClient client = host.CredentialClient(bare.AccessKey, bare.SecretKey))
                {
                    Assert.Equal(403, (await HttpTestHelpers.SendAsync(client, HttpMethod.Get, "/v1.0/api/resources", null, token).ConfigureAwait(false)).StatusCode, "unscoped credential");
                }

                // A TenantAdmin-scoped credential of a system administrator administers the tenant but not the platform.
                CredentialCreateResponse tenantScoped = await CreateCredentialAsync(host, host.TenantId, admin.Id, "TenantAdmin", token).ConfigureAwait(false);
                using (HttpClient client = host.CredentialClient(tenantScoped.AccessKey, tenantScoped.SecretKey))
                {
                    Assert.Equal(200, (await HttpTestHelpers.SendAsync(client, HttpMethod.Get, t + "/users", null, token).ConfigureAwait(false)).StatusCode, "TenantAdmin credential lists users");
                    Assert.Equal(403, (await HttpTestHelpers.SendAsync(client, HttpMethod.Get, "/v1.0/api/settings", null, token).ConfigureAwait(false)).StatusCode, "TenantAdmin credential on platform settings");
                }

                // The owner ceiling honors an owner who is a tenant administrator by flag only (no role assignment).
                User flagged = await CreateUserAsync(host, host.TenantId, "flagged@example.com", null, token).ConfigureAwait(false);
                await HttpTestHelpers.SendAsync<User>(host.AdminClient, HttpMethod.Put, t + "/users/" + flagged.Id, new UserRequest { IsTenantAdmin = true }, 200, token).ConfigureAwait(false);
                foreach (UserRoleAssignment assignment in await host.Server.Context.Database.UserRoleAssignments.ListByUserAsync(host.TenantId, flagged.Id, token).ConfigureAwait(false))
                {
                    await host.Server.Context.Database.UserRoleAssignments.DeleteAsync(assignment.Id, token).ConfigureAwait(false);
                }
                host.Server.Context.Authorization.Invalidate();
                CredentialCreateResponse security = await CreateCredentialAsync(host, host.TenantId, flagged.Id, "SecurityAdmin", token).ConfigureAwait(false);
                using (HttpClient client = host.CredentialClient(security.AccessKey, security.SecretKey))
                {
                    Assert.Equal(200, (await HttpTestHelpers.SendAsync(client, HttpMethod.Get, t + "/users", null, token).ConfigureAwait(false)).StatusCode, "SecurityAdmin credential of a flag-only tenant administrator");
                    Assert.Equal(403, (await HttpTestHelpers.SendAsync(client, HttpMethod.Get, "/v1.0/api/secrets", null, token).ConfigureAwait(false)).StatusCode, "SecurityAdmin credential does not get the owner's bypass for secrets");
                    TestResponse escalate = await HttpTestHelpers.SendAsync(client, HttpMethod.Put, t + "/credentials/" + security.Credential.Id, new CredentialRequest { InheritOwnerBypass = true }, token).ConfigureAwait(false);
                    Assert.Equal(403, escalate.StatusCode, "a credential cannot opt itself into the owner's bypass");
                    Assert.True(escalate.Body.Contains("BypassRequiresAdministrator", StringComparison.Ordinal), "escalation error code: " + escalate.Body);
                }

                // A SecurityAdmin user can manage credentials but cannot grant the bypass opt-in.
                await CreateUserAsync(host, host.TenantId, "secadmin@example.com", "SecurityAdmin", token).ConfigureAwait(false);
                using (HttpClient secAdmin = await host.UserClientAsync("secadmin@example.com", "password123", host.TenantId, token).ConfigureAwait(false))
                {
                    TestResponse refused = await HttpTestHelpers.SendAsync(secAdmin, HttpMethod.Post, t + "/credentials", new CredentialRequest { Name = "automation", UserId = admin.Id, RoleName = "Operator", InheritOwnerBypass = true }, token).ConfigureAwait(false);
                    Assert.Equal(403, refused.StatusCode, "SecurityAdmin granting inheritOwnerBypass");
                }

                // The opt-in: an administrator lets an automation credential carry the owner's bypass; it is audited, and
                // turning it off removes the bypass again.
                CredentialCreateResponse automation = await HttpTestHelpers.SendAsync<CredentialCreateResponse>(host.AdminClient, HttpMethod.Post, t + "/credentials", new CredentialRequest { Name = "automation", UserId = admin.Id, RoleName = "Operator", InheritOwnerBypass = true }, 201, token).ConfigureAwait(false);
                Assert.True(automation.Credential.InheritOwnerBypass, "opt-in stored");
                using (HttpClient client = host.CredentialClient(automation.AccessKey, automation.SecretKey))
                {
                    TokenDetailsResponse details = await HttpTestHelpers.SendAsync<TokenDetailsResponse>(client, HttpMethod.Get, "/v1.0/token/details", null, 200, token).ConfigureAwait(false);
                    Assert.True(details.IsAdmin, "the opted-in credential carries the owner's IsAdmin");
                    Assert.Equal(200, (await HttpTestHelpers.SendAsync(client, HttpMethod.Get, "/v1.0/api/settings", null, token).ConfigureAwait(false)).StatusCode, "opted-in credential on platform settings");
                }
                AuditPage audited = await HttpTestHelpers.SendAsync<AuditPage>(host.AdminClient, HttpMethod.Get, t + "/audit?eventType=CredentialBypassChanged", null, 200, token).ConfigureAwait(false);
                Assert.True(audited.Objects.Any(r => r.ResourceId == automation.Credential.Id && (r.Description ?? String.Empty).Contains("now carries", StringComparison.Ordinal)), "opt-in audited");

                Credential off = await HttpTestHelpers.SendAsync<Credential>(host.AdminClient, HttpMethod.Put, t + "/credentials/" + automation.Credential.Id, new CredentialRequest { InheritOwnerBypass = false }, 200, token).ConfigureAwait(false);
                Assert.True(!off.InheritOwnerBypass, "opt-in removed");
                using (HttpClient client = host.CredentialClient(automation.AccessKey, automation.SecretKey))
                {
                    Assert.Equal(403, (await HttpTestHelpers.SendAsync(client, HttpMethod.Get, "/v1.0/api/settings", null, token).ConfigureAwait(false)).StatusCode, "bypass gone after opting out");
                }
                AuditPage removed = await HttpTestHelpers.SendAsync<AuditPage>(host.AdminClient, HttpMethod.Get, t + "/audit?eventType=CredentialBypassChanged", null, 200, token).ConfigureAwait(false);
                Assert.True(removed.Objects.Any(r => r.ResourceId == automation.Credential.Id && (r.Description ?? String.Empty).Contains("no longer carries", StringComparison.Ordinal)), "opt-out audited");

                EffectivePermissionsResponse effective = await HttpTestHelpers.SendAsync<EffectivePermissionsResponse>(host.AdminClient, HttpMethod.Get, t + "/credentials/" + operatorCredential.Credential.Id + "/permissions", null, 200, token).ConfigureAwait(false);
                Assert.True(!effective.IsAdmin && !effective.IsTenantAdmin, "effective permissions show no inherited bypass");
            }
        }

        /// <summary>
        /// Administrator and tenant administrator bypass rules and platform-only resources.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public static async Task BypassAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                AuthorizationService authorization = host.Server.Context.Authorization;
                User tenantAdmin = await CreateUserAsync(host, host.TenantId, "tadmin@example.com", null, token).ConfigureAwait(false);
                await HttpTestHelpers.SendAsync<User>(host.AdminClient, HttpMethod.Put, "/v1.0/api/tenants/" + host.TenantId + "/users/" + tenantAdmin.Id, new UserRequest { IsTenantAdmin = true }, 200, token).ConfigureAwait(false);

                RequestContext ta = Principal(PrincipalTypeEnum.User, host.TenantId, tenantAdmin.Id, false, true);
                Assert.True((await authorization.EvaluateAsync(ta, host.TenantId, ResourceTypeEnum.Secret, OperationTypeEnum.Delete, "sec_1", token).ConfigureAwait(false)).Permitted, "Tenant admin bypass in own tenant");
                Assert.True(!(await authorization.EvaluateAsync(ta, "ten_other", ResourceTypeEnum.Secret, OperationTypeEnum.Read, null, token).ConfigureAwait(false)).Permitted, "Tenant admin outside own tenant");
                Assert.True(!(await authorization.EvaluateAsync(ta, host.TenantId, ResourceTypeEnum.Settings, OperationTypeEnum.Read, null, token).ConfigureAwait(false)).Permitted, "Tenant admin on platform settings");
                Assert.True(!(await authorization.EvaluateAsync(ta, null, ResourceTypeEnum.Tenant, OperationTypeEnum.Create, null, token).ConfigureAwait(false)).Permitted, "Tenant admin creating tenants");

                RequestContext admin = Principal(PrincipalTypeEnum.User, host.TenantId, "usr_admin", true, false);
                AuthorizationDecision adminDecision = await authorization.EvaluateAsync(admin, "ten_other", ResourceTypeEnum.Settings, OperationTypeEnum.Update, null, token).ConfigureAwait(false);
                Assert.True(adminDecision.Permitted && adminDecision.BypassReason == "IsAdmin", "System administrator bypass");

            }
        }

        /// <summary>
        /// Cross-tenant reads return 404.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public static async Task CrossTenantAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                Tenant other = await HttpTestHelpers.SendAsync<Tenant>(host.AdminClient, HttpMethod.Post, "/v1.0/api/tenants", new TenantRequest { Name = "Other" }, 201, token).ConfigureAwait(false);
                User otherUser = await CreateUserAsync(host, other.Id, "someone@other.com", null, token).ConfigureAwait(false);

                User local = await CreateUserAsync(host, host.TenantId, "local@example.com", null, token).ConfigureAwait(false);
                await HttpTestHelpers.SendAsync<User>(host.AdminClient, HttpMethod.Put, "/v1.0/api/tenants/" + host.TenantId + "/users/" + local.Id, new UserRequest { IsTenantAdmin = true }, 200, token).ConfigureAwait(false);
                using (HttpClient client = await host.UserClientAsync("local@example.com", "password123", host.TenantId, token).ConfigureAwait(false))
                {
                    Assert.Equal(404, (await HttpTestHelpers.SendAsync(client, HttpMethod.Get, "/v1.0/api/tenants/" + other.Id + "/users", null, token).ConfigureAwait(false)).StatusCode, "Cross-tenant list");
                    Assert.Equal(404, (await HttpTestHelpers.SendAsync(client, HttpMethod.Get, "/v1.0/api/tenants/" + other.Id + "/users/" + otherUser.Id, null, token).ConfigureAwait(false)).StatusCode, "Cross-tenant read");
                    Assert.Equal(404, (await HttpTestHelpers.SendAsync(client, HttpMethod.Get, "/v1.0/api/tenants/" + host.TenantId + "/users/" + otherUser.Id, null, token).ConfigureAwait(false)).StatusCode, "Foreign identifier in own tenant path");
                    EnumerationResult<Tenant> tenants = await HttpTestHelpers.SendAsync<EnumerationResult<Tenant>>(client, HttpMethod.Get, "/v1.0/api/tenants", null, 200, token).ConfigureAwait(false);
                    Assert.Equal(1, tenants.Objects.Count, "Tenant admins only see their own tenant");
                    Assert.Equal(403, (await HttpTestHelpers.SendAsync(client, HttpMethod.Post, "/v1.0/api/tenants", new TenantRequest { Name = "Nope" }, token).ConfigureAwait(false)).StatusCode, "Tenant admins cannot create tenants");
                }
            }
        }

        /// <summary>
        /// Tenant deletion cascades across tables; protected records survive.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public static async Task TenantCascadeAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                Tenant doomed = await HttpTestHelpers.SendAsync<Tenant>(host.AdminClient, HttpMethod.Post, "/v1.0/api/tenants", new TenantRequest { Name = "Doomed" }, 201, token).ConfigureAwait(false);
                User user = await CreateUserAsync(host, doomed.Id, "u@doomed.com", "Viewer", token).ConfigureAwait(false);
                CredentialCreateResponse credential = await CreateCredentialAsync(host, doomed.Id, user.Id, "Operator", token).ConfigureAwait(false);
                Secret secret = await HttpTestHelpers.SendAsync<Secret>(host.AdminClient, HttpMethod.Post, "/v1.0/api/tenants/" + doomed.Id + "/secrets", new { name = "doomed", kind = "ApiToken", values = new { token = "tok-doomed-value" } }, 201, token).ConfigureAwait(false);

                Assert.Equal(409, (await HttpTestHelpers.SendAsync(host.AdminClient, HttpMethod.Delete, "/v1.0/api/tenants/" + host.TenantId, null, token).ConfigureAwait(false)).StatusCode, "Protected tenant delete");
                User seededAdmin = Assert.NotNull(await host.Server.Context.Database.Users.ReadByEmailAsync(host.TenantId, "admin@hugo.local", token).ConfigureAwait(false), "Seeded administrator");
                Assert.Equal(409, (await HttpTestHelpers.SendAsync(host.AdminClient, HttpMethod.Delete, "/v1.0/api/tenants/" + host.TenantId + "/users/" + seededAdmin.Id, null, token).ConfigureAwait(false)).StatusCode, "Protected user delete");

                TestResponse deleted = await HttpTestHelpers.SendAsync(host.AdminClient, HttpMethod.Delete, "/v1.0/api/tenants/" + doomed.Id, null, token).ConfigureAwait(false);
                Assert.Equal(204, deleted.StatusCode, "Tenant delete status (" + deleted.Body + ")");
                Assert.True(await host.Server.Context.Database.Tenants.ReadAsync(doomed.Id, token).ConfigureAwait(false) == null, "Tenant remains.");
                Assert.True(await host.Server.Context.Database.Users.ReadAsync(user.Id, token).ConfigureAwait(false) == null, "User remains.");
                Assert.True(await host.Server.Context.Database.Credentials.ReadAsync(credential.Credential.Id, token).ConfigureAwait(false) == null, "Credential remains.");
                Assert.True(await host.Server.Context.Database.Secrets.ReadAsync(secret.Id, token).ConfigureAwait(false) == null, "Secret remains.");

                User doomedUser = await CreateUserAsync(host, host.TenantId, "gone@example.com", null, token).ConfigureAwait(false);
                CredentialCreateResponse doomedCred = await CreateCredentialAsync(host, host.TenantId, doomedUser.Id, null, token).ConfigureAwait(false);
                await HttpTestHelpers.SendAsync(host.AdminClient, HttpMethod.Delete, "/v1.0/api/tenants/" + host.TenantId + "/users/" + doomedUser.Id, null, token).ConfigureAwait(false);
                Assert.True(await host.Server.Context.Database.Credentials.ReadAsync(doomedCred.Credential.Id, token).ConfigureAwait(false) == null, "User delete left a credential.");
                Assert.Equal(0, (await host.Server.Context.Database.UserRoleAssignments.ListByUserAsync(host.TenantId, doomedUser.Id, token).ConfigureAwait(false)).Count, "User delete left assignments");
            }
        }

        /// <summary>
        /// Custom roles, permissions, mappings, assignments, built-in protection, and effective permissions.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public static async Task RolesAndAssignmentsAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                string t = "/v1.0/api/tenants/" + host.TenantId;
                EnumerationResult<Role> roles = await HttpTestHelpers.SendAsync<EnumerationResult<Role>>(host.AdminClient, HttpMethod.Get, t + "/roles?maxResults=100", null, 200, token).ConfigureAwait(false);
                Role builtIn = roles.Objects.First(r => r.Name == "TenantAdmin");
                Assert.Equal(409, (await HttpTestHelpers.SendAsync(host.AdminClient, HttpMethod.Put, t + "/roles/" + builtIn.Id, new RoleRequest { Name = "Hacked" }, token).ConfigureAwait(false)).StatusCode, "Built-in role edit");
                Assert.Equal(409, (await HttpTestHelpers.SendAsync(host.AdminClient, HttpMethod.Delete, t + "/roles/" + builtIn.Id, null, token).ConfigureAwait(false)).StatusCode, "Built-in role delete");

                Assert.Equal(400, (await HttpTestHelpers.SendAsync(host.AdminClient, HttpMethod.Post, t + "/permissions", new PermissionRequest { Name = "Bad", ResourceTypes = new List<string> { "Spaceship" }, OperationTypes = new List<string> { "Read" } }, token).ConfigureAwait(false)).StatusCode, "Invalid resource type");
                Permission readUsers = await HttpTestHelpers.SendAsync<Permission>(host.AdminClient, HttpMethod.Post, t + "/permissions", new PermissionRequest { Name = "Read users", ResourceTypes = new List<string> { "User" }, OperationTypes = new List<string> { "Read" } }, 201, token).ConfigureAwait(false);
                Role custom = await HttpTestHelpers.SendAsync<Role>(host.AdminClient, HttpMethod.Post, t + "/roles", new RoleRequest { Name = "UserReader" }, 201, token).ConfigureAwait(false);
                await HttpTestHelpers.SendAsync<RolePermissionMap>(host.AdminClient, HttpMethod.Post, t + "/role-permissions", new RolePermissionRequest { RoleId = custom.Id, PermissionId = readUsers.Id }, 201, token).ConfigureAwait(false);

                User user = await CreateUserAsync(host, host.TenantId, "reader@example.com", null, token).ConfigureAwait(false);
                using (HttpClient client = await host.UserClientAsync("reader@example.com", "password123", host.TenantId, token).ConfigureAwait(false))
                {
                    Assert.Equal(403, (await HttpTestHelpers.SendAsync(client, HttpMethod.Get, t + "/users", null, token).ConfigureAwait(false)).StatusCode, "Before assignment");
                    Assert.Equal(200, (await HttpTestHelpers.SendAsync(client, HttpMethod.Get, t + "/users/" + user.Id, null, token).ConfigureAwait(false)).StatusCode, "Self read");
                    UserRoleAssignment assignment = await HttpTestHelpers.SendAsync<UserRoleAssignment>(host.AdminClient, HttpMethod.Post, t + "/user-role-assignments", new AssignmentRequest { UserId = user.Id, RoleId = custom.Id }, 201, token).ConfigureAwait(false);
                    Assert.Equal(200, (await HttpTestHelpers.SendAsync(client, HttpMethod.Get, t + "/users", null, token).ConfigureAwait(false)).StatusCode, "After assignment");

                    EffectivePermissionsResponse effective = await HttpTestHelpers.SendAsync<EffectivePermissionsResponse>(client, HttpMethod.Get, t + "/users/" + user.Id + "/permissions", null, 200, token).ConfigureAwait(false);
                    Assert.True(effective.Grants.Any(g => g.Source == "UserReader"), "Custom role grant missing.");
                    Assert.True(effective.HasPermission.Any(c => c.ResourceType == ResourceTypeEnum.User && c.Operation == OperationTypeEnum.Read && c.Permitted), "hasPermission User Read.");
                    Assert.True(effective.HasPermission.Any(c => c.ResourceType == ResourceTypeEnum.User && c.Operation == OperationTypeEnum.Delete && !c.Permitted), "hasPermission User Delete.");

                    Permission denyUsers = await HttpTestHelpers.SendAsync<Permission>(host.AdminClient, HttpMethod.Post, t + "/permissions", new PermissionRequest { Name = "Deny users", PermissionType = PermissionTypeEnum.Deny, ResourceTypes = new List<string> { "User" }, OperationTypes = new List<string> { "All" } }, 201, token).ConfigureAwait(false);
                    await HttpTestHelpers.SendAsync<RolePermissionMap>(host.AdminClient, HttpMethod.Post, t + "/role-permissions", new RolePermissionRequest { RoleId = custom.Id, PermissionId = denyUsers.Id }, 201, token).ConfigureAwait(false);
                    Assert.Equal(403, (await HttpTestHelpers.SendAsync(client, HttpMethod.Get, t + "/users", null, token).ConfigureAwait(false)).StatusCode, "Explicit deny wins");

                    await HttpTestHelpers.SendAsync(host.AdminClient, HttpMethod.Delete, t + "/user-role-assignments/" + assignment.Id, null, token).ConfigureAwait(false);
                }

                Role clone = await HttpTestHelpers.SendAsync<Role>(host.AdminClient, HttpMethod.Post, t + "/roles/" + builtIn.Id + "/clone", new RoleRequest { Name = "MyTenantAdmin" }, 201, token).ConfigureAwait(false);
                EnumerationResult<RolePermissionMap> maps = await HttpTestHelpers.SendAsync<EnumerationResult<RolePermissionMap>>(host.AdminClient, HttpMethod.Get, t + "/role-permissions?roleId=" + clone.Id, null, 200, token).ConfigureAwait(false);
                Assert.True(maps.Objects.Count >= 1, "Clone copied no permissions.");
            }
        }

        /// <summary>
        /// Request history capture, redaction, listing, detail, and summary.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public static async Task RequestHistoryAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                for (int i = 0; i < 3; i++) await HttpTestHelpers.SendAsync(host.AdminClient, HttpMethod.Get, "/v1.0/api/health", null, token).ConfigureAwait(false);
                await HttpTestHelpers.SendAsync(host.AdminClient, HttpMethod.Get, "/v1.0/api/does-not-exist", null, token).ConfigureAwait(false);
                await HttpTestHelpers.SendAsync(host.AdminClient, HttpMethod.Post, "/v1.0/api/tenants/" + host.TenantId + "/credentials", new CredentialRequest { Name = "captured" }, token).ConfigureAwait(false);

                RequestHistoryPage page = new RequestHistoryPage();
                for (int attempt = 0; attempt < 50; attempt++)
                {
                    page = await HttpTestHelpers.SendAsync<RequestHistoryPage>(host.AdminClient, HttpMethod.Get, "/v1.0/api/request-history?pathContains=/v1.0/api/&pageSize=100", null, 200, token).ConfigureAwait(false);
                    if (page.Objects.Count(o => o.Path == "/v1.0/api/health") >= 3 && page.Objects.Any(o => o.Path.Contains("/credentials", StringComparison.Ordinal))) break;
                    await Task.Delay(100, token).ConfigureAwait(false);
                }
                Assert.True(page.Objects.Count(o => o.Path == "/v1.0/api/health") >= 3, "Health requests were not captured.");
                Assert.True(page.Objects.All(o => o.ResponseBody == null && o.RequestBody == null), "List returned bodies.");

                RequestHistoryEntry credentialEntry = page.Objects.First(o => o.Path.Contains("/credentials", StringComparison.Ordinal));
                RequestHistoryEntry detail = await HttpTestHelpers.SendAsync<RequestHistoryEntry>(host.AdminClient, HttpMethod.Get, "/v1.0/api/request-history/" + credentialEntry.Id, null, 200, token).ConfigureAwait(false);
                Assert.Equal(RequestHistoryCaptureService.Redacted, detail.ResponseBody, "Secret route response body");
                RequestHistoryHeader? apiKey = detail.Headers.FirstOrDefault(h => String.Equals(h.Name, "x-api-key", StringComparison.OrdinalIgnoreCase));
                Assert.True(apiKey != null && apiKey.Value == RequestHistoryCaptureService.Redacted, "x-api-key header not redacted.");

                RequestHistoryPage reads = new RequestHistoryPage();
                for (int attempt = 0; attempt < 50; attempt++)
                {
                    reads = await HttpTestHelpers.SendAsync<RequestHistoryPage>(host.AdminClient, HttpMethod.Get, "/v1.0/api/request-history?pathContains=/request-history/" + credentialEntry.Id + "&pageSize=10", null, 200, token).ConfigureAwait(false);
                    if (reads.Objects.Count >= 1) break;
                    await Task.Delay(100, token).ConfigureAwait(false);
                }
                Assert.True(reads.Objects.Count >= 1, "The request history read was not captured.");
                RequestHistoryEntry readDetail = await HttpTestHelpers.SendAsync<RequestHistoryEntry>(host.AdminClient, HttpMethod.Get, "/v1.0/api/request-history/" + reads.Objects[0].Id, null, 200, token).ConfigureAwait(false);
                Assert.True(readDetail.ResponseBody == null, "A request history read kept its response body, which copies history into itself.");
                Assert.True(readDetail.ResponseBodyBytes > 0, "The response size of a request history read was not recorded.");

                RequestHistoryPage failures = await HttpTestHelpers.SendAsync<RequestHistoryPage>(host.AdminClient, HttpMethod.Get, "/v1.0/api/request-history?statusCode=404", null, 200, token).ConfigureAwait(false);
                Assert.True(failures.Objects.Count >= 1 && failures.Objects.All(o => o.StatusCode == 404), "Status filter.");

                DateTime to = DateTime.UtcNow.AddMinutes(1);
                DateTime from = to.AddHours(-1);
                RequestHistorySummary summary = await HttpTestHelpers.SendAsync<RequestHistorySummary>(host.AdminClient, HttpMethod.Get, "/v1.0/api/request-history/summary?fromUtc=" + Uri.EscapeDataString(from.ToString("o")) + "&toUtc=" + Uri.EscapeDataString(to.ToString("o")) + "&bucketMinutes=1", null, 200, token).ConfigureAwait(false);
                Assert.Equal(60, summary.Buckets.Count, "Every bucket present");
                Assert.True(summary.TotalFailure >= 1 && summary.TotalSuccess >= 3, "Summary totals.");
                Assert.Equal(summary.TotalCount, summary.Buckets.Sum(b => b.SuccessCount + b.FailureCount), "Bucket sum");

                DeleteCountResponse cleared = await HttpTestHelpers.SendAsync<DeleteCountResponse>(host.AdminClient, HttpMethod.Delete, "/v1.0/api/request-history?statusCode=404", null, 200, token).ConfigureAwait(false);
                Assert.True(cleared.DeletedCount >= 1, "Bulk delete.");
            }
        }

        /// <summary>
        /// Authorization denials are audited and queryable.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public static async Task AuditAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                User user = await CreateUserAsync(host, host.TenantId, "denied@example.com", null, token).ConfigureAwait(false);
                using (HttpClient client = await host.UserClientAsync("denied@example.com", "password123", host.TenantId, token).ConfigureAwait(false))
                {
                    Assert.Equal(403, (await HttpTestHelpers.SendAsync(client, HttpMethod.Get, "/v1.0/api/tenants/" + host.TenantId + "/credentials", null, token).ConfigureAwait(false)).StatusCode, "Denied");
                }
                AuditPage page = await HttpTestHelpers.SendAsync<AuditPage>(host.AdminClient, HttpMethod.Get, "/v1.0/api/tenants/" + host.TenantId + "/audit?eventType=AuthorizationDenied&principalId=" + user.Id, null, 200, token).ConfigureAwait(false);
                Assert.True(page.TotalCount >= 1, "Denial not audited.");
                AuditRecord record = page.Objects[0];
                Assert.Equal("Credential", record.ResourceType, "Audited resource type");
                AuditRecord read = await HttpTestHelpers.SendAsync<AuditRecord>(host.AdminClient, HttpMethod.Get, "/v1.0/api/tenants/" + host.TenantId + "/audit/" + record.Id, null, 200, token).ConfigureAwait(false);
                Assert.Equal(record.Id, read.Id, "Audit read");
                AuditPage created = await HttpTestHelpers.SendAsync<AuditPage>(host.AdminClient, HttpMethod.Get, "/v1.0/api/tenants/" + host.TenantId + "/audit?eventType=ConfigurationChanged", null, 200, token).ConfigureAwait(false);
                Assert.True(created.TotalCount >= 1, "User creation not audited.");
            }
        }

        /// <summary>
        /// Settings metadata, validation, live cluster settings, and file writes.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public static async Task SettingsAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            string file = Path.Combine(Path.GetTempPath(), "hugo-test", Guid.NewGuid().ToString("N") + ".json");
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, s => { s.SourceFile = file; }, token).ConfigureAwait(false))
            {
                host.Settings.ToFile(file);
                SettingsView view = await HttpTestHelpers.SendAsync<SettingsView>(host.AdminClient, HttpMethod.Get, "/v1.0/api/settings", null, 200, token).ConfigureAwait(false);
                SettingField port = view.Fields.First(f => f.Path == "Rest.Port");
                Assert.Equal("RestartRequired", port.ApplyMode, "Rest.Port apply mode");
                Assert.Equal("File", port.Source, "Rest.Port source");
                SettingField password = view.Fields.First(f => f.Path == "Auth.SigningKey");
                Assert.True(password.IsSecret && password.IsSet && password.Value == SettingsService.SecretMask, "Secrets are masked.");
                SettingField reveal = view.Fields.First(f => f.Path == "Secrets.RevealReauthMinutes");
                Assert.Equal("Live", reveal.ApplyMode, "Reveal window applies live");
                SettingField masterKey = view.Fields.First(f => f.Path == "Secrets.MasterKey");
                Assert.True(masterKey.IsSecret, "Master key is a secret field.");
                SettingField retention = view.Fields.First(f => f.Path == "Audit.RetentionDays");
                Assert.Equal("Database", retention.Source, "Runtime setting source");
                Assert.Equal("365", retention.Value, "Runtime setting default");

                TestResponse invalid = await HttpTestHelpers.SendAsync(host.AdminClient, HttpMethod.Put, "/v1.0/api/settings", Update("Rest.Port", "99999", "Audit.RetentionDays", "0", "Nope.Nothing", "1"), token).ConfigureAwait(false);
                Assert.Equal(400, invalid.StatusCode, "Invalid update status");
                foreach (string path in new[] { "Rest.Port", "Audit.RetentionDays", "Nope.Nothing" }) Assert.True(invalid.Body.Contains(path, StringComparison.Ordinal), "Missing field error for " + path);

                SettingsSaveResponse saved = await HttpTestHelpers.SendAsync<SettingsSaveResponse>(host.AdminClient, HttpMethod.Put, "/v1.0/api/settings", Update("Audit.RetentionDays", "90", "Logging.MinimumSeverity", "2", "Rest.Port", "9123", "Auth.SigningKey", SettingsService.SecretMask, "Secrets.RevealReauthMinutes", "7"), 200, token).ConfigureAwait(false);
                Assert.True(saved.RestartRequired && saved.PendingRestart.Contains("Rest.Port"), "Rest.Port pending restart.");
                Assert.True(saved.AppliedLive.Contains("Audit.RetentionDays") && saved.AppliedLive.Contains("Logging.MinimumSeverity"), "Live settings applied.");
                Assert.Equal(90, host.Server.Context.Get<ClusterSettingsService>().GetInt("Audit.RetentionDays"), "Runtime setting live");
                Assert.Equal(7, host.Settings.Secrets.RevealReauthMinutes, "Reveal window applied live");
                Assert.Equal(2, host.Settings.Logging.MinimumSeverity, "Live file setting applied");
                Assert.True(host.Settings.Rest.Port != 9123, "Restart-required setting applied live.");
                Assert.Equal("test-signing-key-0123456789", host.Settings.Auth.SigningKey, "Mask must not overwrite the secret");

                string written = File.ReadAllText(file);
                Assert.True(written.Contains("9123", StringComparison.Ordinal), "Settings file not written.");

                using (HttpClient member = await MemberClientAsync(host, token).ConfigureAwait(false))
                {
                    Assert.Equal(403, (await HttpTestHelpers.SendAsync(member, HttpMethod.Get, "/v1.0/api/settings", null, token).ConfigureAwait(false)).StatusCode, "Settings require a system administrator");
                }
            }
            if (File.Exists(file)) File.Delete(file);
        }

        #endregion

        /// <summary>
        /// A legacy unsalted SHA-256 password hash still signs in and is replaced by a PBKDF2 hash; a wrong password is
        /// rejected and leaves the stored hash alone; created and updated passwords are stored as PBKDF2.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public static async Task PasswordUpgradeAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                User seededAdmin = Assert.NotNull(await host.Server.Context.Database.Users.ReadByEmailAsync(host.TenantId, "admin@hugo.local", token).ConfigureAwait(false), "Seeded administrator");
                Assert.True(seededAdmin.PasswordHash.StartsWith("pbkdf2-sha256$600000$", StringComparison.Ordinal), "Seeded administrator hash format: " + seededAdmin.PasswordHash);

                User created = await CreateUserAsync(host, host.TenantId, "legacy@example.com", null, token).ConfigureAwait(false);
                User stored = Assert.NotNull(await host.Server.Context.Database.Users.ReadAsync(host.TenantId, created.Id, token).ConfigureAwait(false), "Created user");
                Assert.True(stored.PasswordHash.StartsWith("pbkdf2-sha256$", StringComparison.Ordinal), "Created user hash format: " + stored.PasswordHash);
                Assert.True(PasswordHasher.Verify("password123", stored.PasswordHash), "Created user hash does not verify.");

                string legacy = PasswordHasher.LegacySha256("password123");
                stored.PasswordHash = legacy;
                await host.Server.Context.Database.Users.UpdateAsync(stored, token).ConfigureAwait(false);

                using (HttpClient anonymous = new HttpClient())
                {
                    anonymous.BaseAddress = new Uri(host.BaseUrl);
                    TestResponse wrong = await HttpTestHelpers.SendAsync(anonymous, HttpMethod.Post, "/v1.0/token", new TokenRequest { TenantId = host.TenantId, Email = "legacy@example.com", Password = "password124" }, token).ConfigureAwait(false);
                    Assert.Equal(401, wrong.StatusCode, "Wrong password against a legacy hash");
                    User afterWrong = Assert.NotNull(await host.Server.Context.Database.Users.ReadAsync(host.TenantId, created.Id, token).ConfigureAwait(false), "User after wrong password");
                    Assert.Equal(legacy, afterWrong.PasswordHash, "A failed sign-in changed the stored hash");

                    TestResponse unknown = await HttpTestHelpers.SendAsync(anonymous, HttpMethod.Post, "/v1.0/token", new TokenRequest { TenantId = host.TenantId, Email = "nobody@example.com", Password = "password123" }, token).ConfigureAwait(false);
                    Assert.Equal(401, unknown.StatusCode, "Unknown user");
                }

                using (HttpClient upgraded = await host.UserClientAsync("legacy@example.com", "password123", host.TenantId, token).ConfigureAwait(false))
                {
                    Assert.Equal(200, (await HttpTestHelpers.SendAsync(upgraded, HttpMethod.Get, "/v1.0/token/details", null, token).ConfigureAwait(false)).StatusCode, "Session issued after the legacy sign-in");
                }
                User afterLogin = Assert.NotNull(await host.Server.Context.Database.Users.ReadAsync(host.TenantId, created.Id, token).ConfigureAwait(false), "User after sign-in");
                Assert.True(afterLogin.PasswordHash.StartsWith("pbkdf2-sha256$600000$", StringComparison.Ordinal), "Legacy hash was not upgraded: " + afterLogin.PasswordHash);
                Assert.True(PasswordHasher.Verify("password123", afterLogin.PasswordHash), "Upgraded hash does not verify.");

                using (HttpClient again = await host.UserClientAsync("legacy@example.com", "password123", host.TenantId, token).ConfigureAwait(false))
                {
                    User unchanged = Assert.NotNull(await host.Server.Context.Database.Users.ReadAsync(host.TenantId, created.Id, token).ConfigureAwait(false), "User after second sign-in");
                    Assert.Equal(afterLogin.PasswordHash, unchanged.PasswordHash, "A current hash was rewritten on sign-in");
                }

                await HttpTestHelpers.SendAsync<User>(host.AdminClient, HttpMethod.Put, "/v1.0/api/tenants/" + host.TenantId + "/users/" + created.Id, new UserRequest { Password = "new-password-456" }, 200, token).ConfigureAwait(false);
                User updated = Assert.NotNull(await host.Server.Context.Database.Users.ReadAsync(host.TenantId, created.Id, token).ConfigureAwait(false), "User after password change");
                Assert.True(updated.PasswordHash.StartsWith("pbkdf2-sha256$", StringComparison.Ordinal) && PasswordHasher.Verify("new-password-456", updated.PasswordHash), "Updated password is not stored as PBKDF2.");
                Assert.True(!PasswordHasher.Verify("password123", updated.PasswordHash), "Old password still verifies after the change.");
            }
        }

        #region Private-Methods

        private static SettingsUpdateRequest Update(params string[] pathValuePairs)
        {
            SettingsUpdateRequest request = new SettingsUpdateRequest();
            for (int i = 0; i + 1 < pathValuePairs.Length; i += 2) request.Values.Add(new SettingValue { Path = pathValuePairs[i], Value = pathValuePairs[i + 1] });
            return request;
        }

        private static async Task<HttpClient> MemberClientAsync(TestServerHost host, CancellationToken token)
        {
            await CreateUserAsync(host, host.TenantId, "member2@example.com", null, token).ConfigureAwait(false);
            return await host.UserClientAsync("member2@example.com", "password123", host.TenantId, token).ConfigureAwait(false);
        }

        private static async Task<User> CreateUserAsync(TestServerHost host, string tenantId, string email, string? roleName, CancellationToken token)
        {
            User user = await HttpTestHelpers.SendAsync<User>(host.AdminClient, HttpMethod.Post, "/v1.0/api/tenants/" + tenantId + "/users", new UserRequest { Email = email, Password = "password123" }, 201, token).ConfigureAwait(false);
            if (!String.IsNullOrEmpty(roleName))
            {
                await HttpTestHelpers.SendAsync<UserRoleAssignment>(host.AdminClient, HttpMethod.Post, "/v1.0/api/tenants/" + tenantId + "/user-role-assignments", new AssignmentRequest { UserId = user.Id, RoleName = roleName }, 201, token).ConfigureAwait(false);
            }
            return user;
        }

        private static async Task<CredentialCreateResponse> CreateCredentialAsync(TestServerHost host, string tenantId, string? userId, string? roleName, CancellationToken token)
        {
            if (userId == null)
            {
                User admin = (await host.Server.Context.Database.Users.ReadByEmailAsync(host.TenantId, "admin@hugo.local", token).ConfigureAwait(false))!;
                userId = admin.Id;
            }
            return await HttpTestHelpers.SendAsync<CredentialCreateResponse>(host.AdminClient, HttpMethod.Post, "/v1.0/api/tenants/" + tenantId + "/credentials", new CredentialRequest { Name = "test-" + Guid.NewGuid().ToString("N").Substring(0, 6), UserId = userId, RoleName = roleName }, 201, token).ConfigureAwait(false);
        }

        private static RequestContext Principal(PrincipalTypeEnum type, string tenantId, string? userId, bool isAdmin, bool isTenantAdmin)
        {
            RequestContext ctx = new RequestContext();
            ctx.IsAuthenticated = true;
            ctx.PrincipalType = type;
            ctx.TenantId = tenantId;
            ctx.UserId = userId;
            ctx.IsAdmin = isAdmin;
            ctx.IsTenantAdmin = isTenantAdmin;
            return ctx;
        }

        #endregion
    }
}
