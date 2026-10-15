namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;
    using Hugo.Core.Security;
    using Hugo.Server.Routes;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;

    /// <summary>
    /// Authorization matrix (P1.43): every scoped route is called by a user holding each built-in role (tenant scope)
    /// and by a tenant administrator of another tenant. A role's expected outcome comes from its built-in grants and the
    /// platform-only rule; a hand-written table pins the plan section 15.4 semantics for key routes so the oracle cannot
    /// drift silently.
    /// </summary>
    public static class AuthorizationMatrixSuite
    {
        #region Private-Members

        private static readonly Regex _Parameter = new Regex(@"\{([A-Za-z0-9_]+)\}", RegexOptions.Compiled);
        private static readonly HashSet<ResourceTypeEnum> _PlatformOnly = new HashSet<ResourceTypeEnum> { ResourceTypeEnum.Settings, ResourceTypeEnum.Admin, ResourceTypeEnum.Plugin };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suites.
        /// </summary>
        /// <returns>Suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            List<TestSuiteDescriptor> suites = new List<TestSuiteDescriptor>();
            List<TestCaseDescriptor> unit = new List<TestCaseDescriptor>();
            unit.Add(new TestCaseDescriptor("authz-unit", "builtin-semantics", "Built-in roles grant what plan section 15.4 says", BuiltInSemanticsAsync));
            suites.Add(new TestSuiteDescriptor("authz-unit", "Authorization semantics", unit));

            foreach (DatabaseTypeEnum provider in TestEnvironment.Providers())
            {
                string id = "authz-matrix-" + provider.ToString().ToLowerInvariant();
                List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
                cases.Add(new TestCaseDescriptor(id, "every-route-every-role", "Every scoped route by every built-in role matches the expected decision (" + provider + ")", ct => MatrixAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "foreign-tenant", "A tenant administrator of another tenant gets 404 on every tenant route (" + provider + ")", ct => ForeignTenantAsync(provider, ct)));
                suites.Add(new TestSuiteDescriptor(id, "Authorization matrix (" + provider + ")", cases));
            }
            return suites;
        }

        #endregion

        #region Private-Methods

        private static Task BuiltInSemanticsAsync(CancellationToken token)
        {
            Assert.True(Allowed("TenantAdmin", ResourceTypeEnum.Secret, OperationTypeEnum.Reveal), "TenantAdmin reveals secrets");
            Assert.True(Allowed("TenantAdmin", ResourceTypeEnum.User, OperationTypeEnum.Delete), "TenantAdmin manages users");
            Assert.True(!Allowed("Builder", ResourceTypeEnum.Secret, OperationTypeEnum.Reveal), "Builder must not reveal");
            Assert.True(Allowed("Builder", ResourceTypeEnum.Secret, OperationTypeEnum.Create) && Allowed("Builder", ResourceTypeEnum.Secret, OperationTypeEnum.Delete), "Builder authors secrets");
            Assert.True(Allowed("Builder", ResourceTypeEnum.PromptTemplate, OperationTypeEnum.Update), "Builder edits prompt templates");
            Assert.True(!Allowed("Builder", ResourceTypeEnum.User, OperationTypeEnum.Read), "Builder does not read users");
            Assert.True(Allowed("Operator", ResourceTypeEnum.AgentProfile, OperationTypeEnum.Execute), "Operator executes profiles");
            Assert.True(Allowed("Operator", ResourceTypeEnum.Approval, OperationTypeEnum.Update), "Operator decides approvals");
            Assert.True(!Allowed("Operator", ResourceTypeEnum.Secret, OperationTypeEnum.Read), "Operator does not read secrets");
            Assert.True(!Allowed("Operator", ResourceTypeEnum.Resource, OperationTypeEnum.Create), "Operator does not create resources");
            Assert.True(Allowed("SecurityAdmin", ResourceTypeEnum.Credential, OperationTypeEnum.Create) && Allowed("SecurityAdmin", ResourceTypeEnum.Audit, OperationTypeEnum.Delete), "SecurityAdmin administers identity and audit");
            Assert.True(!Allowed("SecurityAdmin", ResourceTypeEnum.Secret, OperationTypeEnum.Read), "SecurityAdmin does not read secrets");
            Assert.True(Allowed("Auditor", ResourceTypeEnum.Audit, OperationTypeEnum.Read) && !Allowed("Auditor", ResourceTypeEnum.Audit, OperationTypeEnum.Delete), "Auditor reads audit only");
            Assert.True(Allowed("Viewer", ResourceTypeEnum.Secret, OperationTypeEnum.Read) && !Allowed("Viewer", ResourceTypeEnum.Secret, OperationTypeEnum.Reveal), "Viewer reads but never reveals");
            Assert.True(Allowed("Editor", ResourceTypeEnum.Resource, OperationTypeEnum.Delete) && !Allowed("Editor", ResourceTypeEnum.Secret, OperationTypeEnum.Reveal), "Editor writes but never reveals");
            Assert.True(!Allowed("ResourceAdmin", ResourceTypeEnum.Secret, OperationTypeEnum.Reveal), "ResourceAdmin All does not imply Reveal");
            Assert.True(Allowed("ProfileUser", ResourceTypeEnum.AgentProfile, OperationTypeEnum.Execute) && !Allowed("ProfileUser", ResourceTypeEnum.Resource, OperationTypeEnum.Read), "ProfileUser uses a profile only");
            Assert.True(Allowed("TenantMember", ResourceTypeEnum.Tenant, OperationTypeEnum.Read) && !Allowed("TenantMember", ResourceTypeEnum.User, OperationTypeEnum.Read), "TenantMember is minimal");
            Assert.True(!Allowed("Custom", ResourceTypeEnum.Tenant, OperationTypeEnum.Read), "Custom grants nothing");
            return Task.CompletedTask;
        }

        private static bool Allowed(string roleName, ResourceTypeEnum resourceType, OperationTypeEnum operation)
        {
            if (_PlatformOnly.Contains(resourceType)) return false;
            if (resourceType == ResourceTypeEnum.Tenant && (operation == OperationTypeEnum.Create || operation == OperationTypeEnum.Delete)) return false;
            BuiltInRole role = BuiltInRoles.Find(roleName) ?? throw new TestAssertionException("Unknown role " + roleName);
            List<PermissionGrant> grants = role.Permissions.Select(p =>
            {
                PermissionGrant grant = new PermissionGrant();
                grant.ResourceTypes = new List<string>(p.ResourceTypes);
                grant.Operations = new List<string>(p.Operations);
                grant.PermissionType = p.PermissionType;
                grant.Scope = ResourceScopeEnum.Tenant;
                grant.InheritsToChildren = true;
                grant.Source = role.Name;
                return grant;
            }).ToList();
            return PermissionEvaluator.Evaluate(grants, resourceType, operation, null).Permitted;
        }

        private static async Task MatrixAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                List<OperationScope> scopes = host.Server.Context.Scopes.All().Where(s => s.Authorization == RouteAuthorizationEnum.Scoped && s.ResourceType.HasValue && s.Operation.HasValue).ToList();
                Assert.True(scopes.Count > 50, "Too few scoped routes: " + scopes.Count);
                List<string> failures = new List<string>();
                foreach (BuiltInRole role in BuiltInRoles.All)
                {
                    string email = role.Name.ToLowerInvariant() + "@matrix.local";
                    User user = await HttpTestHelpers.SendAsync<User>(host.AdminClient, HttpMethod.Post, "/v1.0/api/tenants/" + host.TenantId + "/users", new UserRequest { Email = email, Password = "password123" }, 201, token).ConfigureAwait(false);
                    await HttpTestHelpers.SendAsync<UserRoleAssignment>(host.AdminClient, HttpMethod.Post, "/v1.0/api/tenants/" + host.TenantId + "/user-role-assignments", new AssignmentRequest { UserId = user.Id, RoleName = role.Name }, 201, token).ConfigureAwait(false);
                    // New users also hold TenantMember; remove it so each user holds exactly one role.
                    foreach (UserRoleAssignment assignment in await host.Server.Context.Database.UserRoleAssignments.ListByUserAsync(host.TenantId, user.Id, token).ConfigureAwait(false))
                    {
                        if (!String.Equals(assignment.RoleName, role.Name, StringComparison.Ordinal)) await host.Server.Context.Database.UserRoleAssignments.DeleteAsync(assignment.Id, token).ConfigureAwait(false);
                    }
                    host.Server.Context.Authorization.Invalidate();

                    using (HttpClient client = await host.UserClientAsync(email, "password123", host.TenantId, token).ConfigureAwait(false))
                    {
                        foreach (OperationScope scope in scopes)
                        {
                            bool expected = Allowed(role.Name, scope.ResourceType!.Value, scope.Operation!.Value);
                            string path = Concrete(scope.Path, host.TenantId);
                            TestResponse response = await HttpTestHelpers.SendAsync(client, new HttpMethod(scope.Method), path, scope.Method == "GET" || scope.Method == "DELETE" ? null : "{}", token).ConfigureAwait(false);
                            bool denied = response.StatusCode == 403 && response.Body.Contains("AuthorizationDenied", StringComparison.Ordinal);
                            if (denied == expected) failures.Add(role.Name + " " + scope.Method + " " + scope.Path + ": expected " + (expected ? "allowed" : "denied") + ", got " + response.StatusCode + " " + Truncate(response.Body));
                        }
                    }
                }
                Assert.True(failures.Count == 0, failures.Count + " matrix mismatches: " + String.Join(" | ", failures.Take(20)));
            }
        }

        private static async Task ForeignTenantAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                Tenant other = await HttpTestHelpers.SendAsync<Tenant>(host.AdminClient, HttpMethod.Post, "/v1.0/api/tenants", new TenantRequest { Name = "Foreign" }, 201, token).ConfigureAwait(false);
                await HttpTestHelpers.SendAsync<User>(host.AdminClient, HttpMethod.Post, "/v1.0/api/tenants/" + other.Id + "/users", new UserRequest { Email = "foreign@matrix.local", Password = "password123", IsTenantAdmin = true }, 201, token).ConfigureAwait(false);
                List<OperationScope> scopes = host.Server.Context.Scopes.All().Where(s => s.Path.Contains("{tenantId}", StringComparison.Ordinal) && s.Authorization != RouteAuthorizationEnum.Anonymous).ToList();
                List<string> failures = new List<string>();
                using (HttpClient client = await host.UserClientAsync("foreign@matrix.local", "password123", other.Id, token).ConfigureAwait(false))
                {
                    foreach (OperationScope scope in scopes)
                    {
                        string path = Concrete(scope.Path, host.TenantId);
                        TestResponse response = await HttpTestHelpers.SendAsync(client, new HttpMethod(scope.Method), path, scope.Method == "GET" || scope.Method == "DELETE" ? null : "{}", token).ConfigureAwait(false);
                        if (response.StatusCode != 404) failures.Add(scope.Method + " " + scope.Path + ": " + response.StatusCode + " " + Truncate(response.Body));
                    }
                }
                Assert.True(failures.Count == 0, failures.Count + " foreign-tenant routes did not answer 404: " + String.Join(" | ", failures.Take(20)));
                Assert.True(await host.Server.Context.Database.Tenants.ExistsAsync(host.TenantId, token).ConfigureAwait(false), "The default tenant survived the foreign calls.");
            }
        }

        private static string Concrete(string template, string tenantId)
        {
            return _Parameter.Replace(template, match =>
            {
                string name = match.Groups[1].Value;
                if (name == "tenantId") return tenantId;
                if (name == "version") return "1";
                return "missing_" + name;
            });
        }

        private static string Truncate(string text)
        {
            return text.Length <= 160 ? text : text.Substring(0, 160);
        }

        #endregion
    }
}
