namespace Test.Shared.Integration
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Agent;
    using Hugo.Core.Database;
    using Hugo.Core.Enums;
    using Hugo.Core.Instructions;
    using Hugo.Core.Models;
    using Hugo.Core.Responses;
    using Hugo.Core.Requests;
    using Hugo.Core.Security;
    using Hugo.Core.Services;
    using Hugo.Server.Services;
    using Test.Shared.Agent.StubLlm;
    using Test.Shared.Chat;
    using Test.Shared.Infrastructure;
    using Test.Shared.Integration.Mcp;
    using Touchstone.Core;

    /// <summary>
    /// Persistent profile instructions exercised through actual chat requests and deterministic model responses.
    /// </summary>
    public static class AgentInstructionsSuite
    {
        /// <summary>
        /// Build provider-specific integration suites.
        /// </summary>
        /// <returns>Instruction suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            List<TestSuiteDescriptor> suites = new List<TestSuiteDescriptor>();
            foreach (DatabaseTypeEnum provider in TestEnvironment.Providers())
            {
                string id = "agent-instructions-" + provider.ToString().ToLowerInvariant();
                suites.Add(new TestSuiteDescriptor(id, "Persistent agent instructions (" + provider + ")", new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(id, "natural-chat-persistence", "A natural chat reconciles profile instructions without user-supplied versions and the next conversation compiles them", ct => NaturalChatAsync(provider, ct)),
                    new TestCaseDescriptor(id, "atomic-version-updates", "Versioned concurrent writes conflict safely and versionless appends preserve every update", ct => ConcurrentAsync(provider, ct)),
                    new TestCaseDescriptor(id, "tenant-profile-boundaries", "Tenant and credential profile locks prevent instruction disclosure or updates", ct => BoundariesAsync(provider, ct)),
                    new TestCaseDescriptor(id, "mcp-current-profile", "MCP instruction tools authorize each call and only operate on the selected profile", ct => McpAsync(provider, ct)),
                    new TestCaseDescriptor(id, "operator-permission-denied", "A user with Execute but no profile Update cannot save agent instructions", ct => DeniedAsync(provider, ct))
                }));
            }
            return suites;
        }

        private static async Task ConfigureModelAsync(IntegrationRig rig, StubLlmServer stub, CancellationToken token)
        {
            ModelEndpoint endpoint = await HttpTestHelpers.SendAsync<ModelEndpoint>(rig.Anderson, HttpMethod.Post, "/v1.0/api/model-endpoints", new { name = "instruction-fixture", provider = "OpenAICompatible", baseUrl = stub.BaseUrl, model = stub.ModelName, maxRetries = 0 }, 201, token).ConfigureAwait(false);
            await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + rig.Hugo.Id, new { inferenceEndpointId = endpoint.Id, maxIterations = 5, instructionsAppend = "Operator rule: preserve the maintenance calendar." }, 200, token).ConfigureAwait(false);
        }

        private static Task<Conversation> ConversationAsync(HttpClient client, string profile, CancellationToken token)
        {
            return HttpTestHelpers.SendAsync<Conversation>(client, HttpMethod.Post, "/v1.0/api/conversations", new { agentProfileId = profile, title = "Already titled instruction fixture" }, 201, token);
        }

        private static Task<AgentTurnResult> TurnAsync(HttpClient client, Conversation conversation, string message, CancellationToken token)
        {
            return HttpTestHelpers.SendAsync<AgentTurnResult>(client, HttpMethod.Post, "/v1.0/api/conversations/" + conversation.Id + "/messages", new { message }, 200, token);
        }

        private static async Task NaturalChatAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (StubLlmServer stub = new StubLlmServer())
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, settings => settings.Agent.ToolRetrievalThreshold = 1000, token).ConfigureAwait(false))
            {
                await ConfigureModelAsync(rig, stub, token).ConfigureAwait(false);
                AgentProfile other = await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Post, "/v1.0/api/agent-profiles", new { name = "other-instruction-agent", instructionsAppend = "Other agent private instruction." }, 201, token).ConfigureAwait(false);
                string marker = "Always explain production risks before suggesting a service restart.";
                int planning = 0;
                string readDiagnostic = String.Empty;
                stub.Script.Responder = request =>
                {
                    if (request.Messages.Any(m => m.Content != null && m.Content.Contains("Write a short title", StringComparison.Ordinal))) return StubLlmResponse.Message("Persistent instructions");
                    int step = Interlocked.Increment(ref planning);
                    Assert.True(request.ToolNames.Contains("agent_get_instructions") && request.ToolNames.Contains("agent_update_instructions"), "profile tools are available to an authorized editor without enabling management tools");
                    if (step == 1) return StubLlmResponse.ToolCall("agent_get_instructions", "{}");
                    if (step == 2)
                    {
                        string response = request.Messages.Last(m => m.Role == "tool").Content ?? throw new TestAssertionException("instruction read response is missing");
                        Assert.True(response.StartsWith("<tool_output ", StringComparison.Ordinal) && response.EndsWith("\n</tool_output>", StringComparison.Ordinal), "hosted tool results retain the untrusted-output fence");
                        response = response.Substring(response.IndexOf('\n') + 1, response.LastIndexOf('\n') - response.IndexOf('\n') - 1);
                        readDiagnostic = response;
                        using (JsonDocument read = JsonDocument.Parse(response))
                        {
                            string version = read.RootElement.GetProperty("version").GetString()!;
                            Assert.True(!String.IsNullOrEmpty(version), "the model reads its own concurrency version rather than requesting it from the user");
                            string content = "Operator rule: preserve the maintenance calendar.\n\n" + marker;
                            return StubLlmResponse.ToolCall("agent_update_instructions", JsonSerializer.Serialize(new { content, mode = "replace", expectedVersion = version }));
                        }
                    }
                    Assert.True(request.Messages.Any(m => m.Role == "system" && m.Content != null && m.Content.Contains(marker, StringComparison.Ordinal)), "the next planning call in the same turn receives fresh updated profile guidance");
                    return StubLlmResponse.Message("Saved the instruction for future conversations.");
                };
                Conversation conversation = await ConversationAsync(rig.Anderson, rig.Hugo.Id, token).ConfigureAwait(false);
                AgentTurnResult saved = await TurnAsync(rig.Anderson, conversation, "Please remember to explain production risks before suggesting any service restart.", token).ConfigureAwait(false);
                Assert.True(saved.FinalText.Contains("Saved", StringComparison.Ordinal) && planning == 3, "normal get, reconcile, final tool sequence completes without asking the user for an expectedVersion; steps=" + planning + "; final=" + saved.FinalText + "; read=" + readDiagnostic);
                AgentProfile profile = await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Get, "/v1.0/api/agent-profiles/" + rig.Hugo.Id, null, 200, token).ConfigureAwait(false);
                Assert.True(profile.InstructionsAppend != null && profile.InstructionsAppend.Contains(marker, StringComparison.Ordinal) && profile.InstructionsAppend.Contains("preserve the maintenance calendar", StringComparison.Ordinal), "dashboard profile field persists new guidance while preserving operator text");
                AgentProfile unchanged = await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Get, "/v1.0/api/agent-profiles/" + other.Id, null, 200, token).ConfigureAwait(false);
                Assert.Equal("Other agent private instruction.", unchanged.InstructionsAppend, "saving instructions changes only the current profile");
                CompiledInstructions compiled = await HttpTestHelpers.SendAsync<CompiledInstructions>(rig.Anderson, HttpMethod.Get, "/v1.0/api/agent-profiles/" + rig.Hugo.Id + "/compiled-instructions?surface=Chat", null, 200, token).ConfigureAwait(false);
                Assert.True(compiled.Text.Contains(marker, StringComparison.Ordinal), "compiler cache invalidated after the profile instruction update");
                stub.Script.Responder = request =>
                {
                    if (request.Messages.Any(m => m.Content != null && m.Content.Contains("Write a short title", StringComparison.Ordinal))) return StubLlmResponse.Message("Next conversation");
                    Assert.True(request.Messages.Any(m => m.Role == "system" && m.Content != null && m.Content.Contains(marker, StringComparison.Ordinal)), "a new conversation receives persisted profile guidance before its first model call");
                    return StubLlmResponse.Message("I will explain the production risks first.");
                };
                Conversation next = await ConversationAsync(rig.Anderson, rig.Hugo.Id, token).ConfigureAwait(false);
                await TurnAsync(rig.Anderson, next, "What should we consider before a restart?", token).ConfigureAwait(false);
                Assert.True((await rig.AuditAsync(AuditEventTypeEnum.ProfileChanged, token).ConfigureAwait(false)).Any(e => e.ResourceId == rig.Hugo.Id), "profile instruction changes are audited");
            }
        }

        private static async Task<RequestContext> PrincipalAsync(IntegrationRig rig, CancellationToken token)
        {
            User user = (await rig.Context.Database.Users.ReadByEmailAsync(rig.TenantId, "anderson@hugo.local", token).ConfigureAwait(false))!;
            return new RequestContext { IsAuthenticated = true, TenantId = rig.TenantId, UserId = user.Id, User = user, PrincipalName = user.Email, PrincipalType = PrincipalTypeEnum.User, IsTenantAdmin = user.IsTenantAdmin };
        }

        private static AgentInstructionService Instructions(IntegrationRig rig)
        {
            return new AgentInstructionService(rig.Context.Database, rig.Context.Authorization, rig.Context.Audit, rig.Context.Get<InstructionCompiler>());
        }

        private static async Task<bool> VersionedWriteAsync(AgentInstructionService service, RequestContext principal, string tenant, string profile, string version, string content, CancellationToken token)
        {
            try
            {
                await service.UpdateAsync(principal, tenant, profile, new AgentInstructionRequest { Content = content, Mode = "replace", ExpectedVersion = version }, token).ConfigureAwait(false);
                return true;
            }
            catch (ConflictException error) when (error.Code == "version_conflict")
            {
                return false;
            }
        }

        private static async Task ConcurrentAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                AgentInstructionService service = Instructions(rig);
                RequestContext principal = await PrincipalAsync(rig, token).ConfigureAwait(false);
                AgentInstructionResponse initial = await service.ReadAsync(principal, rig.TenantId, rig.Hugo.Id, token).ConfigureAwait(false);
                bool[] outcomes = await Task.WhenAll(
                    VersionedWriteAsync(service, principal, rig.TenantId, rig.Hugo.Id, initial.Version, "Concurrent winner alpha.", token),
                    VersionedWriteAsync(service, principal, rig.TenantId, rig.Hugo.Id, initial.Version, "Concurrent winner beta.", token)).ConfigureAwait(false);
                Assert.Equal(1, outcomes.Count(success => success), "one writer using the same version succeeds and the stale writer conflicts");
                AgentInstructionResponse winner = await service.ReadAsync(principal, rig.TenantId, rig.Hugo.Id, token).ConfigureAwait(false);
                Assert.Equal(outcomes[0] ? "Concurrent winner alpha." : "Concurrent winner beta.", winner.Content, "stale replacement never overwrites the winner");
                Assert.True(initial.Version != winner.Version, "the content version changes after a durable write");
                await Task.WhenAll(
                    service.UpdateAsync(principal, rig.TenantId, rig.Hugo.Id, new AgentInstructionRequest { Content = "Concurrent appended red.", Mode = "append" }, token),
                    service.UpdateAsync(principal, rig.TenantId, rig.Hugo.Id, new AgentInstructionRequest { Content = "Concurrent appended blue.", Mode = "append" }, token)).ConfigureAwait(false);
                AgentInstructionResponse appended = await service.ReadAsync(principal, rig.TenantId, rig.Hugo.Id, token).ConfigureAwait(false);
                Assert.True(appended.Content.Contains(winner.Content, StringComparison.Ordinal) && appended.Content.Contains("Concurrent appended red.", StringComparison.Ordinal) && appended.Content.Contains("Concurrent appended blue.", StringComparison.Ordinal), "versionless concurrent appends retry CAS without losing existing instructions or either addition");
                AgentProfile saved = (await rig.Context.Database.AgentProfiles.ReadAsync(rig.TenantId, rig.Hugo.Id, token).ConfigureAwait(false))!;
                Assert.Equal(rig.Hugo.PersonaName, saved.PersonaName, "atomic instruction updates preserve unrelated profile fields");
                Assert.Equal(appended.Content, saved.InstructionsAppend, "existing dashboard profile field is the durable source of truth");
                bool replaceRequiresVersion = false;
                try { await service.UpdateAsync(principal, rig.TenantId, rig.Hugo.Id, new AgentInstructionRequest { Content = "Unsafe blind replacement.", Mode = "replace" }, token).ConfigureAwait(false); }
                catch (ValidationException) { replaceRequiresVersion = true; }
                Assert.True(replaceRequiresVersion, "destructive replacement requires a read version even though natural append does not");
                Assert.Equal(appended.Content, (await service.ReadAsync(principal, rig.TenantId, rig.Hugo.Id, token).ConfigureAwait(false)).Content, "rejected replacement does not mutate saved instructions");
                foreach (AgentInstructionRequest invalid in new[]
                {
                    new AgentInstructionRequest { Content = "password=fixture-private-secret" },
                    new AgentInstructionRequest { Content = new string('x', 65537) },
                    new AgentInstructionRequest { Content = "Invalid mode instruction.", Mode = "merge" },
                    new AgentInstructionRequest { Content = "   ", Mode = "append" }
                })
                {
                    bool refused = false;
                    try { await service.UpdateAsync(principal, rig.TenantId, rig.Hugo.Id, invalid, token).ConfigureAwait(false); }
                    catch (ValidationException) { refused = true; }
                    Assert.True(refused, "secret-shaped, oversized, invalid-mode, and empty append requests are refused");
                    Assert.Equal(appended.Content, (await service.ReadAsync(principal, rig.TenantId, rig.Hugo.Id, token).ConfigureAwait(false)).Content, "invalid contributions leave the durable document intact");
                }
                AgentInstructionResponse cleared = await service.UpdateAsync(principal, rig.TenantId, rig.Hugo.Id, new AgentInstructionRequest { Content = String.Empty, Mode = "replace", ExpectedVersion = appended.Version }, token).ConfigureAwait(false);
                Assert.Equal(String.Empty, cleared.Content, "an explicitly versioned empty replacement clears instructions");
                Assert.Equal(String.Empty, (await service.ReadAsync(principal, rig.TenantId, rig.Hugo.Id, token).ConfigureAwait(false)).Content, "clearing persists for subsequent reads");
            }
        }

        private static async Task BoundariesAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                AgentInstructionService service = Instructions(rig);
                RequestContext principal = await PrincipalAsync(rig, token).ConfigureAwait(false);
                Tenant foreign = new Tenant { Name = "Foreign instruction tenant" };
                await rig.Context.Database.Tenants.CreateAsync(foreign, token).ConfigureAwait(false);
                AgentProfile foreignProfile = new AgentProfile { TenantId = foreign.Id, Name = "foreign-instruction-agent", InstructionsAppend = "Foreign confidential instruction." };
                await rig.Context.Database.AgentProfiles.CreateAsync(foreignProfile, token).ConfigureAwait(false);
                bool blocked = false;
                try { await service.ReadAsync(principal, foreign.Id, foreignProfile.Id, token).ConfigureAwait(false); }
                catch (ForbiddenException) { blocked = true; }
                catch (NotFoundException) { blocked = true; }
                Assert.True(blocked, "a caller cannot read instructions from a different tenant");
                blocked = false;
                try { await service.UpdateAsync(principal, foreign.Id, foreignProfile.Id, new AgentInstructionRequest { Content = "Foreign overwrite." }, token).ConfigureAwait(false); }
                catch (ForbiddenException) { blocked = true; }
                catch (NotFoundException) { blocked = true; }
                Assert.True(blocked, "a caller cannot update a different tenant");
                AgentProfile other = await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Post, "/v1.0/api/agent-profiles", new { name = "locked-out-instruction-profile", instructionsAppend = "Other private instruction." }, 201, token).ConfigureAwait(false);
                Credential credential = new Credential { TenantId = rig.TenantId, Name = "instruction-lock", AgentProfileId = rig.Hugo.Id, LockToProfile = true };
                RequestContext locked = new RequestContext { IsAuthenticated = true, TenantId = rig.TenantId, PrincipalType = PrincipalTypeEnum.Credential, CredentialId = credential.Id, Credential = credential, IsTenantAdmin = true };
                blocked = false;
                try { await service.ReadAsync(locked, rig.TenantId, other.Id, token).ConfigureAwait(false); }
                catch (ForbiddenException) { blocked = true; }
                catch (NotFoundException) { blocked = true; }
                Assert.True(blocked, "a locked credential cannot read another profile even with an administrator capability flag");
                blocked = false;
                try { await service.UpdateAsync(locked, rig.TenantId, other.Id, new AgentInstructionRequest { Content = "Locked credential overwrite." }, token).ConfigureAwait(false); }
                catch (ForbiddenException) { blocked = true; }
                catch (NotFoundException) { blocked = true; }
                Assert.True(blocked, "a locked credential cannot update another profile");
                Assert.Equal("Other private instruction.", (await rig.Context.Database.AgentProfiles.ReadAsync(rig.TenantId, other.Id, token).ConfigureAwait(false))!.InstructionsAppend, "profile lock leaves the other profile unchanged");
                Assert.Equal("Foreign confidential instruction.", (await rig.Context.Database.AgentProfiles.ReadAsync(foreign.Id, foreignProfile.Id, token).ConfigureAwait(false))!.InstructionsAppend, "cross-tenant writes leave foreign data unchanged");
            }
        }

        private static async Task McpAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (McpFixture fixture = await McpFixture.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                IntegrationRig rig = fixture.Rig;
                await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + rig.Hugo.Id, new { instructionsAppend = "Hugo operator instruction." }, 200, token).ConfigureAwait(false);
                await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + fixture.Ops.Id, new { instructionsAppend = "Ops operator instruction." }, 200, token).ConfigureAwait(false);
                using (McpTestClient admin = await fixture.ConnectAsync(fixture.AndersonHeaders(), token, "/mcp/Hugo").ConfigureAwait(false))
                {
                    McpTestToolList tools = await admin.ListToolsAsync(token).ConfigureAwait(false);
                    Assert.True(tools.Find("agent_get_instructions") != null && tools.Find("agent_update_instructions") != null, "authorized editor sees read and update without enabling management tools");
                    McpTestToolResult read = await admin.CallToolAsync("agent_get_instructions", new { }, token).ConfigureAwait(false);
                    Assert.True(read.IsError != true && read.Text.Contains("Hugo operator instruction.", StringComparison.Ordinal), "MCP reads the selected profile");
                    using (JsonDocument document = JsonDocument.Parse(read.Text))
                    {
                        Assert.Equal(rig.Hugo.Id, document.RootElement.GetProperty("profileId").GetString(), "MCP read is bound to the selected profile");
                        Assert.True(!String.IsNullOrEmpty(document.RootElement.GetProperty("version").GetString()), "MCP supplies the version to the agent itself");
                    }
                    McpTestToolResult written = await admin.CallToolAsync("agent_update_instructions", new { content = "Remember the agreed escalation workflow.", mode = "append" }, token).ConfigureAwait(false);
                    Assert.True(written.IsError != true, "authorized MCP update succeeds: " + written.Text);
                    McpTestToolResult redirected = await admin.CallToolAsync("agent_update_instructions", new { content = "Cross-profile overwrite.", agentProfileId = fixture.Ops.Id }, token).ConfigureAwait(false);
                    Assert.True(redirected.IsError == true && redirected.Text.StartsWith("invalid_argument:", StringComparison.Ordinal), "model arguments cannot select another profile: " + redirected.Text);
                }
                using (McpTestClient restricted = await fixture.ConnectAsync(McpFixture.Headers(fixture.HugoCredential), token).ConfigureAwait(false))
                {
                    McpTestToolList tools = await restricted.ListToolsAsync(token).ConfigureAwait(false);
                    Assert.True(tools.Find("agent_get_instructions") != null && tools.Find("agent_update_instructions") == null, "Execute-only Operator credential sees read but not update");
                    McpTestToolResult read = await restricted.CallToolAsync("agent_get_instructions", new { }, token).ConfigureAwait(false);
                    Assert.True(read.IsError != true && read.Text.Contains("agreed escalation workflow", StringComparison.Ordinal), "read permission independently survives hidden update");
                    McpTestToolResult denied = await restricted.CallToolAsync("agent_update_instructions", new { content = "Unauthorized MCP persistent instruction." }, token).ConfigureAwait(false);
                    Assert.True(denied.IsError == true && denied.Text.StartsWith("forbidden:", StringComparison.Ordinal), "a guessed hidden MCP update is denied at execution");
                }
                AgentProfile current = (await rig.Context.Database.AgentProfiles.ReadAsync(rig.TenantId, rig.Hugo.Id, token).ConfigureAwait(false))!;
                AgentProfile other = (await rig.Context.Database.AgentProfiles.ReadAsync(rig.TenantId, fixture.Ops.Id, token).ConfigureAwait(false))!;
                Assert.True(current.InstructionsAppend!.Contains("Hugo operator instruction.", StringComparison.Ordinal) && current.InstructionsAppend.Contains("agreed escalation workflow", StringComparison.Ordinal) && !current.InstructionsAppend.Contains("Unauthorized", StringComparison.Ordinal), "only authorized current-profile content persists");
                Assert.Equal("Ops operator instruction.", other.InstructionsAppend, "MCP never edits a model-selected other profile");
            }
        }

        private static async Task DeniedAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (StubLlmServer stub = new StubLlmServer())
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, settings => settings.Agent.ToolRetrievalThreshold = 1000, token).ConfigureAwait(false))
            {
                await ConfigureModelAsync(rig, stub, token).ConfigureAwait(false);
                using (HttpClient caller = await ChatTestSupport.UserAsync(rig, "instruction-operator@fixture.local", "Operator", token).ConfigureAwait(false))
                {
                    User user = (await rig.Context.Database.Users.ReadByEmailAsync(rig.TenantId, "instruction-operator@fixture.local", token).ConfigureAwait(false))!;
                    RequestContext principal = new RequestContext { IsAuthenticated = true, TenantId = rig.TenantId, UserId = user.Id, User = user, PrincipalName = user.Email, PrincipalType = PrincipalTypeEnum.User };
                    bool denied = false;
                    try { await Instructions(rig).UpdateAsync(principal, rig.TenantId, rig.Hugo.Id, new AgentInstructionRequest { Content = "Direct unauthorized instruction." }, token).ConfigureAwait(false); }
                    catch (ForbiddenException error) when (error.Code == "permission_denied") { denied = true; }
                    Assert.True(denied, "the write service returns permission_denied for an actual authenticated user without profile Update");
                    int planning = 0;
                    stub.Script.Responder = request =>
                    {
                        if (request.Messages.Any(m => m.Content != null && m.Content.Contains("Write a short title", StringComparison.Ordinal))) return StubLlmResponse.Message("Denied instruction write");
                        int step = Interlocked.Increment(ref planning);
                        Assert.True(!request.ToolNames.Contains("agent_update_instructions"), "no profile Update permission means the write tool is hidden");
                        if (step == 1) return StubLlmResponse.ToolCall("agent_update_instructions", "{\"content\":\"unauthorized persistent guidance\",\"mode\":\"append\"}");
                        return StubLlmResponse.Message("I cannot change the profile instructions.");
                    };
                    Conversation conversation = await ConversationAsync(caller, rig.Hugo.Id, token).ConfigureAwait(false);
                    await TurnAsync(caller, conversation, "Save this as your persistent instruction.", token).ConfigureAwait(false);
                    AgentProfile stored = (await rig.Context.Database.AgentProfiles.ReadAsync(rig.TenantId, rig.Hugo.Id, token).ConfigureAwait(false))!;
                    Assert.Equal("Operator rule: preserve the maintenance calendar.", stored.InstructionsAppend, "a guessed hidden tool cannot bypass Update authorization");
                }
            }
        }
    }
}
