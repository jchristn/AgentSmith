namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Responses;
    using Hugo.Server.Routes;
    using Hugo.Server.Services;
    using Hugo.Server.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using WatsonWebserver.Core;

    /// <summary>
    /// Server foundation: route contract and fail-closed mapping (P1.24), health and request identifiers (P1.5, P1.38),
    /// must-change-password enforcement (P1.16), token lifecycle and tenant lookup (P1.21), rate limiting (P1.22),
    /// settings environment overrides (P1.2), sessions (P1.28), and restart (P1.34).
    /// </summary>
    public static class FoundationSuite
    {
        /// <summary>
        /// Build the suites.
        /// </summary>
        /// <returns>Suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            List<TestSuiteDescriptor> suites = new List<TestSuiteDescriptor>();

            List<TestCaseDescriptor> unit = new List<TestCaseDescriptor>();
            unit.Add(new TestCaseDescriptor("foundation-unit", "settings-environment", "HUGO_* overrides apply with field paths and invalid values are rejected", SettingsEnvironmentAsync));
            unit.Add(new TestCaseDescriptor("foundation-unit", "settings-validation", "Settings clamp to documented ranges and reject unusable combinations", SettingsValidationAsync));
            suites.Add(new TestSuiteDescriptor("foundation-unit", "Foundation unit", unit));

            foreach (DatabaseTypeEnum provider in TestEnvironment.Providers())
            {
                string id = "foundation-" + provider.ToString().ToLowerInvariant();
                List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
                cases.Add(new TestCaseDescriptor(id, "route-contract", "Every Watson route has an operation scope and OpenAPI lists every route (" + provider + ")", ct => RouteContractAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "route-fail-closed", "A route registered outside the registrar fails closed to administrators (" + provider + ")", ct => FailClosedAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "health-and-request-id", "Health answers anonymously and every response carries x-request-id (" + provider + ")", ct => HealthAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "must-change-password", "Seeded users must change their password before using other routes (" + provider + ")", ct => MustChangePasswordAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "token-lifecycle", "Token details, refresh, logout, tenant lookup, and conflicting schemes (" + provider + ")", ct => TokenLifecycleAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "rate-limit", "Token routes answer 429 after the per-minute limit (" + provider + ")", ct => RateLimitAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "sessions", "Sessions are listed per tenant and revoking one ends its token (" + provider + ")", ct => SessionsAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "events", "The event stream delivers tenant events to that tenant only, with heartbeats (" + provider + ")", ct => EventsAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "restart", "Restart requests are accepted from administrators only (" + provider + ")", ct => RestartAsync(provider, ct)));
                suites.Add(new TestSuiteDescriptor(id, "Foundation over HTTP (" + provider + ")", cases));
            }
            return suites;
        }

        private static Task SettingsEnvironmentAsync(CancellationToken token)
        {
            Dictionary<string, string> env = new Dictionary<string, string>
            {
                { "HUGO_REST_PORT", "8899" },
                { "HUGO_REST_HOSTNAME", "0.0.0.0" },
                { "HUGO_DB_TYPE", "postgresql" },
                { "HUGO_DB_SERVER", "db.internal" },
                { "HUGO_DB_DATABASE", "hugo_prod" },
                { "HUGO_AUTH_SIGNING_KEY", "env-signing-key-0123456789" },
                { "HUGO_MASTER_KEY_VERSION", "3" },
                { "HUGO_ENABLE_PLUGINS", "true" }
            };
            HugoSettings settings = new HugoSettings();
            settings.ApplyEnvironment(name => env.TryGetValue(name, out string? value) ? value : null);
            Assert.Equal(8899, settings.Rest.Port, "Rest.Port");
            Assert.Equal("0.0.0.0", settings.Rest.Hostname, "Rest.Hostname");
            Assert.Equal(DatabaseTypeEnum.Postgresql, settings.Database.Type, "Database.Type");
            Assert.Equal("db.internal", settings.Database.Host, "Database.Host");
            Assert.Equal("hugo_prod", settings.Database.DatabaseName, "Database.DatabaseName");
            Assert.Equal(3, settings.Secrets.MasterKeyVersion, "Secrets.MasterKeyVersion");
            Assert.True(settings.Connectors.EnablePlugins, "Connectors.EnablePlugins");
            foreach (string path in new[] { "Rest.Port", "Rest.Hostname", "Database.Type", "Database.Host", "Auth.SigningKey", "Secrets.MasterKeyVersion", "Connectors.EnablePlugins" })
            {
                Assert.True(settings.EnvironmentOverrides.Contains(path), path + " not recorded as an environment override.");
            }

            HugoSettings defaults = new HugoSettings();
            Assert.Equal(8800, defaults.Rest.Port, "Default port");
            Assert.Equal("data/hugo.db", defaults.Database.Filename, "Default database file");
            Assert.Equal("127.0.0.1", defaults.Rest.Hostname, "Default hostname");

            bool threw = false;
            try
            {
                new HugoSettings().ApplyEnvironment(name => name == "HUGO_REST_PORT" ? "eighty" : null);
            }
            catch (InvalidOperationException e)
            {
                threw = e.Message.Contains("HUGO_REST_PORT", StringComparison.Ordinal) && e.Message.Contains("Rest.Port", StringComparison.Ordinal);
            }
            Assert.True(threw, "An invalid integer was accepted or the message lacks the variable and path.");
            return Task.CompletedTask;
        }

        private static Task SettingsValidationAsync(CancellationToken token)
        {
            HugoSettings settings = new HugoSettings();
            settings.Rest.Port = 70000;
            Assert.Equal(65535, settings.Rest.Port, "Port clamp");
            settings.Auth.PasswordHashIterations = 1000;
            Assert.Equal(600000, settings.Auth.PasswordHashIterations, "PBKDF2 iterations never below 600000 (D-009)");
            settings.Secrets.RevealReauthMinutes = 0;
            Assert.Equal(1, settings.Secrets.RevealReauthMinutes, "Reveal window minimum");
            settings.Auth.SigningKey = "short";
            bool threw = false;
            try
            {
                settings.Validate();
            }
            catch (InvalidOperationException e)
            {
                threw = e.Message.Contains("Auth.SigningKey", StringComparison.Ordinal);
            }
            Assert.True(threw, "A short signing key was accepted.");
            return Task.CompletedTask;
        }

        private static async Task RouteContractAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                List<string> unmapped = host.Server.Routes.UnmappedRoutes();
                Assert.True(unmapped.Count == 0, "Routes without an operation scope: " + String.Join(", ", unmapped));

                List<OperationScope> scopes = host.Server.Context.Scopes.All();
                foreach (OperationScope scope in scopes.Where(s => s.Authorization == RouteAuthorizationEnum.Scoped))
                {
                    Assert.True(scope.ResourceType.HasValue && scope.Operation.HasValue, scope.Method + " " + scope.Path + " has no resource type or operation.");
                }
                Assert.Equal(scopes.Count, host.Server.Routes.RegisteredRoutes().Count(r => !RouteRegistrar.IsDocumentationRoute(r.Value)), "Registered Watson routes match the scope map");

                using (HttpClient anonymous = new HttpClient())
                {
                    string document = await anonymous.GetStringAsync(host.BaseUrl + "/openapi.json", token).ConfigureAwait(false);
                    foreach (OperationScope scope in scopes)
                    {
                        Assert.True(document.Contains("\"" + scope.Path + "\"", StringComparison.Ordinal), "OpenAPI is missing " + scope.Path);
                    }
                }

                foreach (string path in new[] { "/v1.0/token", "/v1.0/token/reauthenticate", "/v1.0/tenants/lookup", "/v1.0/api/health", "/v1.0/api/info", "/v1.0/api/tenants/{tenantId}/secrets/{id}/reveal", "/v1.0/api/system/master-key/rotate", "/v1.0/api/tenants/{tenantId}/sessions", "/v1.0/api/users/me/password", "/v1.0/tenants/{tenantId}/users/{id}/permissions" })
                {
                    Assert.True(scopes.Any(s => s.Path == path), "Route " + path + " is not registered.");
                }
            }
        }

        private static async Task FailClosedAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                host.Server.Webserver.Routes.PostAuthentication.Static.Add(WatsonWebserver.Core.HttpMethod.GET, "/v1.0/api/unmapped-probe", async ctx =>
                {
                    ctx.Response.StatusCode = 200;
                    await ctx.Response.Send("ok").ConfigureAwait(false);
                });
                Assert.True(host.Server.Routes.UnmappedRoutes().Contains("GET /v1.0/api/unmapped-probe"), "The self-check did not report the unmapped route.");

                using (HttpClient anderson = await host.ActivateSeededUserAsync("anderson@hugo.local", token).ConfigureAwait(false))
                {
                    TestResponse denied = await HttpTestHelpers.SendAsync(anderson, System.Net.Http.HttpMethod.Get, "/v1.0/api/unmapped-probe", null, token).ConfigureAwait(false);
                    Assert.Equal(403, denied.StatusCode, "Unmapped route for a tenant administrator (" + denied.Body + ")");
                    Assert.True(denied.Body.Contains("RouteNotMapped", StringComparison.Ordinal), "Denial code.");
                }
                TestResponse admin = await HttpTestHelpers.SendAsync(host.AdminClient, System.Net.Http.HttpMethod.Get, "/v1.0/api/unmapped-probe", null, token).ConfigureAwait(false);
                Assert.Equal(200, admin.StatusCode, "Unmapped route for a system administrator");
            }
        }

        private static async Task HealthAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                using (HttpClient anonymous = new HttpClient())
                {
                    HttpResponseMessage response = await anonymous.GetAsync(host.BaseUrl + "/v1.0/api/health", token).ConfigureAwait(false);
                    Assert.Equal(200, (int)response.StatusCode, "Health status");
                    Assert.True(response.Headers.Contains("x-request-id"), "x-request-id missing on health.");
                    HealthResponse health = Assert.NotNull(Hugo.Core.Serialization.Serializer.Deserialize<HealthResponse>(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false)), "Health body");
                    Assert.Equal("healthy", health.Status, "Health");
                    Assert.Equal("ok", health.Database, "Database");
                    Assert.Equal("ok", health.Background, "Background services");
                    Assert.Equal(health.ExpectedSchemaVersion, health.SchemaVersion, "Schema version");

                    using (HttpRequestMessage request = new HttpRequestMessage(System.Net.Http.HttpMethod.Get, host.BaseUrl + "/v1.0/api/info"))
                    {
                        request.Headers.Add("x-request-id", "trace-me-123");
                        HttpResponseMessage info = await anonymous.SendAsync(request, token).ConfigureAwait(false);
                        Assert.Equal(401, (int)info.StatusCode, "Info requires authentication");
                        Assert.Equal("trace-me-123", info.Headers.GetValues("x-request-id").First(), "Caller request id echoed");
                        Assert.True((await info.Content.ReadAsStringAsync(token).ConfigureAwait(false)).Contains("trace-me-123", StringComparison.Ordinal), "Error body carries the request id.");
                    }
                }
                ServerInfoResponse server = await HttpTestHelpers.SendAsync<ServerInfoResponse>(host.AdminClient, System.Net.Http.HttpMethod.Get, "/v1.0/api/info", null, 200, token).ConfigureAwait(false);
                Assert.Equal("Hugo", server.Product, "Product");
                Assert.True(server.Features.Contains("secrets"), "Secrets feature listed.");
            }
        }

        private static async Task MustChangePasswordAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                TokenResponse first = await host.LoginAsync("anderson@hugo.local", "password", null, token).ConfigureAwait(false);
                Assert.True(first.MustChangePassword, "Seeded user not flagged at sign-in.");
                using (HttpClient client = await host.UserClientAsync("anderson@hugo.local", "password", null, token).ConfigureAwait(false))
                {
                    TestResponse blocked = await HttpTestHelpers.SendAsync(client, System.Net.Http.HttpMethod.Get, "/v1.0/api/tenants/" + host.TenantId + "/secrets", null, token).ConfigureAwait(false);
                    Assert.Equal(403, blocked.StatusCode, "Blocked before the password change");
                    Assert.True(blocked.Body.Contains("PasswordChangeRequired", StringComparison.Ordinal), "Blocked code.");
                    TokenDetailsResponse details = await HttpTestHelpers.SendAsync<TokenDetailsResponse>(client, System.Net.Http.HttpMethod.Get, "/v1.0/token/details", null, 200, token).ConfigureAwait(false);
                    Assert.True(details.MustChangePassword, "Token details flag.");
                    Assert.Equal(200, (await HttpTestHelpers.SendAsync(client, System.Net.Http.HttpMethod.Get, "/v1.0/api/users/me", null, token).ConfigureAwait(false)).StatusCode, "Me is allowed");

                    TestResponse wrong = await HttpTestHelpers.SendAsync(client, System.Net.Http.HttpMethod.Post, "/v1.0/api/users/me/password", new { currentPassword = "nope", newPassword = "Another-Password-1" }, token).ConfigureAwait(false);
                    Assert.Equal(400, wrong.StatusCode, "Wrong current password");
                    TestResponse same = await HttpTestHelpers.SendAsync(client, System.Net.Http.HttpMethod.Post, "/v1.0/api/users/me/password", new { currentPassword = "password", newPassword = "password" }, token).ConfigureAwait(false);
                    Assert.Equal(400, same.StatusCode, "Unchanged password");
                    TestResponse shortPassword = await HttpTestHelpers.SendAsync(client, System.Net.Http.HttpMethod.Post, "/v1.0/api/users/me/password", new { currentPassword = "password", newPassword = "short" }, token).ConfigureAwait(false);
                    Assert.Equal(400, shortPassword.StatusCode, "Short password");
                    User changed = await HttpTestHelpers.SendAsync<User>(client, System.Net.Http.HttpMethod.Post, "/v1.0/api/users/me/password", new { currentPassword = "password", newPassword = TestServerHost.ActivatedPassword }, 200, token).ConfigureAwait(false);
                    Assert.True(!changed.MustChangePassword, "Flag cleared.");
                    Assert.Equal(200, (await HttpTestHelpers.SendAsync(client, System.Net.Http.HttpMethod.Get, "/v1.0/api/tenants/" + host.TenantId + "/secrets", null, token).ConfigureAwait(false)).StatusCode, "Allowed after the change on the same session");
                }
                using (HttpClient anonymous = new HttpClient())
                {
                    anonymous.BaseAddress = new Uri(host.BaseUrl);
                    TestResponse old = await HttpTestHelpers.SendAsync(anonymous, System.Net.Http.HttpMethod.Post, "/v1.0/token", new { email = "anderson@hugo.local", password = "password" }, token).ConfigureAwait(false);
                    Assert.Equal(401, old.StatusCode, "Old password after the change");
                }
                TokenResponse after = await host.LoginAsync("anderson@hugo.local", TestServerHost.ActivatedPassword, null, token).ConfigureAwait(false);
                Assert.True(!after.MustChangePassword, "Flag after sign-in with the new password.");

                User admin = Assert.NotNull(await host.Server.Context.Database.Users.ReadByEmailAsync(host.TenantId, "admin@hugo.local", token).ConfigureAwait(false), "Seeded administrator");
                Assert.True(admin.IsAdmin && admin.MustChangePassword, "Seeded administrator flags.");
                User anderson = Assert.NotNull(await host.Server.Context.Database.Users.ReadByEmailAsync(host.TenantId, "anderson@hugo.local", token).ConfigureAwait(false), "Anderson");
                User reset = await HttpTestHelpers.SendAsync<User>(host.AdminClient, System.Net.Http.HttpMethod.Post, "/v1.0/api/tenants/" + host.TenantId + "/users/" + anderson.Id + "/password", new { newPassword = "Reset-By-Admin-1" }, 200, token).ConfigureAwait(false);
                Assert.True(reset.MustChangePassword, "An administrator reset sets the flag again.");
            }
        }

        private static async Task TokenLifecycleAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                using (HttpClient client = await host.ActivateSeededUserAsync("anderson@hugo.local", token).ConfigureAwait(false))
                {
                    TokenDetailsResponse details = await HttpTestHelpers.SendAsync<TokenDetailsResponse>(client, System.Net.Http.HttpMethod.Get, "/v1.0/token/details", null, 200, token).ConfigureAwait(false);
                    Assert.Equal("anderson@hugo.local", details.Email, "Email");
                    Assert.True(details.IsTenantAdmin && !details.IsAdmin, "Anderson is a tenant administrator.");
                    Assert.Equal("Thomas", details.FirstName, "First name");

                    TokenResponse refreshed = await HttpTestHelpers.SendAsync<TokenResponse>(client, System.Net.Http.HttpMethod.Post, "/v1.0/token/refresh", null, 200, token).ConfigureAwait(false);
                    Assert.True(refreshed.ExpiresUtc > DateTime.UtcNow.AddMinutes(30), "Refreshed expiry.");
                    using (HttpClient second = new HttpClient())
                    {
                        second.BaseAddress = new Uri(host.BaseUrl);
                        second.DefaultRequestHeaders.Add("x-token", refreshed.Token);
                        Assert.Equal(200, (await HttpTestHelpers.SendAsync(second, System.Net.Http.HttpMethod.Get, "/v1.0/token", null, token).ConfigureAwait(false)).StatusCode, "x-token scheme");
                        second.DefaultRequestHeaders.Add("Authorization", "Bearer something-else");
                        Assert.Equal(401, (await HttpTestHelpers.SendAsync(second, System.Net.Http.HttpMethod.Get, "/v1.0/token", null, token).ConfigureAwait(false)).StatusCode, "Disagreeing Authorization and x-token");
                    }

                    using (HttpClient conflicting = new HttpClient())
                    {
                        conflicting.BaseAddress = new Uri(host.BaseUrl);
                        conflicting.DefaultRequestHeaders.Add("x-token", refreshed.Token);
                        conflicting.DefaultRequestHeaders.Add("x-api-key", TestServerHost.AdminApiKey);
                        Assert.Equal(401, (await HttpTestHelpers.SendAsync(conflicting, System.Net.Http.HttpMethod.Get, "/v1.0/token", null, token).ConfigureAwait(false)).StatusCode, "Two schemes at once");
                    }

                    using (HttpClient wrongTenant = new HttpClient())
                    {
                        wrongTenant.BaseAddress = new Uri(host.BaseUrl);
                        wrongTenant.DefaultRequestHeaders.Add("Authorization", "Bearer " + refreshed.Token);
                        wrongTenant.DefaultRequestHeaders.Add("x-tenant-guid", "ten_other");
                        Assert.Equal(401, (await HttpTestHelpers.SendAsync(wrongTenant, System.Net.Http.HttpMethod.Get, "/v1.0/token", null, token).ConfigureAwait(false)).StatusCode, "Tenant header disagreeing with the session");
                    }

                    Assert.Equal(200, (await HttpTestHelpers.SendAsync(client, System.Net.Http.HttpMethod.Delete, "/v1.0/token", null, token).ConfigureAwait(false)).StatusCode, "Logout");
                    Assert.Equal(401, (await HttpTestHelpers.SendAsync(client, System.Net.Http.HttpMethod.Get, "/v1.0/token/details", null, token).ConfigureAwait(false)).StatusCode, "Token after logout");
                }

                using (HttpClient anonymous = new HttpClient())
                {
                    anonymous.BaseAddress = new Uri(host.BaseUrl);
                    List<TenantLookupEntry> found = await HttpTestHelpers.SendAsync<List<TenantLookupEntry>>(anonymous, System.Net.Http.HttpMethod.Post, "/v1.0/tenants/lookup", new { email = "anderson@hugo.local" }, 200, token).ConfigureAwait(false);
                    Assert.True(found.Count == 1 && found[0].TenantId == host.TenantId, "Tenant lookup.");
                    List<TenantLookupEntry> none = await HttpTestHelpers.SendAsync<List<TenantLookupEntry>>(anonymous, System.Net.Http.HttpMethod.Post, "/v1.0/tenants/lookup", new { email = "nobody@hugo.local" }, 200, token).ConfigureAwait(false);
                    Assert.Equal(0, none.Count, "Unknown email lookup");
                    Assert.Equal(401, (await HttpTestHelpers.SendAsync(anonymous, System.Net.Http.HttpMethod.Post, "/v1.0/token", new { email = "anderson@hugo.local", password = "wrong" }, token).ConfigureAwait(false)).StatusCode, "Wrong password");
                }

                AuditPage failures = await HttpTestHelpers.SendAsync<AuditPage>(host.AdminClient, System.Net.Http.HttpMethod.Get, "/v1.0/api/tenants/" + host.TenantId + "/audit?eventType=SessionIssued", null, 200, token).ConfigureAwait(false);
                Assert.True(failures.TotalCount >= 1, "Session issuance audited.");
            }
        }

        private static async Task RateLimitAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, s => s.Auth.RateLimitPerMinute = 3, token).ConfigureAwait(false))
            {
                using (HttpClient anonymous = new HttpClient())
                {
                    anonymous.BaseAddress = new Uri(host.BaseUrl);
                    List<int> statuses = new List<int>();
                    for (int i = 0; i < 5; i++)
                    {
                        statuses.Add((await HttpTestHelpers.SendAsync(anonymous, System.Net.Http.HttpMethod.Post, "/v1.0/token", new { email = "anderson@hugo.local", password = "wrong" }, token).ConfigureAwait(false)).StatusCode);
                    }
                    Assert.True(statuses.Take(3).All(s => s == 401), "First attempts: " + String.Join(",", statuses));
                    Assert.True(statuses.Skip(3).All(s => s == 429), "Later attempts are rate limited: " + String.Join(",", statuses));
                }
            }
        }

        private static async Task SessionsAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                using (HttpClient anderson = await host.ActivateSeededUserAsync("anderson@hugo.local", token).ConfigureAwait(false))
                {
                    TokenDetailsResponse details = await HttpTestHelpers.SendAsync<TokenDetailsResponse>(anderson, System.Net.Http.HttpMethod.Get, "/v1.0/token/details", null, 200, token).ConfigureAwait(false);
                    EnumerationResult<AuthSession> sessions = await HttpTestHelpers.SendAsync<EnumerationResult<AuthSession>>(host.AdminClient, System.Net.Http.HttpMethod.Get, "/v1.0/api/tenants/" + host.TenantId + "/sessions?maxResults=100", null, 200, token).ConfigureAwait(false);
                    Assert.True(sessions.Objects.Any(s => s.Id == details.SessionId), "Session listed.");
                    TestResponse raw = await HttpTestHelpers.SendAsync(host.AdminClient, System.Net.Http.HttpMethod.Get, "/v1.0/api/tenants/" + host.TenantId + "/sessions/" + details.SessionId, null, token).ConfigureAwait(false);
                    Assert.True(!raw.Body.Contains("tokenId", StringComparison.OrdinalIgnoreCase), "Session token identifier exposed.");
                    Assert.Equal(204, (await HttpTestHelpers.SendAsync(host.AdminClient, System.Net.Http.HttpMethod.Delete, "/v1.0/api/tenants/" + host.TenantId + "/sessions/" + details.SessionId, null, token).ConfigureAwait(false)).StatusCode, "Revoke");
                    Assert.Equal(401, (await HttpTestHelpers.SendAsync(anderson, System.Net.Http.HttpMethod.Get, "/v1.0/token/details", null, token).ConfigureAwait(false)).StatusCode, "Revoked session");
                }
            }
        }

        private static async Task EventsAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                host.Server.Context.Get<EventHub>().HeartbeatSeconds = 1;
                Tenant other = await HttpTestHelpers.SendAsync<Tenant>(host.AdminClient, System.Net.Http.HttpMethod.Post, "/v1.0/api/tenants", new Hugo.Core.Requests.TenantRequest { Name = "Events Other" }, 201, token).ConfigureAwait(false);
                await HttpTestHelpers.SendAsync<User>(host.AdminClient, System.Net.Http.HttpMethod.Post, "/v1.0/api/tenants/" + other.Id + "/users", new Hugo.Core.Requests.UserRequest { Email = "events@other.local", Password = "password123", IsTenantAdmin = true }, 201, token).ConfigureAwait(false);

                using (HttpClient anderson = await host.ActivateSeededUserAsync("anderson@hugo.local", token).ConfigureAwait(false))
                using (HttpClient stranger = await host.UserClientAsync("events@other.local", "password123", other.Id, token).ConfigureAwait(false))
                using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(30));
                    using (HttpResponseMessage own = await anderson.GetAsync("/v1.0/api/events", HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                    using (HttpResponseMessage foreign = await stranger.GetAsync("/v1.0/api/events", HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                    {
                        Assert.Equal(200, (int)own.StatusCode, "Stream status");
                        Assert.Equal("text/event-stream", own.Content.Headers.ContentType?.MediaType, "Content type");
                        using (System.IO.StreamReader ownReader = new System.IO.StreamReader(await own.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false)))
                        using (System.IO.StreamReader foreignReader = new System.IO.StreamReader(await foreign.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false)))
                        {
                            Assert.True(await ReadUntilAsync(ownReader, "event: stream.opened", timeout.Token).ConfigureAwait(false), "Own stream did not open.");
                            Assert.True(await ReadUntilAsync(foreignReader, "event: stream.opened", timeout.Token).ConfigureAwait(false), "Foreign stream did not open.");
                            await HttpTestHelpers.SendAsync<Secret>(anderson, System.Net.Http.HttpMethod.Post, "/v1.0/api/tenants/" + host.TenantId + "/secrets", new { name = "evented", kind = "ApiToken", values = new { token = "evented-token-value" } }, 201, timeout.Token).ConfigureAwait(false);
                            List<string> ownLines = await ReadLinesUntilAsync(ownReader, "secret.created", timeout.Token).ConfigureAwait(false);
                            Assert.True(ownLines.Any(l => l.Contains("secret.created", StringComparison.Ordinal)), "secret.created not delivered.");
                            Assert.True(ownLines.All(l => !l.Contains("evented-token-value", StringComparison.Ordinal)), "The event carried the secret value.");
                            List<string> foreignLines = await ReadLinesUntilAsync(foreignReader, "event: heartbeat", timeout.Token).ConfigureAwait(false);
                            await ReadLinesUntilAsync(foreignReader, "event: heartbeat", timeout.Token).ConfigureAwait(false);
                            Assert.True(foreignLines.All(l => !l.Contains("secret.created", StringComparison.Ordinal)), "Another tenant received the event.");
                        }
                    }
                }
            }
        }

        private static async Task<bool> ReadUntilAsync(System.IO.StreamReader reader, string marker, CancellationToken token)
        {
            return (await ReadLinesUntilAsync(reader, marker, token).ConfigureAwait(false)).Any(l => l.Contains(marker, StringComparison.Ordinal));
        }

        private static async Task<List<string>> ReadLinesUntilAsync(System.IO.StreamReader reader, string marker, CancellationToken token)
        {
            List<string> lines = new List<string>();
            while (!token.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                if (line == null) break;
                lines.Add(line);
                if (line.Contains(marker, StringComparison.Ordinal)) break;
            }
            return lines;
        }

        private static async Task RestartAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (TestServerHost host = await TestServerHost.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                using (HttpClient anderson = await host.ActivateSeededUserAsync("anderson@hugo.local", token).ConfigureAwait(false))
                {
                    Assert.Equal(403, (await HttpTestHelpers.SendAsync(anderson, System.Net.Http.HttpMethod.Post, "/v1.0/api/system/restart", null, token).ConfigureAwait(false)).StatusCode, "Tenant administrators cannot restart the server");
                    Assert.Equal(403, (await HttpTestHelpers.SendAsync(anderson, System.Net.Http.HttpMethod.Get, "/v1.0/api/settings", null, token).ConfigureAwait(false)).StatusCode, "Tenant administrators cannot read server settings");
                }
                RestartResponse restart = await HttpTestHelpers.SendAsync<RestartResponse>(host.AdminClient, System.Net.Http.HttpMethod.Post, "/v1.0/api/system/restart", null, 202, token).ConfigureAwait(false);
                Assert.True(restart.Restarting, "Restart accepted.");
                Assert.True(RestartCoordinator.RestartRequested && RestartCoordinator.ExitCode == RestartCoordinator.RestartExitCode, "Exit code 75 recorded.");
                Hugo.Core.Requests.AuditFilter filter = new Hugo.Core.Requests.AuditFilter();
                filter.EventType = AuditEventTypeEnum.RestartRequested;
                Assert.True((await host.Server.Context.Database.Audit.EnumerateAsync(filter, token).ConfigureAwait(false)).TotalCount >= 1, "Restart request audited.");
            }
        }
    }
}
