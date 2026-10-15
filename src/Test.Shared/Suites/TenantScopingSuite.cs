namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;
    using Hugo.Core.Responses;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;

    /// <summary>
    /// Multi-tenant scoping (P1.44, Armada MultiTenantScopingTests pattern): for every Phase 1 and Phase 2 entity, a
    /// tenant administrator of tenant A attempts read, update, delete, and enumerate on tenant B's record, through both
    /// tenant paths, and must get 404 or an empty list.
    /// </summary>
    public static class TenantScopingSuite
    {
        /// <summary>
        /// Build the suites.
        /// </summary>
        /// <returns>Suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            List<TestSuiteDescriptor> suites = new List<TestSuiteDescriptor>();
            foreach (DatabaseTypeEnum provider in TestEnvironment.Providers())
            {
                string id = "tenant-scoping-" + provider.ToString().ToLowerInvariant();
                List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
                cases.Add(new TestCaseDescriptor(id, "every-entity", "Every tenant-owned entity is invisible and immutable from another tenant (" + provider + ")", ct => EveryEntityAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "request-history", "Request history lists only the caller's tenant (" + provider + ")", ct => RequestHistoryAsync(provider, ct)));
                suites.Add(new TestSuiteDescriptor(id, "Multi-tenant scoping (" + provider + ")", cases));
            }
            return suites;
        }

        private static async Task EveryEntityAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                string a = host.TenantId;
                Tenant tenantB = await HttpTestHelpers.SendAsync<Tenant>(host.AdminClient, HttpMethod.Post, "/v1.0/api/tenants", new TenantRequest { Name = "Tenant B" }, 201, token).ConfigureAwait(false);
                string b = tenantB.Id;
                string pb = "/v1.0/api/tenants/" + b;

                // Records in tenant B, created by the system administrator.
                User userB = await HttpTestHelpers.SendAsync<User>(host.AdminClient, HttpMethod.Post, pb + "/users", new UserRequest { Email = "b@scoping.local", Password = "password123" }, 201, token).ConfigureAwait(false);
                CredentialCreateResponse credentialB = await HttpTestHelpers.SendAsync<CredentialCreateResponse>(host.AdminClient, HttpMethod.Post, pb + "/credentials", new CredentialRequest { Name = "b", UserId = userB.Id }, 201, token).ConfigureAwait(false);
                Role roleB = await HttpTestHelpers.SendAsync<Role>(host.AdminClient, HttpMethod.Post, pb + "/roles", new RoleRequest { Name = "RoleB" }, 201, token).ConfigureAwait(false);
                Permission permissionB = await HttpTestHelpers.SendAsync<Permission>(host.AdminClient, HttpMethod.Post, pb + "/permissions", new PermissionRequest { Name = "PermB", ResourceTypes = new List<string> { "User" }, OperationTypes = new List<string> { "Read" } }, 201, token).ConfigureAwait(false);
                RolePermissionMap mapB = await HttpTestHelpers.SendAsync<RolePermissionMap>(host.AdminClient, HttpMethod.Post, pb + "/role-permissions", new RolePermissionRequest { RoleId = roleB.Id, PermissionId = permissionB.Id }, 201, token).ConfigureAwait(false);
                UserRoleAssignment uraB = await HttpTestHelpers.SendAsync<UserRoleAssignment>(host.AdminClient, HttpMethod.Post, pb + "/user-role-assignments", new AssignmentRequest { UserId = userB.Id, RoleId = roleB.Id }, 201, token).ConfigureAwait(false);
                CredentialScopeAssignment csaB = await HttpTestHelpers.SendAsync<CredentialScopeAssignment>(host.AdminClient, HttpMethod.Post, pb + "/credential-scope-assignments", new AssignmentRequest { CredentialId = credentialB.Credential.Id, RoleName = "Viewer" }, 201, token).ConfigureAwait(false);
                Secret secretB = await HttpTestHelpers.SendAsync<Secret>(host.AdminClient, HttpMethod.Post, pb + "/secrets", new { name = "secret-b", kind = "Password", values = new { password = "tenant-b-password" } }, 201, token).ConfigureAwait(false);
                PromptTemplate templateB = await HttpTestHelpers.SendAsync<PromptTemplate>(host.AdminClient, HttpMethod.Post, pb + "/prompt-templates", new PromptTemplateRequest { Name = "template-b", Content = "Hello {{ user.name }}" }, 201, token).ConfigureAwait(false);
                TokenResponse sessionB = await host.LoginAsync("b@scoping.local", "password123", b, token).ConfigureAwait(false);
                AuditPage auditB = await HttpTestHelpers.SendAsync<AuditPage>(host.AdminClient, HttpMethod.Get, pb + "/audit?pageSize=1", null, 200, token).ConfigureAwait(false);
                Assert.True(auditB.Objects.Count == 1, "Tenant B has audit records.");
                string auditIdB = auditB.Objects[0].Id;

                Dictionary<string, string> entities = new Dictionary<string, string>
                {
                    { "users", userB.Id },
                    { "credentials", credentialB.Credential.Id },
                    { "roles", roleB.Id },
                    { "permissions", permissionB.Id },
                    { "user-role-assignments", uraB.Id },
                    { "credential-scope-assignments", csaB.Id },
                    { "secrets", secretB.Id },
                    { "prompt-templates", templateB.Id },
                    { "sessions", sessionB.SessionId },
                    { "audit", auditIdB }
                };

                List<string> failures = new List<string>();
                using (HttpClient anderson = await host.ActivateSeededUserAsync("anderson@hugo.local", token).ConfigureAwait(false))
                {
                    foreach (KeyValuePair<string, string> entity in entities)
                    {
                        foreach (string tenantPath in new[] { "/v1.0/api/tenants/" + a, "/v1.0/api/tenants/" + b })
                        {
                            string item = tenantPath + "/" + entity.Key + "/" + entity.Value;
                            await Expect404Async(anderson, HttpMethod.Get, item, null, failures, token).ConfigureAwait(false);
                            await Expect404Async(anderson, HttpMethod.Put, item, "{}", failures, token).ConfigureAwait(false);
                            await Expect404Async(anderson, HttpMethod.Delete, item, null, failures, token).ConfigureAwait(false);
                        }
                        TestResponse list = await HttpTestHelpers.SendAsync(anderson, HttpMethod.Get, "/v1.0/api/tenants/" + a + "/" + entity.Key + "?maxResults=1000&pageSize=1000", null, token).ConfigureAwait(false);
                        if (list.StatusCode != 200) failures.Add("list " + entity.Key + " in own tenant: " + list.StatusCode);
                        else if (list.Body.Contains("\"id\":\"" + entity.Value + "\"", StringComparison.Ordinal)) failures.Add("list " + entity.Key + " in own tenant shows tenant B's record");
                        await Expect404Async(anderson, HttpMethod.Get, "/v1.0/api/tenants/" + b + "/" + entity.Key, null, failures, token).ConfigureAwait(false);
                    }

                    foreach (string extra in new[] { "/secrets/" + secretB.Id + "/values", "/secrets/" + secretB.Id + "/reveal", "/secrets/" + secretB.Id + "/referrers", "/prompt-templates/" + templateB.Id + "/versions", "/credentials/" + credentialB.Credential.Id + "/rotate", "/users/" + userB.Id + "/password", "/roles/" + roleB.Id + "/clone", "/role-permissions/" + mapB.Id })
                    {
                        string method = extra.EndsWith("/versions", StringComparison.Ordinal) || extra.EndsWith("/referrers", StringComparison.Ordinal) ? "GET" : (extra.Contains("/values", StringComparison.Ordinal) ? "PUT" : (extra.Contains("/role-permissions/", StringComparison.Ordinal) ? "DELETE" : "POST"));
                        await Expect404Async(anderson, new HttpMethod(method), "/v1.0/api/tenants/" + a + extra, method == "GET" || method == "DELETE" ? null : "{\"reason\":\"x\",\"values\":{\"password\":\"y\"},\"newPassword\":\"Another-Password-1\",\"name\":\"z\"}", failures, token).ConfigureAwait(false);
                        await Expect404Async(anderson, new HttpMethod(method), "/v1.0/api/tenants/" + b + extra, method == "GET" || method == "DELETE" ? null : "{\"reason\":\"x\"}", failures, token).ConfigureAwait(false);
                    }

                    await Expect404Async(anderson, HttpMethod.Get, "/v1.0/tenants/" + b + "/users/" + userB.Id + "/permissions", null, failures, token).ConfigureAwait(false);
                    await Expect404Async(anderson, HttpMethod.Get, "/v1.0/tenants/" + a + "/credentials/" + credentialB.Credential.Id + "/permissions", null, failures, token).ConfigureAwait(false);
                }
                Assert.True(failures.Count == 0, failures.Count + " cross-tenant leaks: " + String.Join(" | ", failures.Take(25)));

                // Nothing in tenant B changed.
                Secret stillB = Assert.NotNull(await host.Server.Context.Database.Secrets.ReadAsync(b, secretB.Id, token).ConfigureAwait(false), "Tenant B secret");
                Assert.Equal(1, stillB.Values.Count, "Tenant B secret values");
                Assert.True(await host.Server.Context.Database.Users.ReadAsync(b, userB.Id, token).ConfigureAwait(false) != null, "Tenant B user survived.");
                AuthSession? session = await host.Server.Context.Database.Sessions.ReadAsync(sessionB.SessionId, token).ConfigureAwait(false);
                Assert.True(session != null && !session.RevokedUtc.HasValue, "Tenant B session survived.");
            }
        }

        private static async Task RequestHistoryAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                Tenant tenantB = await HttpTestHelpers.SendAsync<Tenant>(host.AdminClient, HttpMethod.Post, "/v1.0/api/tenants", new TenantRequest { Name = "History B" }, 201, token).ConfigureAwait(false);
                await HttpTestHelpers.SendAsync<User>(host.AdminClient, HttpMethod.Post, "/v1.0/api/tenants/" + tenantB.Id + "/users", new UserRequest { Email = "hb@scoping.local", Password = "password123", IsTenantAdmin = true }, 201, token).ConfigureAwait(false);
                using (HttpClient userB = await host.UserClientAsync("hb@scoping.local", "password123", tenantB.Id, token).ConfigureAwait(false))
                {
                    for (int i = 0; i < 3; i++) await HttpTestHelpers.SendAsync(userB, HttpMethod.Get, "/v1.0/api/tenants/" + tenantB.Id + "/users", null, token).ConfigureAwait(false);
                }
                using (HttpClient anderson = await host.ActivateSeededUserAsync("anderson@hugo.local", token).ConfigureAwait(false))
                {
                    await HttpTestHelpers.SendAsync(anderson, HttpMethod.Get, "/v1.0/api/tenants/" + host.TenantId + "/users", null, token).ConfigureAwait(false);
                    RequestHistoryPage page = new RequestHistoryPage();
                    for (int attempt = 0; attempt < 50; attempt++)
                    {
                        page = await HttpTestHelpers.SendAsync<RequestHistoryPage>(anderson, HttpMethod.Get, "/v1.0/api/request-history?pageSize=1000&tenantId=" + tenantB.Id, null, 200, token).ConfigureAwait(false);
                        if (page.Objects.Any()) break;
                        await Task.Delay(100, token).ConfigureAwait(false);
                    }
                    Assert.True(page.Objects.Count > 0, "Own tenant history is empty.");
                    Assert.True(page.Objects.All(o => o.TenantId == host.TenantId), "Request history leaked another tenant's entries.");
                    RequestHistoryPage all = await HttpTestHelpers.SendAsync<RequestHistoryPage>(host.AdminClient, HttpMethod.Get, "/v1.0/api/request-history?pageSize=1000&tenantId=" + tenantB.Id, null, 200, token).ConfigureAwait(false);
                    RequestHistoryEntry? foreign = all.Objects.FirstOrDefault(o => o.TenantId == tenantB.Id);
                    Assert.True(foreign != null, "Tenant B history missing for the administrator.");
                    TestResponse read = await HttpTestHelpers.SendAsync(anderson, HttpMethod.Get, "/v1.0/api/request-history/" + foreign!.Id, null, token).ConfigureAwait(false);
                    Assert.Equal(404, read.StatusCode, "Foreign history entry");
                }
            }
        }

        private static async Task Expect404Async(HttpClient client, HttpMethod method, string path, string? body, List<string> failures, CancellationToken token)
        {
            TestResponse response = await HttpTestHelpers.SendAsync(client, method, path, body, token).ConfigureAwait(false);
            if (response.StatusCode != 404) failures.Add(method + " " + path + ": " + response.StatusCode + " " + (response.Body.Length > 120 ? response.Body.Substring(0, 120) : response.Body));
        }
    }
}
