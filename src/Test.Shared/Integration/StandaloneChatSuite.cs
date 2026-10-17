namespace Test.Shared.Integration
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.IO;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Database;
    using Hugo.Core.Database.Ado;
    using Hugo.Core.Enums;
    using Hugo.Core.Instructions;
    using Hugo.Server.Services;
    using Hugo.Core.Models;
    using Hugo.Core.Prompts;
    using Hugo.Core.Responses;
    using Test.Shared.Agent.StubLlm;
    using Test.Shared.Chat;
    using Test.Shared.Infrastructure;
    using Test.Shared.Integration.Mcp;
    using Touchstone.Core;

    /// <summary>
    /// Standalone chat capability, session isolation, publisher configuration, and normal runtime permissions.
    /// Uses real HTTP routes and deterministic model responses without external model or target connections.
    /// </summary>
    public static class StandaloneChatSuite
    {
        /// <summary>Build standalone chat cases for each persistence provider.</summary>
        /// <returns>Provider suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            List<TestSuiteDescriptor> suites = new List<TestSuiteDescriptor>();
            foreach (DatabaseTypeEnum provider in TestEnvironment.Providers())
            {
                string id = "standalone-chat-" + provider.ToString().ToLowerInvariant();
                suites.Add(new TestSuiteDescriptor(id, "Standalone chat (" + provider + ")", new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(id, "migration-upgrade", "Version 9 profiles upgrade with disabled authenticated chat while preserving existing profile data", ct => MigrationAsync(provider, ct)),
                    new TestCaseDescriptor(id, "disabled-management", "Standalone chat defaults disabled and only profile editors can publish or rotate links", ct => ManagementAsync(provider, ct)),
                    new TestCaseDescriptor(id, "publication-permissions", "Public publication requires explicit profile execution and conversation grants even for administrator-owned credentials", ct => PublicationPermissionsAsync(provider, ct)),
                    new TestCaseDescriptor(id, "public-runtime", "Public visitors execute through the configured credential and normal binding permissions without exposing traces", ct => PublicRuntimeAsync(provider, ct)),
                    new TestCaseDescriptor(id, "visitor-isolation", "Conversation capabilities isolate anonymous visitors, profiles, and tampered session tokens", ct => IsolationAsync(provider, ct)),
                    new TestCaseDescriptor(id, "public-approval-revocation", "Deferred public approvals revalidate publication authority before executing", ct => ApprovalRevocationAsync(provider, ct)),
                    new TestCaseDescriptor(id, "revocation", "Disable, rotation, mode changes and credential revocation invalidate existing visitor sessions", ct => RevocationAsync(provider, ct)),
                    new TestCaseDescriptor(id, "authenticated-access", "Authenticated links require the visitor's own login and profile Execute rights", ct => AuthenticatedAsync(provider, ct)),
                    new TestCaseDescriptor(id, "inference-failure", "Failed inference returns a safe response and keeps public message history isolated", ct => FailureAsync(provider, ct))
                }));
            }
            return suites;
        }

        private static HttpClient Anonymous(IntegrationRig rig)
        {
            return new HttpClient { BaseAddress = new Uri(rig.Host.BaseUrl) };
        }

        private static string Chat(AgentProfile profile)
        {
            return "/v1.0/chat/" + profile.ChatLinkId;
        }

        private static async Task<JsonElement> JsonAsync(HttpClient client, HttpMethod method, string path, object? body, int status, CancellationToken token)
        {
            TestResponse response = await HttpTestHelpers.SendAsync(client, method, path, body, token).ConfigureAwait(false);
            Assert.Equal(status, response.StatusCode, method + " " + path + ": " + response.Body);
            using (JsonDocument json = JsonDocument.Parse(response.Body)) return json.RootElement.Clone();
        }

        private static async Task<AgentProfile> PublishAsync(IntegrationRig rig, StubLlmServer? stub, string mode, CancellationToken token, string role = "Operator")
        {
            CredentialCreateResponse credential = await McpFixture.CredentialAsync(rig, "standalone-credential", rig.Hugo.Id, role, token).ConfigureAwait(false);
            string? model = null;
            if (stub != null)
            {
                ModelEndpoint endpoint = await HttpTestHelpers.SendAsync<ModelEndpoint>(rig.Anderson, HttpMethod.Post, "/v1.0/api/model-endpoints", new { name = "standalone-model", provider = "OpenAICompatible", baseUrl = stub.BaseUrl, model = stub.ModelName, maxRetries = 0 }, 201, token).ConfigureAwait(false);
                model = endpoint.Id;
            }
            Resource resource = await rig.ResourceAsync("standalone-echo", token).ConfigureAwait(false);
            await rig.BindAsync(rig.Hugo.Id, resource.Id, AccessModeEnum.ReadOnly, token, request => request.AllowKnowledgeUpdates = true).ConfigureAwait(false);
            return await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + rig.Hugo.Id, new { chatEnabled = true, chatAccessMode = mode, chatCredentialId = credential.Credential.Id, inferenceEndpointId = model, maxIterations = 8 }, 200, token).ConfigureAwait(false);
        }

        private static async Task<(string Id, string Token)> SessionAsync(HttpClient client, AgentProfile profile, CancellationToken token)
        {
            JsonElement session = await JsonAsync(client, HttpMethod.Post, Chat(profile) + "/conversations", new { }, 201, token).ConfigureAwait(false);
            return (session.GetProperty("id").GetString()!, session.GetProperty("sessionToken").GetString()!);
        }

        private static void Session(HttpClient client, string token)
        {
            client.DefaultRequestHeaders.Remove("x-hugo-chat-session");
            client.DefaultRequestHeaders.Add("x-hugo-chat-session", token);
        }

        private static async Task MigrationAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            DatabaseSettings settings = TestEnvironment.IsolatedSettings(provider);
            await using (DatabaseDriverBase database = await DatabaseDriverFactory.CreateAndInitializeAsync(settings, token).ConfigureAwait(false))
            {
                AdoDatabaseDriver driver = (AdoDatabaseDriver)database;
                try
                {
                    Tenant tenant = new Tenant { Name = "standalone-upgrade" };
                    await database.Tenants.CreateAsync(tenant, token).ConfigureAwait(false);
                    AgentProfile profile = new AgentProfile { TenantId = tenant.Id, Name = "legacy-chat-agent", InstructionsAppend = "Preserved operator guidance." };
                    await database.AgentProfiles.CreateAsync(profile, token).ConfigureAwait(false);
                    List<SchemaMigration> migrations = SchemaBuilder.Build(driver.Catalog, driver.Dialect).Where(item => item.Version >= 10).ToList();
                    string schema = String.IsNullOrEmpty(driver.Catalog.Schema) ? String.Empty : driver.Dialect.Quote(driver.Catalog.Schema!) + ".";
                    string index = schema + driver.Dialect.Quote("idx_" + driver.Catalog.RawName("agent_profiles") + "_chat_link");
                    await driver.NonQueryAsync("DROP INDEX " + index + ";", null, token).ConfigureAwait(false);
                    foreach (SchemaColumnAddition column in migrations.SelectMany(item => item.Columns))
                        await driver.NonQueryAsync("ALTER TABLE " + driver.Catalog.Reference(column.Table) + " DROP COLUMN " + driver.Dialect.Quote(column.Column) + ";", null, token).ConfigureAwait(false);
                    await driver.NonQueryAsync("DELETE FROM " + driver.Catalog.Reference("schema_migrations") + " WHERE version >= 10;", null, token).ConfigureAwait(false);
                    Assert.Equal(9, await driver.ReadSchemaVersionAsync(token).ConfigureAwait(false), "fixture is at the pre-chat schema version before migration");
                    await driver.InitializeAsync(token).ConfigureAwait(false);
                    AgentProfile upgraded = (await database.AgentProfiles.ReadAsync(tenant.Id, profile.Id, token).ConfigureAwait(false))!;
                    Assert.True(!upgraded.ChatEnabled && upgraded.ChatAccessMode.ToString() == "Authenticated", "upgraded existing profiles cannot accidentally become public");
                    Assert.True(String.IsNullOrEmpty(upgraded.ChatCredentialId) && String.IsNullOrEmpty(upgraded.ChatLinkId), "migration does not bind credentials or mint share links for existing profiles");
                    Assert.Equal("Preserved operator guidance.", upgraded.InstructionsAppend, "upgrade preserves operator instructions");
                    Assert.Equal(profile.Name, upgraded.Name, "upgrade preserves existing profile identity");
                    Assert.Equal(11, await driver.ReadSchemaVersionAsync(token).ConfigureAwait(false), "migration is recorded durably");
                }
                finally
                {
                    if (!String.IsNullOrEmpty(driver.Catalog.Schema))
                        await driver.NonQueryAsync("DROP SCHEMA " + driver.Dialect.Quote(driver.Catalog.Schema!) + " CASCADE;", null, token).ConfigureAwait(false);
                }
            }
            if (provider == DatabaseTypeEnum.Sqlite)
                foreach (string suffix in new[] { "", "-wal", "-shm" })
                    if (File.Exists(settings.Filename + suffix)) File.Delete(settings.Filename + suffix);
        }

        private static async Task ManagementAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            using (HttpClient visitor = Anonymous(rig))
            {
                Assert.True(rig.Context.Database.SchemaVersion >= 10, "standalone migration is installed");
                Assert.True(!rig.Hugo.ChatEnabled, "existing seeded profiles remain unpublished by default");
                AgentProfile fresh = await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Post, "/v1.0/api/agent-profiles", new { name = "unpublished-chat" }, 201, token).ConfigureAwait(false);
                Assert.True(!fresh.ChatEnabled, "new profiles are unpublished by default");
                TestResponse disabled = await HttpTestHelpers.SendAsync(visitor, HttpMethod.Get, "/v1.0/chat/" + (fresh.ChatLinkId ?? "missing-link"), null, token).ConfigureAwait(false);
                Assert.Equal(404, disabled.StatusCode, "unpublished profiles have no public capability");
                using (HttpClient op = await ChatTestSupport.UserAsync(rig, "chat-operator@fixture.local", "Operator", token).ConfigureAwait(false))
                {
                    TestResponse denied = await HttpTestHelpers.SendAsync(op, HttpMethod.Put, "/v1.0/api/agent-profiles/" + rig.Hugo.Id, new { chatEnabled = true, chatAccessMode = "Authenticated", rotateChatLink = true }, token).ConfigureAwait(false);
                    Assert.Equal(403, denied.StatusCode, "Execute-only user cannot publish or rotate another agent");
                }
                AgentProfile published = await PublishAsync(rig, null, "Public", token).ConfigureAwait(false);
                Assert.True(!String.IsNullOrWhiteSpace(published.ChatLinkId) && published.ChatLinkId != published.Id, "published link is an opaque capability rather than the profile ID");
                AgentProfile copy = await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Post, "/v1.0/api/agent-profiles/" + published.Id + "/duplicate", new { name = "unpublished-chat-copy" }, 201, token).ConfigureAwait(false);
                Assert.True(!copy.ChatEnabled && copy.ChatAccessMode == ChatAccessModeEnum.Authenticated && String.IsNullOrEmpty(copy.ChatCredentialId) && copy.ChatLinkId != published.ChatLinkId, "duplicating a published profile resets publication, credentials and link without changing the source");
                AgentProfile original = (await rig.Context.Database.AgentProfiles.ReadAsync(rig.TenantId, published.Id, token).ConfigureAwait(false))!;
                Assert.True(original.ChatEnabled && original.ChatLinkId == published.ChatLinkId, "the original published agent remains available after duplication");
                JsonElement metadata = await JsonAsync(visitor, HttpMethod.Get, Chat(published), null, 200, token).ConfigureAwait(false);
                Assert.True(metadata.EnumerateObject().All(property => new[] { "accessMode", "authenticationRequired", "displayName", "name", "title", "logoUrl", "pageTitle" }.Contains(property.Name)) && metadata.TryGetProperty("name", out JsonElement _) && metadata.TryGetProperty("accessMode", out JsonElement _), "metadata exposes only safe presentation fields (nullable displayName may be omitted)");
                Assert.True(!metadata.GetProperty("authenticationRequired").GetBoolean(), "public page needs no Hugo login");
                published = await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + published.Id, new { chatTitle = "Visitor heading", chatLogoUrl = "/brand/logo.svg", chatPageTitle = "Visitor browser title" }, 200, token).ConfigureAwait(false);
                metadata = await JsonAsync(visitor, HttpMethod.Get, Chat(published), null, 200, token).ConfigureAwait(false);
                Assert.Equal("Visitor heading", metadata.GetProperty("title").GetString(), "configured heading persists and is safely exposed");
                Assert.Equal("/brand/logo.svg", metadata.GetProperty("logoUrl").GetString(), "configured logo persists");
                Assert.Equal("Visitor browser title", metadata.GetProperty("pageTitle").GetString(), "configured browser title persists");
                foreach (string unsafeLogo in new[] { "javascript:alert(1)", "//foreign.invalid/logo", "/\\foreign.invalid/logo", "data:image/svg+xml,unsafe" })
                    Assert.Equal(400, (await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + published.Id, new { chatLogoUrl = unsafeLogo }, token).ConfigureAwait(false)).StatusCode, "unsafe logo schemes and network paths are rejected");
                published = await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + published.Id, new { chatTitle = "", chatLogoUrl = "", chatPageTitle = "" }, 200, token).ConfigureAwait(false);
                metadata = await JsonAsync(visitor, HttpMethod.Get, Chat(published), null, 200, token).ConfigureAwait(false);
                Assert.Equal(published.DisplayName ?? published.Name, metadata.GetProperty("title").GetString(), "empty settings restore default heading");
            }
        }

        private static async Task PublicationPermissionsAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            using (HttpClient visitor = Anonymous(rig))
            {
                CredentialCreateResponse empty = await McpFixture.CredentialAsync(rig, "unscoped-publisher", rig.Hugo.Id, null, token).ConfigureAwait(false);
                Credential stored = empty.Credential;
                stored.InheritOwnerBypass = true;
                await rig.Context.Database.Credentials.UpdateAsync(stored, token).ConfigureAwait(false);
                string path = "/v1.0/api/agent-profiles/" + rig.Hugo.Id;
                TestResponse missingExecute = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Put, path, new { chatEnabled = true, chatAccessMode = "Public", chatCredentialId = stored.Id }, token).ConfigureAwait(false);
                Assert.Equal(400, missingExecute.StatusCode, "administrator-owned credential cannot publish using inherited bypass");
                Assert.True(missingExecute.Body.Contains("chatCredentialId") && missingExecute.Body.Contains("Execute"), "publisher receives a field-specific missing execution permission");
                AgentProfile unchanged = (await rig.Context.Database.AgentProfiles.ReadAsync(rig.TenantId, rig.Hugo.Id, token).ConfigureAwait(false))!;
                Assert.True(!unchanged.ChatEnabled, "rejected publication does not persist an enabled link");

                Role role = new Role { TenantId = rig.TenantId, Name = "StandalonePublisher" };
                await rig.Context.Database.Roles.CreateAsync(role, token).ConfigureAwait(false);
                Permission execute = new Permission { TenantId = rig.TenantId, Name = "StandaloneExecute", ResourceTypes = new List<string> { "AgentProfile" }, OperationTypes = new List<string> { "Execute" } };
                await rig.Context.Database.Permissions.CreateAsync(execute, token).ConfigureAwait(false);
                await rig.Context.Database.RolePermissionMaps.CreateAsync(new RolePermissionMap { TenantId = rig.TenantId, RoleId = role.Id, PermissionId = execute.Id }, token).ConfigureAwait(false);
                CredentialCreateResponse scoped = await McpFixture.CredentialAsync(rig, "scoped-publisher", rig.Hugo.Id, role.Name, token).ConfigureAwait(false);
                Permission conversations = new Permission { TenantId = rig.TenantId, Name = "StandaloneConversations", ResourceTypes = new List<string> { "Conversation" }, OperationTypes = new List<string> { "Create", "Read" } };
                await rig.Context.Database.Permissions.CreateAsync(conversations, token).ConfigureAwait(false);
                await rig.Context.Database.RolePermissionMaps.CreateAsync(new RolePermissionMap { TenantId = rig.TenantId, RoleId = role.Id, PermissionId = conversations.Id }, token).ConfigureAwait(false);
                TestResponse missingUpdate = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Put, path, new { chatEnabled = true, chatAccessMode = "Public", chatCredentialId = scoped.Credential.Id }, token).ConfigureAwait(false);
                Assert.Equal(400, missingUpdate.StatusCode, "profile Execute and partial conversation grants are insufficient");
                Assert.True(missingUpdate.Body.Contains("chatCredentialId") && missingUpdate.Body.Contains("Conversation"), "publisher receives conversation permission guidance");
                conversations.OperationTypes.Add("Update");
                await rig.Context.Database.Permissions.UpdateAsync(conversations, token).ConfigureAwait(false);
                rig.Context.Authorization.Invalidate();
                AgentProfile published = await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Put, path, new { chatEnabled = true, chatAccessMode = "Public", chatCredentialId = scoped.Credential.Id }, 200, token).ConfigureAwait(false);
                await JsonAsync(visitor, HttpMethod.Get, Chat(published), null, 200, token).ConfigureAwait(false);
                (string id, string sessionToken) = await SessionAsync(visitor, published, token).ConfigureAwait(false);
                Session(visitor, sessionToken);
                await JsonAsync(visitor, HttpMethod.Get, Chat(published) + "/conversations/" + id + "/messages", null, 200, token).ConfigureAwait(false);
                stored = (await rig.Context.Database.Credentials.ReadAsync(rig.TenantId, scoped.Credential.Id, token).ConfigureAwait(false))!;
                stored.Active = false;
                await rig.Context.Database.Credentials.UpdateAsync(stored, token).ConfigureAwait(false);
                AgentProfile disabled = await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Put, path, new { chatEnabled = false }, 200, token).ConfigureAwait(false);
                Assert.True(!disabled.ChatEnabled, "broken publications can still be disabled after credential revocation");
            }
        }

        private static async Task PublicRuntimeAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (StubLlmServer stub = new StubLlmServer())
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, settings => settings.Agent.ToolRetrievalThreshold = 1000, token).ConfigureAwait(false))
            using (HttpClient visitor = Anonymous(rig))
            {
                AgentProfile profile = await PublishAsync(rig, stub, "Public", token, "TenantAdmin").ConfigureAwait(false);
                await HttpTestHelpers.SendAsync<PromptTemplateView>(rig.Anderson, HttpMethod.Put, "/v1.0/api/prompt-templates/persona.agent-smith", new { content = "Cache identity fixture: {{user.email}} {{user.firstName}}. You are {{profile.personaName}}." }, 200, token).ConfigureAwait(false);
                CompiledInstructions ownerCompiled = await HttpTestHelpers.SendAsync<CompiledInstructions>(rig.Anderson, HttpMethod.Get, "/v1.0/api/agent-profiles/" + profile.Id + "/compiled-instructions?surface=Chat", null, 200, token).ConfigureAwait(false);
                Assert.True(ownerCompiled.Text.Contains("anderson@hugo.local", StringComparison.Ordinal), "fixture primes the owner's personalized compiler cache before public chat");
                int planning = 0;
                stub.Script.Responder = request =>
                {
                    if (request.Messages.Any(message => message.Content?.Contains("Write a short title", StringComparison.Ordinal) == true)) return StubLlmResponse.Message("Visitor chat");
                    string system = String.Join("\n", request.Messages.Where(message => message.Role == "system").Select(message => message.Content));
                    Assert.True(system.Contains("Cache identity fixture:", StringComparison.Ordinal) && !system.Contains("anderson@hugo.local", StringComparison.Ordinal), "public compiler context cannot reuse the owner's cached personal template values");
                    Assert.True(!request.ToolNames.Contains("agent_update_instructions") && !request.ToolNames.Contains("resource_update_context"), "public chat cannot mutate persistent agent instructions or resource context");
                    Assert.True(!request.ToolNames.Any(name => name.StartsWith("hugo_admin_", StringComparison.Ordinal)), "public chat does not expose management tools");
                    int step = Interlocked.Increment(ref planning);
                    if (step == 1) return StubLlmResponse.ToolCall("agent_update_instructions", "{\"content\":\"Untrusted visitor persistent instruction.\"}");
                    if (step == 2) return StubLlmResponse.ToolCall("resource_update_context", "{\"resource\":\"standalone-echo\",\"content\":\"Untrusted visitor persistent resource fact.\"}");
                    if (step == 3) return StubLlmResponse.ToolCall("echo_write", "{\"resource\":\"standalone-echo\",\"text\":\"forbidden-write\"}");
                    if (step == 4) return StubLlmResponse.ToolCall("echo_read", "{\"resource\":\"standalone-echo\",\"text\":\"visitor-read-marker\"}");
                    return StubLlmResponse.Message("The requested read completed.");
                };
                (string id, string sessionToken) = await SessionAsync(visitor, profile, token).ConfigureAwait(false);
                Session(visitor, sessionToken);
                JsonElement response = await JsonAsync(visitor, HttpMethod.Post, Chat(profile) + "/conversations/" + id + "/messages", new { message = "Read the bound resource." }, 200, token).ConfigureAwait(false);
                Assert.True(response.GetProperty("text").GetString()!.Contains("requested read completed", StringComparison.Ordinal), "public execution reaches the normal internal runtime");
                string messageId = response.GetProperty("messageId").GetString()!;
                string feedbackPath = Chat(profile) + "/conversations/" + id + "/messages/" + messageId + "/feedback";
                JsonElement feedback = await JsonAsync(visitor, HttpMethod.Post, feedbackPath, new { rating = "Positive", comment = "Useful answer" }, 200, token).ConfigureAwait(false);
                Assert.Equal("Positive", feedback.GetProperty("rating").GetString(), "thumbs up is saved");
                await JsonAsync(visitor, HttpMethod.Post, feedbackPath, new { rating = "Negative", comment = "Please explain more" }, 200, token).ConfigureAwait(false);
                ConversationMessage savedFeedback = (await rig.Context.Database.Messages.ReadAsync(rig.TenantId, messageId, token).ConfigureAwait(false))!;
                Assert.True(savedFeedback.FeedbackRating == "Negative" && savedFeedback.FeedbackComment == "Please explain more" && savedFeedback.FeedbackUpdatedUtc.HasValue, "feedback updates are persisted as structured columns");
                await JsonAsync(visitor, HttpMethod.Post, feedbackPath, new { comment = "Comment without rating" }, 200, token).ConfigureAwait(false);
                Assert.Equal(400, (await HttpTestHelpers.SendAsync(visitor, HttpMethod.Post, feedbackPath, new { rating = "Invalid", comment = "" }, token).ConfigureAwait(false)).StatusCode, "unknown rating is rejected");
                Assert.Equal(400, (await HttpTestHelpers.SendAsync(visitor, HttpMethod.Post, feedbackPath, new { comment = new string('x', 4001) }, token).ConfigureAwait(false)).StatusCode, "oversized comment is rejected");
                using (HttpClient stranger = Anonymous(rig))
                {
                    (string otherId, string otherCapability) = await SessionAsync(stranger, profile, token).ConfigureAwait(false);
                    Session(stranger, otherCapability);
                    Assert.Equal(404, (await HttpTestHelpers.SendAsync(stranger, HttpMethod.Post, feedbackPath, new { rating = "Negative" }, token).ConfigureAwait(false)).StatusCode, "another visitor cannot rate or inspect this conversation");
                    Assert.Equal(404, (await HttpTestHelpers.SendAsync(stranger, HttpMethod.Post, Chat(profile) + "/conversations/" + otherId + "/messages/" + messageId + "/feedback", new { rating = "Negative" }, token).ConfigureAwait(false)).StatusCode, "a foreign message cannot be attached to the caller's conversation");
                }
                JsonElement history = await JsonAsync(visitor, HttpMethod.Get, Chat(profile) + "/conversations/" + id + "/messages", null, 200, token).ConfigureAwait(false);
                Assert.True(history.GetProperty("messages").EnumerateArray().All(message => new[] { "user", "assistant" }.Contains(message.GetProperty("role").GetString())), "visitor history contains only user and assistant messages");
                JsonElement rated = history.GetProperty("messages").EnumerateArray().Single(message => message.GetProperty("id").GetString() == messageId);
                Assert.Equal("Comment without rating", rated.GetProperty("feedback").GetProperty("comment").GetString(), "visitor feedback survives history reload");
                Hugo.Core.Enumeration.EnumerationResult<ConversationMessage> review = await HttpTestHelpers.SendAsync<Hugo.Core.Enumeration.EnumerationResult<ConversationMessage>>(rig.Anderson, HttpMethod.Get, "/v1.0/api/conversations/" + id + "/messages", null, 200, token).ConfigureAwait(false);
                Assert.Equal("Comment without rating", review.Objects.Single(message => message.Id == messageId).FeedbackComment, "authorized operators can review feedback through paginated message API");
                string userMessageId = history.GetProperty("messages").EnumerateArray().First(message => message.GetProperty("role").GetString() == "user").GetProperty("id").GetString()!;
                Assert.Equal(404, (await HttpTestHelpers.SendAsync(visitor, HttpMethod.Post, Chat(profile) + "/conversations/" + id + "/messages/" + userMessageId + "/feedback", new { rating = "Positive" }, token).ConfigureAwait(false)).StatusCode, "feedback only accepts visible assistant responses");
                Assert.True(!history.GetRawText().Contains("<tool_output", StringComparison.Ordinal) && !history.GetRawText().Contains("visitor-read-marker", StringComparison.Ordinal), "tool traces and raw outputs are not exposed to visitors");
                foreach (JsonElement message in history.GetProperty("messages").EnumerateArray())
                    Assert.True(message.EnumerateObject().All(property => new[] { "id", "feedback", "role", "content", "createdUtc", "inputTokens", "outputTokens" }.Contains(property.Name)), "history does not expose model prompts, tool arguments, credential identifiers, or internal message metadata");
                await rig.Context.Get<Hugo.Server.Services.ToolInvocationRecorder>().FlushAsync(token).ConfigureAwait(false);
                Hugo.Core.Enumeration.EnumerationResult<ToolInvocation> invocations = await rig.Context.Database.ToolInvocations.EnumerateAsync(rig.TenantId, new Hugo.Core.Enumeration.EnumerationQuery(), null, token).ConfigureAwait(false);
                List<ConversationMessage> internalMessages = await rig.Context.Database.Messages.ListAsync(rig.TenantId, id, false, token).ConfigureAwait(false);
                bool deniedWrite = invocations.Objects.Any(invocation => invocation.ToolName == "echo_write" && invocation.Status == InvocationStatusEnum.NotExecuted && invocation.Decision == InvocationDecisionEnum.Denied);
                bool hiddenWrite = internalMessages.Any(message => message.ToolName == "echo_write" && message.Content.Contains("error=\"unknown_tool\"", StringComparison.Ordinal));
                Assert.True(deniedWrite || hiddenWrite, "ReadOnly operational writes are either hidden and rejected as unknown_tool or denied by the normal invocation policy");
                Assert.True(!invocations.Objects.Any(invocation => invocation.ToolName == "echo_write" && invocation.Status == InvocationStatusEnum.Succeeded), "the public visitor never executes a write against the ReadOnly resource");
                AgentProfile saved = (await rig.Context.Database.AgentProfiles.ReadAsync(rig.TenantId, profile.Id, token).ConfigureAwait(false))!;
                Assert.True(!(saved.InstructionsAppend ?? String.Empty).Contains("Untrusted visitor", StringComparison.Ordinal), "forced hidden public instruction calls cannot write persistent profile state");
                Resource resource = (await rig.Context.Database.Resources.ReadByNameAsync(rig.TenantId, "standalone-echo", token).ConfigureAwait(false))!;
                Assert.Equal(0, (await rig.Context.Database.ResourceKnowledge.ListAsync(rig.TenantId, resource.Id, null, token).ConfigureAwait(false)).Count, "forced hidden context updates cannot write resource facts");
                StandaloneChatAccess access = await rig.Context.Get<StandaloneChatService>().ResolveAsync(profile.ChatLinkId!, null, token).ConfigureAwait(false);
                ToolInvocationOutcome direct = await rig.Context.Get<ToolInvocationService>().InvokeAsync(new InvocationRequest { Principal = access.Principal, TenantId = rig.TenantId, ProfileId = profile.Id, ResourceAlias = resource.Name, ToolName = "resource_update_context", ArgumentsJson = "{\"content\":\"Direct visitor metadata mutation.\"}", Surface = InvocationSurfaceEnum.Chat }, token).ConfigureAwait(false);
                Assert.True(direct.Decision == InvocationDecisionEnum.Denied && direct.Status == InvocationStatusEnum.NotExecuted, "public metadata writes remain denied even when a caller bypasses tool discovery");
                Assert.Equal(0, (await rig.Context.Database.ResourceKnowledge.ListAsync(rig.TenantId, resource.Id, null, token).ConfigureAwait(false)).Count, "direct public metadata attempt does not persist any resource context");
                Assert.True(invocations.Objects.Any(invocation => invocation.ToolName == "echo_read" && invocation.Status == InvocationStatusEnum.Succeeded && invocation.PrincipalType == PrincipalTypeEnum.Credential), "normal credential authorization, execution and audit pipeline handles the visitor's tool call");
            }
        }

        private static async Task IsolationAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            using (HttpClient first = Anonymous(rig))
            using (HttpClient second = Anonymous(rig))
            {
                AgentProfile profile = await PublishAsync(rig, null, "Public", token).ConfigureAwait(false);
                (string id, string sessionToken) = await SessionAsync(first, profile, token).ConfigureAwait(false);
                (string otherId, string otherToken) = await SessionAsync(second, profile, token).ConfigureAwait(false);
                Session(first, sessionToken);
                Session(second, otherToken);
                AgentProfile otherProfile = await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Post, "/v1.0/api/agent-profiles", new { name = "other-standalone-agent" }, 201, token).ConfigureAwait(false);
                CredentialCreateResponse otherCredential = await McpFixture.CredentialAsync(rig, "other-chat-credential", otherProfile.Id, "Operator", token).ConfigureAwait(false);
                otherProfile = await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + otherProfile.Id, new { chatEnabled = true, chatAccessMode = "Public", chatCredentialId = otherCredential.Credential.Id }, 200, token).ConfigureAwait(false);
                Assert.Equal(404, (await HttpTestHelpers.SendAsync(first, HttpMethod.Get, Chat(otherProfile) + "/conversations/" + id + "/messages", null, token).ConfigureAwait(false)).StatusCode, "a valid visitor capability cannot be replayed under another profile's share link");
                TestResponse overridden = await HttpTestHelpers.SendAsync(first, HttpMethod.Post, Chat(profile) + "/conversations", new { agentProfileId = otherProfile.Id, tenantId = "foreign-tenant", credentialId = otherCredential.Credential.Id, systemPrompt = "Override instructions", allowKnowledgeUpdates = true }, token).ConfigureAwait(false);
                Assert.True(overridden.StatusCode == 400 || overridden.StatusCode == 201, "unknown request fields are rejected or ignored safely");
                if (overridden.StatusCode == 201)
                {
                    using (JsonDocument document = JsonDocument.Parse(overridden.Body))
                    {
                        string createdId = document.RootElement.GetProperty("id").GetString()!;
                        Conversation created = (await rig.Context.Database.Conversations.ReadAsync(rig.TenantId, createdId, token).ConfigureAwait(false))!;
                        Assert.Equal(profile.Id, created.AgentProfileId, "model-selected profile/tenant/credential fields cannot override the link's runtime authority");
                    }
                }
                await JsonAsync(first, HttpMethod.Get, Chat(profile) + "/conversations/" + id + "/messages", null, 200, token).ConfigureAwait(false);
                foreach (HttpMethod method in new[] { HttpMethod.Get, HttpMethod.Post })
                {
                    TestResponse denied = await HttpTestHelpers.SendAsync(second, method, Chat(profile) + "/conversations/" + id + "/messages", method == HttpMethod.Post ? new { message = "Cross-visitor attempt." } : null, token).ConfigureAwait(false);
                    Assert.Equal(404, denied.StatusCode, "another visitor's conversation is not accessible for " + method);
                }
                Session(first, sessionToken + "tampered");
                Assert.Equal(404, (await HttpTestHelpers.SendAsync(first, HttpMethod.Get, Chat(profile) + "/conversations/" + id + "/messages", null, token).ConfigureAwait(false)).StatusCode, "tampered capability is refused");
                Session(first, sessionToken);
                Assert.Equal(404, (await HttpTestHelpers.SendAsync(first, HttpMethod.Post, Chat(profile) + "/conversations/" + otherId + "/cancel", new { }, token).ConfigureAwait(false)).StatusCode, "cancellation cannot target a different visitor");
                await JsonAsync(second, HttpMethod.Post, Chat(profile) + "/conversations/" + otherId + "/cancel", new { }, 200, token).ConfigureAwait(false);
            }
        }

        private static async Task ApprovalRevocationAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            using (HttpClient visitor = Anonymous(rig))
            {
                AgentProfile profile = await PublishAsync(rig, null, "Public", token, "TenantAdmin").ConfigureAwait(false);
                Binding binding = (await rig.Context.Database.Bindings.ListByProfileAsync(rig.TenantId, profile.Id, token).ConfigureAwait(false)).Single();
                await rig.UpdateBindingAsync(binding, request => request.AccessMode = AccessModeEnum.ReadWriteWithApproval, token).ConfigureAwait(false);
                ToolInvocationService pipeline = rig.Context.Get<ToolInvocationService>();
                foreach (string action in new[] { "active", "disable", "rotate" })
                {
                    profile = await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + profile.Id, new { chatEnabled = true, chatAccessMode = "Public" }, 200, token).ConfigureAwait(false);
                    (string conversation, string capability) = await SessionAsync(visitor, profile, token).ConfigureAwait(false);
                    Session(visitor, capability);
                    StandaloneChatAccess access = await rig.Context.Get<StandaloneChatService>().ResolveAsync(profile.ChatLinkId!, null, token).ConfigureAwait(false);
                    ToolInvocationOutcome pending = await pipeline.InvokeAsync(new InvocationRequest
                    {
                        Principal = access.Principal, TenantId = rig.TenantId, ProfileId = profile.Id,
                        ResourceAlias = "standalone-echo", ToolName = "echo_write", ArgumentsJson = "{\"text\":\"public-approved-" + action + "\"}",
                        Surface = InvocationSurfaceEnum.Chat, WaitMode = ApprovalWaitModeEnum.NoWait, ConversationId = conversation
                    }, token).ConfigureAwait(false);
                    Assert.True(pending.Status == InvocationStatusEnum.Pending && pending.Decision == InvocationDecisionEnum.ApprovalRequired && pending.ApprovalId != null, "public operational write waits for actual approval");
                    if (action != "active")
                    {
                        object change = action == "disable" ? new { chatEnabled = false } : (object)new { rotateChatLink = true };
                        await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + profile.Id, change, 200, token).ConfigureAwait(false);
                    }
                    await rig.DecideAsync(pending.ApprovalId!, "approve", token).ConfigureAwait(false);
                    ToolInvocation recorded = await rig.RecordAsync(pending.InvocationId!, token).ConfigureAwait(false);
                    if (action == "active")
                    {
                        Assert.Equal(InvocationStatusEnum.Succeeded, recorded.Status, "a still-authorized public request can execute an approved normal action");
                        Assert.Equal(InvocationDecisionEnum.Approved, recorded.Decision, "the approved action retains its explicit operator decision");
                        Assert.Equal(PrincipalTypeEnum.Credential, recorded.PrincipalType, "approved public action remains credential-scoped");
                    }
                    else
                    {
                        Assert.True(recorded.Status == InvocationStatusEnum.NotExecuted || recorded.Status == InvocationStatusEnum.Failed, "revoked publication cannot execute a deferred approval: " + action + " " + recorded.Status + " " + recorded.ErrorCode);
                        Assert.True(recorded.Status != InvocationStatusEnum.Succeeded, "operator approval cannot resurrect a revoked visitor capability");
                        Assert.True(!(recorded.OutputPreview ?? String.Empty).Contains("public-approved-" + action, StringComparison.Ordinal), "revoked action has no successful tool output");
                    }
                }
            }
        }

        private static async Task RevocationAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            using (HttpClient visitor = Anonymous(rig))
            {
                AgentProfile profile = await PublishAsync(rig, null, "Public", token).ConfigureAwait(false);
                foreach (object change in new object[] { new { chatEnabled = false }, new { rotateChatLink = true }, new { chatAccessMode = "Authenticated" } })
                {
                    AgentProfile current = await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + profile.Id, new { chatEnabled = true, chatAccessMode = "Public" }, 200, token).ConfigureAwait(false);
                    (string id, string sessionToken) = await SessionAsync(visitor, current, token).ConfigureAwait(false);
                    Session(visitor, sessionToken);
                    StandaloneChatAccess oldAccess = await rig.Context.Get<StandaloneChatService>().ResolveAsync(current.ChatLinkId!, null, token).ConfigureAwait(false);
                    await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + profile.Id, change, 200, token).ConfigureAwait(false);
                    Hugo.Core.Security.AuthorizationDecision stale = await rig.Context.Authorization.AuthorizeAsync(oldAccess.Principal, rig.TenantId, ResourceTypeEnum.AgentProfile, OperationTypeEnum.Execute, profile.Id, token).ConfigureAwait(false);
                    Assert.True(!stale.Permitted, "a previously resolved public request context cannot retain Execute after its publication revision changes");
                    TestResponse denied = await HttpTestHelpers.SendAsync(visitor, HttpMethod.Get, Chat(current) + "/conversations/" + id + "/messages", null, token).ConfigureAwait(false);
                    Assert.True(denied.StatusCode == 404 || denied.StatusCode == 401, "configuration changes revoke previously issued capabilities; authenticated mode may demand login before inspecting a session");
                }
                AgentProfile enabled = await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + profile.Id, new { chatEnabled = true, chatAccessMode = "Public" }, 200, token).ConfigureAwait(false);
                (string activeId, string activeToken) = await SessionAsync(visitor, enabled, token).ConfigureAwait(false);
                Session(visitor, activeToken);
                Credential credential = (await rig.Context.Database.Credentials.ReadAsync(rig.TenantId, enabled.ChatCredentialId!, token).ConfigureAwait(false))!;
                credential.Active = false;
                await rig.Context.Database.Credentials.UpdateAsync(credential, token).ConfigureAwait(false);
                Assert.Equal(404, (await HttpTestHelpers.SendAsync(visitor, HttpMethod.Get, Chat(enabled) + "/conversations/" + activeId + "/messages", null, token).ConfigureAwait(false)).StatusCode, "revoked credential invalidates existing visitor sessions");
                credential.Active = true;
                credential.ExpiresUtc = DateTime.UtcNow.AddMinutes(-1);
                await rig.Context.Database.Credentials.UpdateAsync(credential, token).ConfigureAwait(false);
                Assert.Equal(404, (await HttpTestHelpers.SendAsync(visitor, HttpMethod.Get, Chat(enabled) + "/conversations/" + activeId + "/messages", null, token).ConfigureAwait(false)).StatusCode, "expired credentials are rechecked on every visitor request");
                credential.ExpiresUtc = null;
                await rig.Context.Database.Credentials.UpdateAsync(credential, token).ConfigureAwait(false);
                User owner = (await rig.Context.Database.Users.ReadAsync(credential.UserId, token).ConfigureAwait(false))!;
                owner.Active = false;
                await rig.Context.Database.Users.UpdateAsync(owner, token).ConfigureAwait(false);
                Assert.Equal(404, (await HttpTestHelpers.SendAsync(visitor, HttpMethod.Get, Chat(enabled) + "/conversations/" + activeId + "/messages", null, token).ConfigureAwait(false)).StatusCode, "an inactive publisher owner immediately revokes visitor authority");
            }
        }

        private static async Task AuthenticatedAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            using (HttpClient visitor = Anonymous(rig))
            {
                AgentProfile profile = await PublishAsync(rig, null, "Authenticated", token).ConfigureAwait(false);
                JsonElement metadata = await JsonAsync(visitor, HttpMethod.Get, Chat(profile), null, 200, token).ConfigureAwait(false);
                Assert.True(metadata.GetProperty("authenticationRequired").GetBoolean(), "login requirement is public safe metadata");
                Assert.Equal(401, (await HttpTestHelpers.SendAsync(visitor, HttpMethod.Post, Chat(profile) + "/conversations", new { }, token).ConfigureAwait(false)).StatusCode, "anonymous visitor cannot start an authenticated conversation");
                using (HttpClient denied = await ChatTestSupport.UserAsync(rig, "standalone-viewer@fixture.local", "TenantMember", token).ConfigureAwait(false))
                    Assert.Equal(403, (await HttpTestHelpers.SendAsync(denied, HttpMethod.Post, Chat(profile) + "/conversations", new { }, token).ConfigureAwait(false)).StatusCode, "a valid login without profile Execute is denied");
                visitor.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-standalone-token");
                Assert.Equal(401, (await HttpTestHelpers.SendAsync(visitor, HttpMethod.Post, Chat(profile) + "/conversations", new { }, token).ConfigureAwait(false)).StatusCode, "invalid bearer is rejected independently of the public link");
                visitor.DefaultRequestHeaders.Authorization = null;
                Tenant foreign = new Tenant { Name = "foreign-standalone-tenant" };
                await rig.Context.Database.Tenants.CreateAsync(foreign, token).ConfigureAwait(false);
                AgentProfile foreignProfile = new AgentProfile { TenantId = foreign.Id, Name = "foreign-chat-agent", ChatEnabled = true, ChatAccessMode = ChatAccessModeEnum.Authenticated, ChatLinkId = "foreign-chat-" + Guid.NewGuid().ToString("N") };
                await rig.Context.Database.AgentProfiles.CreateAsync(foreignProfile, token).ConfigureAwait(false);
                TestResponse crossTenant = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, Chat(foreignProfile) + "/conversations", new { }, token).ConfigureAwait(false);
                Assert.True(crossTenant.StatusCode == 403 || crossTenant.StatusCode == 404, "a valid tenant login cannot start a different tenant's standalone chat");
                using (HttpClient user = await ChatTestSupport.UserAsync(rig, "standalone-operator@fixture.local", "Operator", token).ConfigureAwait(false))
                {
                    (string id, string sessionToken) = await SessionAsync(user, profile, token).ConfigureAwait(false);
                    Session(user, sessionToken);
                    await JsonAsync(user, HttpMethod.Get, Chat(profile) + "/conversations/" + id + "/messages", null, 200, token).ConfigureAwait(false);
                    Session(visitor, sessionToken);
                    Assert.Equal(401, (await HttpTestHelpers.SendAsync(visitor, HttpMethod.Get, Chat(profile) + "/conversations/" + id + "/messages", null, token).ConfigureAwait(false)).StatusCode, "conversation capability never substitutes for the authenticated visitor's bearer token");
                }
            }
        }

        private static async Task FailureAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            using (HttpClient visitor = Anonymous(rig))
            {
                AgentProfile profile = await PublishAsync(rig, null, "Public", token).ConfigureAwait(false);
                (string id, string sessionToken) = await SessionAsync(visitor, profile, token).ConfigureAwait(false);
                Session(visitor, sessionToken);
                TestResponse failed = await HttpTestHelpers.SendAsync(visitor, HttpMethod.Post, Chat(profile) + "/conversations/" + id + "/messages", new { message = "Handle unavailable inference safely." }, token).ConfigureAwait(false);
                Assert.True(failed.StatusCode >= 400 && failed.StatusCode < 600, "missing inference reports a controlled failure");
                Assert.True(!failed.Body.Contains("StackTrace", StringComparison.OrdinalIgnoreCase) && !failed.Body.Contains("System.InvalidOperationException", StringComparison.Ordinal), "public inference failure does not reveal exception internals");
                await JsonAsync(visitor, HttpMethod.Get, Chat(profile) + "/conversations/" + id + "/messages", null, 200, token).ConfigureAwait(false);
            }
        }
    }
}
