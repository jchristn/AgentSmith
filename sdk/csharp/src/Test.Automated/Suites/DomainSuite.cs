namespace Test.Automated.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Hugo.Sdk;
    using Hugo.Sdk.Enumeration;
    using Hugo.Sdk.Enums;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Requests;
    using Hugo.Sdk.Responses;

    /// <summary>
    /// Hugo domain routes: connectors, resources, knowledge, agent profiles, bindings, prompt layers, model endpoints,
    /// tool invocations, approvals, and lockdown. Tests use an HTTP resource pointing at a closed loopback port, so
    /// nothing outside the server is contacted. Conversations are a preview and skip on 404.
    /// </summary>
    public class DomainSuite : TestSuite
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Name
        {
            get
            {
                return "Domain";
            }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override async Task RunAsync(TestRunner runner, HarnessContext ctx)
        {
            HugoClient c = ctx.Owner;

            await runner.RunAsync("Connectors list and read", async () =>
            {
                List<Connector> connectors = await c.Connectors.ListAsync().ConfigureAwait(false);
                Check.True(connectors.Any(x => x.Type == "http"), "http connector loaded");
                Check.True(connectors.Any(x => x.Type == "ssh"), "ssh connector loaded");
                Connector http = await c.Connectors.GetAsync("http").ConfigureAwait(false);
                Check.True(http.Settings.Any(s => s.Key == "baseUrl" && s.Required), "baseUrl setting declared");
                Check.True(http.Tools.Any(t => t.Name == "http_request"), "http_request tool declared");
                await Check.FailsAsync(() => c.Connectors.GetAsync("no-such-connector"), 404).ConfigureAwait(false);
            }).ConfigureAwait(false);

            await runner.RunAsync("Resources CRUD, filter, test, duplicate, referrers", async () =>
            {
                Resource resource = await c.Resources.CreateAsync(HttpResource(Unique("res"))).ConfigureAwait(false);
                try
                {
                    Check.Equal("http", resource.ConnectorType, "connector type");
                    Check.True(resource.Settings.Any(s => s.Key == "baseUrl" && s.Value == "http://127.0.0.1:9"), "settings stored");
                    ResourceRequest update = new ResourceRequest();
                    update.Description = "updated";
                    update.DefaultAccessModeCeiling = "ReadWriteWithApproval";
                    Resource updated = await c.Resources.UpdateAsync(resource.Id, update).ConfigureAwait(false);
                    Check.Equal("updated", updated.Description, "description");
                    Check.Equal("ReadWriteWithApproval", updated.DefaultAccessModeCeiling, "ceiling");
                    EnumerationResult<Resource> http = await c.Resources.ListByConnectorTypeAsync("http").ConfigureAwait(false);
                    Check.True(http.Objects.Any(r => r.Id == resource.Id), "connectorType filter includes the resource");
                    EnumerationResult<Resource> ssh = await c.Resources.ListByConnectorTypeAsync("ssh").ConfigureAwait(false);
                    Check.True(ssh.Objects.All(r => r.Id != resource.Id), "connectorType filter excludes the resource");
                    ResourceTestResult test = await c.Resources.TestAsync(resource.Id).ConfigureAwait(false);
                    Check.True(!test.Success, "closed port fails the test");
                    Check.NotEmpty(test.ErrorCode, "test error code");
                    Resource reread = await c.Resources.GetAsync(resource.Id).ConfigureAwait(false);
                    Check.True(reread.LastTestUtc.HasValue && reread.LastTestResult == false, "test result recorded");
                    ResourceTestResult unsaved = await c.Resources.TestUnsavedAsync(HttpResource(Unique("unsaved"))).ConfigureAwait(false);
                    Check.True(!unsaved.Success, "unsaved test result");
                    Resource copy = await c.Resources.DuplicateAsync(resource.Id, Unique("rescopy")).ConfigureAwait(false);
                    Check.Equal(resource.ConnectorType, copy.ConnectorType, "copy connector type");
                    await c.Resources.DeleteAsync(copy.Id).ConfigureAwait(false);
                    List<ResourceReferrer> referrers = await c.Resources.GetReferrersAsync(resource.Id).ConfigureAwait(false);
                    Check.Equal(0, referrers.Count, "no referrers");
                    ResourceRequest invalid = new ResourceRequest();
                    invalid.Name = Unique("invalid");
                    invalid.ConnectorType = "http";
                    await Check.FailsAsync(() => c.Resources.CreateAsync(invalid), 400).ConfigureAwait(false);
                }
                finally
                {
                    await c.Resources.DeleteAsync(resource.Id).ConfigureAwait(false);
                }
                await Check.FailsAsync(() => c.Resources.GetAsync(resource.Id), 404).ConfigureAwait(false);
            }).ConfigureAwait(false);

            await runner.RunAsync("Resource knowledge entries, scope root, and versions", async () =>
            {
                Resource resource = await c.Resources.CreateAsync(HttpResource(Unique("know"))).ConfigureAwait(false);
                try
                {
                    ResourceKnowledge entry = await c.Resources.PutKnowledgeAsync(resource.Id, "service", "notes", "Checkout runs here.").ConfigureAwait(false);
                    Check.Equal("Checkout runs here.", entry.Content, "content");
                    Check.Equal(1, entry.Version, "version 1");
                    ResourceKnowledge second = await c.Resources.PutKnowledgeAsync(resource.Id, "service", "notes", "Checkout runs on web-01.").ConfigureAwait(false);
                    Check.Equal(2, second.Version, "version 2");
                    ResourceKnowledge root = await c.Resources.PutKnowledgeAsync(resource.Id, "service", null, "Root summary.").ConfigureAwait(false);
                    Check.Equal("Root summary.", root.Content, "root content");
                    ResourceKnowledge rootRead = await c.Resources.GetKnowledgeAsync(resource.Id, "service", "_root").ConfigureAwait(false);
                    Check.Equal(root.Id, rootRead.Id, "_root reads the scope root");
                    List<ResourceKnowledge> all = await c.Resources.ListKnowledgeAsync(resource.Id).ConfigureAwait(false);
                    Check.Equal(2, all.Count, "entries");
                    List<ResourceKnowledge> scope = await c.Resources.ListKnowledgeScopeAsync(resource.Id, "service").ConfigureAwait(false);
                    Check.Equal(2, scope.Count, "scope entries");
                    List<ResourceKnowledgeVersion> versions = await c.Resources.GetKnowledgeVersionsAsync(resource.Id, "service", "notes").ConfigureAwait(false);
                    Check.True(versions.Count >= 2 && versions[0].Version == 2, "versions newest first");
                    await c.Resources.DeleteKnowledgeAsync(resource.Id, "service", "notes").ConfigureAwait(false);
                    await c.Resources.DeleteKnowledgeAsync(resource.Id, "service").ConfigureAwait(false);
                    List<ResourceKnowledge> after = await c.Resources.ListKnowledgeAsync(resource.Id).ConfigureAwait(false);
                    Check.Equal(0, after.Count, "entries deleted");
                }
                finally
                {
                    await c.Resources.DeleteAsync(resource.Id).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            await runner.RunAsync("Agent profiles CRUD, duplicate, card, compiled instructions, tools", async () =>
            {
                AgentProfile profile = await CreateProfileAsync(c).ConfigureAwait(false);
                try
                {
                    Check.Equal("Agent Smith", profile.PersonaName, "default persona name");
                    Check.Equal("Mr. Anderson", profile.UserAddress, "default user address");
                    AgentProfileRequest update = new AgentProfileRequest();
                    update.Description = "updated profile";
                    update.MaxIterations = 6;
                    AgentProfile updated = await c.AgentProfiles.UpdateAsync(profile.Id, update).ConfigureAwait(false);
                    Check.Equal(6, updated.MaxIterations, "max iterations");
                    AgentProfile copy = await c.AgentProfiles.DuplicateAsync(profile.Id, Unique("profcopy")).ConfigureAwait(false);
                    await c.AgentProfiles.DeleteAsync(copy.Id).ConfigureAwait(false);
                    AgentCardPreview card = await c.AgentProfiles.GetCardAsync(profile.Id).ConfigureAwait(false);
                    Check.Equal(profile.Name, card.Name, "card name");
                    Check.True(card.Url != null && card.Url.Contains("/v1.0/a2a/", StringComparison.Ordinal), "card URL");
                    CompiledInstructions compiled = await c.AgentProfiles.GetCompiledInstructionsAsync(profile.Id, "Mcp").ConfigureAwait(false);
                    Check.Equal("Mcp", compiled.Surface, "surface");
                    Check.True(compiled.Text.Contains("Agent Smith", StringComparison.Ordinal), "persona in compiled text");
                    Check.True(compiled.TokenEstimate > 0 && compiled.Segments.Count > 0, "token estimate and source map");
                    string text = await c.AgentProfiles.GetCompiledInstructionsTextAsync(profile.Id).ConfigureAwait(false);
                    Check.True(text.Contains("Mr. Anderson", StringComparison.Ordinal), "plain text instructions");
                    CallerToolCatalog tools = await c.AgentProfiles.GetToolsAsync(profile.Id).ConfigureAwait(false);
                    Check.Equal(profile.Id, tools.ProfileId, "tools profile");
                    EnumerationResult<AgentProfile> page = await c.AgentProfiles.ListAsync().ConfigureAwait(false);
                    Check.True(page.Objects.Any(p => p.Id == profile.Id), "listed");
                }
                finally
                {
                    await c.AgentProfiles.DeleteAsync(profile.Id).ConfigureAwait(false);
                }
                await Check.FailsAsync(() => c.AgentProfiles.GetAsync(profile.Id), 404).ConfigureAwait(false);
            }).ConfigureAwait(false);

            await runner.RunAsync("Bindings CRUD, tool rules, and resource referrers", async () =>
            {
                AgentProfile profile = await CreateProfileAsync(c).ConfigureAwait(false);
                Resource resource = await c.Resources.CreateAsync(HttpResource(Unique("bound"))).ConfigureAwait(false);
                try
                {
                    Binding binding = await BindAsync(c, profile.Id, resource.Id, "api", "ReadOnly").ConfigureAwait(false);
                    Check.Equal("ReadOnly", binding.AccessMode, "access mode");
                    Binding read = await c.AgentProfiles.GetBindingAsync(profile.Id, binding.Id).ConfigureAwait(false);
                    Check.Equal("api", read.Alias, "alias");
                    BindingRequest update = new BindingRequest();
                    update.AccessMode = "ReadWriteWithApproval";
                    update.InstructionMode = "Append";
                    update.InstructionText = "Only touch /status.";
                    Binding updated = await c.AgentProfiles.UpdateBindingAsync(profile.Id, binding.Id, update).ConfigureAwait(false);
                    Check.Equal("ReadWriteWithApproval", updated.AccessMode, "updated access mode");
                    BindingToolRule rule = new BindingToolRule();
                    rule.ToolName = "http_health_check";
                    rule.Rule = "Deny";
                    Binding withRules = await c.AgentProfiles.SetToolRulesAsync(profile.Id, binding.Id, new List<BindingToolRule> { rule }).ConfigureAwait(false);
                    Check.True(withRules.ToolRules.Any(r => r.ToolName == "http_health_check" && r.Rule == "Deny"), "rule stored");
                    List<BindingToolRule> rules = await c.AgentProfiles.GetToolRulesAsync(profile.Id, binding.Id).ConfigureAwait(false);
                    Check.Equal(1, rules.Count, "rules listed");
                    List<Binding> bindings = await c.AgentProfiles.ListBindingsAsync(profile.Id).ConfigureAwait(false);
                    Check.True(bindings.Any(b => b.Id == binding.Id), "binding listed");
                    List<ResourceReferrer> referrers = await c.Resources.GetReferrersAsync(resource.Id).ConfigureAwait(false);
                    Check.True(referrers.Any(r => r.BindingId == binding.Id && r.AgentProfileId == profile.Id), "resource referrer");
                    await c.AgentProfiles.DeleteBindingAsync(profile.Id, binding.Id).ConfigureAwait(false);
                    await Check.FailsAsync(() => c.AgentProfiles.GetBindingAsync(profile.Id, binding.Id), 404).ConfigureAwait(false);
                }
                finally
                {
                    await c.AgentProfiles.DeleteAsync(profile.Id).ConfigureAwait(false);
                    await c.Resources.DeleteAsync(resource.Id).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            await runner.RunAsync("Dry run decisions follow the access mode", async () =>
            {
                AgentProfile profile = await CreateProfileAsync(c).ConfigureAwait(false);
                Resource resource = await c.Resources.CreateAsync(HttpResource(Unique("dry"))).ConfigureAwait(false);
                try
                {
                    await BindAsync(c, profile.Id, resource.Id, "api", "ReadOnly").ConfigureAwait(false);
                    ToolInvocationOutcome read = await c.ToolInvocations.DryRunAsync(Call(profile.Id, "GET", "/status")).ConfigureAwait(false);
                    Check.True(read.DryRun, "dry run flag");
                    Check.Equal("Allowed", read.Decision, "GET on ReadOnly");
                    Check.Equal("Read", read.Effect, "GET is Read");
                    ToolInvocationOutcome delete = await c.ToolInvocations.DryRunAsync(Call(profile.Id, "DELETE", "/x")).ConfigureAwait(false);
                    Check.Equal("Denied", delete.Decision, "DELETE on ReadOnly");
                    Check.Equal("Destructive", delete.Effect, "DELETE is Destructive");
                    Check.NotEmpty(delete.DecisionCode, "decision code");
                    ToolCallRequest missing = Call(profile.Id, "GET", "/");
                    missing.Resource = "no-such-alias";
                    try
                    {
                        ToolInvocationOutcome outcome = await c.ToolInvocations.DryRunAsync(missing).ConfigureAwait(false);
                        Check.True(outcome.Decision != "Allowed", "unknown alias is not allowed");
                    }
                    catch (HugoException e) when (e.StatusCode >= 400 && e.StatusCode < 500)
                    {
                        Check.NotEmpty(e.Code, "unknown alias error code");
                    }
                }
                finally
                {
                    await c.AgentProfiles.DeleteAsync(profile.Id).ConfigureAwait(false);
                    await c.Resources.DeleteAsync(resource.Id).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            await runner.RunAsync("Invoke with approval: pending, reject, approve, history", async () =>
            {
                AgentProfile profile = await CreateProfileAsync(c).ConfigureAwait(false);
                Resource resource = await c.Resources.CreateAsync(HttpResource(Unique("appr"))).ConfigureAwait(false);
                try
                {
                    await BindAsync(c, profile.Id, resource.Id, "api", "ReadWriteWithApproval").ConfigureAwait(false);
                    ToolInvocationOutcome pending = await c.ToolInvocations.InvokeAsync(Call(profile.Id, "POST", "/orders")).ConfigureAwait(false);
                    Check.Equal("ApprovalRequired", pending.Decision, "write needs approval");
                    Check.NotEmpty(pending.ApprovalId, "approval id");
                    Check.Equal("Pending", pending.ApprovalStatus, "approval pending");
                    ApprovalFilter filter = new ApprovalFilter();
                    filter.Status = "Pending";
                    EnumerationResult<Approval> listed = await c.Approvals.ListAsync(filter).ConfigureAwait(false);
                    Check.True(listed.Objects.Any(a => a.Id == pending.ApprovalId), "pending approval listed");
                    Approval waited = await c.Approvals.AwaitDecisionAsync(pending.ApprovalId!, TimeSpan.FromMilliseconds(300)).ConfigureAwait(false);
                    Check.Equal("Pending", waited.Status, "await times out while pending");
                    Approval rejected = await c.Approvals.RejectAsync(pending.ApprovalId!, "not now").ConfigureAwait(false);
                    Check.Equal("Rejected", rejected.Status, "rejected");
                    Check.Equal("not now", rejected.DecisionNote, "decision note");
                    Approval decided = await c.Approvals.AwaitDecisionAsync(pending.ApprovalId!, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    Check.Equal("Rejected", decided.Status, "await returns the decision");

                    ToolInvocationOutcome second = await c.ToolInvocations.InvokeAsync(Call(profile.Id, "PUT", "/orders/1")).ConfigureAwait(false);
                    Approval approved = await c.Approvals.ApproveAsync(second.ApprovalId!, "go ahead").ConfigureAwait(false);
                    Check.Equal("Approved", approved.Status, "approved");
                    Approval executed = await c.Approvals.GetAsync(second.ApprovalId!).ConfigureAwait(false);
                    Check.True(executed.Invocation != null, "approval carries its invocation");
                    ToolInvocation invocation = await c.ToolInvocations.GetAsync(second.InvocationId!).ConfigureAwait(false);
                    Check.Equal("PUT /orders/1 on api", executed.Summary, "summary");
                    Check.True(invocation.Status != "Pending", "invocation ran after approval (status " + invocation.Status + ")");

                    ToolInvocationFilter byProfile = new ToolInvocationFilter();
                    byProfile.AgentProfileId = profile.Id;
                    EnumerationResult<ToolInvocation> history = await c.ToolInvocations.ListAsync(byProfile).ConfigureAwait(false);
                    Check.True(history.Objects.Count >= 2, "invocations recorded for the profile");
                    ToolInvocationSummary summary = await c.ToolInvocations.GetSummaryAsync(DateTime.UtcNow.AddHours(-1)).ConfigureAwait(false);
                    Check.True(summary.Total >= 2 && summary.ByTool.Any(t => t.Status == "http_request"), "summary by tool");
                }
                finally
                {
                    await c.AgentProfiles.DeleteAsync(profile.Id).ConfigureAwait(false);
                    await c.Resources.DeleteAsync(resource.Id).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            await runner.RunAsync("Direct resource invoke and approval cancel", async () =>
            {
                Resource resource = await c.Resources.CreateAsync(HttpResource(Unique("direct"))).ConfigureAwait(false);
                try
                {
                    ToolInvocationOutcome outcome = await c.Resources.InvokeAsync(resource.Id, "http_request", new { method = "POST", path = "/x" }).ConfigureAwait(false);
                    Check.Equal("ApprovalRequired", outcome.Decision, "direct write needs approval");
                    Approval cancelled = await c.Approvals.CancelAsync(outcome.ApprovalId!, "changed my mind").ConfigureAwait(false);
                    Check.Equal("Cancelled", cancelled.Status, "cancelled");
                    await Check.FailsAsync(() => c.Approvals.ApproveAsync(outcome.ApprovalId!), 409).ConfigureAwait(false);
                }
                finally
                {
                    await c.Resources.DeleteAsync(resource.Id).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            await runner.RunAsync("Tenant lockdown cancels pending approvals and denies writes", async () =>
            {
                AgentProfile profile = await CreateProfileAsync(c).ConfigureAwait(false);
                Resource resource = await c.Resources.CreateAsync(HttpResource(Unique("lock"))).ConfigureAwait(false);
                try
                {
                    await BindAsync(c, profile.Id, resource.Id, "api", "ReadWrite").ConfigureAwait(false);
                    ToolInvocationOutcome pending = await c.Resources.InvokeAsync(resource.Id, "http_request", new { method = "POST", path = "/x" }).ConfigureAwait(false);
                    LockdownState state = await c.Lockdown.GetAsync().ConfigureAwait(false);
                    Check.True(!state.Lockdown, "starts off");
                    LockdownState on = await c.Lockdown.EnableAsync("harness check").ConfigureAwait(false);
                    Check.True(on.Lockdown, "on");
                    Check.True(on.ApprovalsCancelled >= 1, "pending approvals cancelled");
                    Approval cancelled = await c.Approvals.GetAsync(pending.ApprovalId!).ConfigureAwait(false);
                    Check.Equal("Cancelled", cancelled.Status, "approval cancelled by lockdown");
                    ToolInvocationOutcome write = await c.ToolInvocations.DryRunAsync(Call(profile.Id, "POST", "/x")).ConfigureAwait(false);
                    Check.Equal("Denied", write.Decision, "writes denied in lockdown");
                    ToolInvocationOutcome read = await c.ToolInvocations.DryRunAsync(Call(profile.Id, "GET", "/x")).ConfigureAwait(false);
                    Check.Equal("Allowed", read.Decision, "reads allowed in lockdown");
                    Tenant tenant = await ctx.Admin.Tenants.GetAsync(ctx.TenantId).ConfigureAwait(false);
                    Check.True(tenant.Lockdown, "tenant record shows lockdown");
                }
                finally
                {
                    LockdownState off = await c.Lockdown.DisableAsync().ConfigureAwait(false);
                    Check.True(!off.Lockdown, "off");
                    await c.AgentProfiles.DeleteAsync(profile.Id).ConfigureAwait(false);
                    await c.Resources.DeleteAsync(resource.Id).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            await runner.RunAsync("Prompt layers: save, versions, diff, built-in, reset, render", async () =>
            {
                string key = "persona.agent-smith";
                List<PromptTemplateView> views = await c.PromptLayers.ListAsync().ConfigureAwait(false);
                Check.True(views.Any(v => v.Key == key), "persona key listed");
                PromptTemplateView before = await c.PromptLayers.GetAsync(key).ConfigureAwait(false);
                Check.True(before.ActiveLayer != PromptLayerEnum.Tenant, "no tenant override yet");
                try
                {
                    PromptTemplateView saved = await c.PromptLayers.SaveAsync(key, "You are {{profile.personaName}}. Harness override.", "harness").ConfigureAwait(false);
                    Check.Equal(PromptLayerEnum.Tenant, saved.ActiveLayer, "tenant layer active");
                    Check.True(saved.Tenant != null && saved.Tenant.Exists, "tenant layer exists");
                    List<PromptTemplateVersion> versions = await c.PromptLayers.GetVersionsAsync(key).ConfigureAwait(false);
                    Check.True(versions.Count >= 1 && versions[0].ChangeNote == "harness", "version recorded");
                    List<PromptDiffLine> diff = await c.PromptLayers.GetDiffAsync(key).ConfigureAwait(false);
                    Check.True(diff.Any(d => d.Op == "add"), "diff has additions");
                    BuiltInTemplate builtIn = await c.PromptLayers.GetBuiltInAsync(key).ConfigureAwait(false);
                    Check.Equal(before.BuiltInContent, builtIn.Content, "built-in content");
                    await Check.FailsAsync(() => c.PromptLayers.SaveAsync(key, "x", null, PromptLayerEnum.Global), 403).ConfigureAwait(false);
                }
                finally
                {
                    PromptTemplateView reset = await c.PromptLayers.ResetAsync(key).ConfigureAwait(false);
                    Check.True(reset.ActiveLayer != PromptLayerEnum.Tenant, "reset removes the tenant override");
                }
                PromptRenderResponse rendered = await c.PromptLayers.RenderAsync("Good evening, {{input.who}}.", new { input = new { who = "Mr. Anderson" } }).ConfigureAwait(false);
                Check.Equal("Good evening, Mr. Anderson.", rendered.Rendered, "render");
            }).ConfigureAwait(false);

            await runner.RunAsync("Model endpoints CRUD and validate", async () =>
            {
                ModelEndpointRequest create = new ModelEndpointRequest();
                create.Name = Unique("model");
                create.Kind = "Inference";
                create.Provider = "Ollama";
                create.BaseUrl = "http://127.0.0.1:9";
                create.Model = "qwen3:8b";
                ModelEndpoint endpoint = await c.ModelEndpoints.CreateAsync(create).ConfigureAwait(false);
                try
                {
                    Check.Equal("Unknown", endpoint.HealthStatus, "initial health");
                    ModelEndpointRequest update = new ModelEndpointRequest();
                    update.MaxConcurrency = 2;
                    ModelEndpoint updated = await c.ModelEndpoints.UpdateAsync(endpoint.Id, update).ConfigureAwait(false);
                    Check.Equal(2, updated.MaxConcurrency, "max concurrency");
                    ModelValidationResult validation = await c.ModelEndpoints.ValidateAsync(endpoint.Id).ConfigureAwait(false);
                    Check.True(!validation.Success, "closed port fails validation");
                    ModelEndpoint after = await c.ModelEndpoints.GetAsync(endpoint.Id).ConfigureAwait(false);
                    Check.Equal("Unhealthy", after.HealthStatus, "health recorded");
                    EnumerationResult<ModelEndpoint> page = await c.ModelEndpoints.ListAsync().ConfigureAwait(false);
                    Check.True(page.Objects.Any(m => m.Id == endpoint.Id), "listed");
                }
                finally
                {
                    await c.ModelEndpoints.DeleteAsync(endpoint.Id).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            await runner.RunAsync("System administrator addresses a tenant with TenantOverride", async () =>
            {
                Resource resource = await c.Resources.CreateAsync(HttpResource(Unique("override"))).ConfigureAwait(false);
                try
                {
                    using (HugoClient admin = ctx.ForTenant(ctx.Admin, ctx.Environment.AdminTenantId))
                    {
                        EnumerationQuery query = new EnumerationQuery();
                        query.MaxResults = 1000;
                        EnumerationResult<Resource> own = await admin.Resources.ListAsync(query).ConfigureAwait(false);
                        Check.True(own.Objects.All(r => r.Id != resource.Id), "the administrator's own tenant does not see it");
                        admin.Transport.TenantOverride = ctx.TenantId;
                        EnumerationResult<Resource> other = await admin.Resources.ListAsync(query).ConfigureAwait(false);
                        Check.True(other.Objects.Any(r => r.Id == resource.Id), "?tenantId= addresses the harness tenant");
                    }
                }
                finally
                {
                    await c.Resources.DeleteAsync(resource.Id).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            await runner.RunAsync("Conversations CRUD (preview)", async () =>
            {
                await RequireRouteAsync(() => c.Conversations.ListAsync()).ConfigureAwait(false);
                ConversationRequest create = new ConversationRequest();
                create.Title = Unique("chat");
                Conversation conversation = await c.Conversations.CreateAsync(create).ConfigureAwait(false);
                try
                {
                    List<ConversationMessage> messages = await c.Conversations.ListMessagesAsync(conversation.Id).ConfigureAwait(false);
                    Check.Equal(0, messages.Count, "no messages yet");
                }
                finally
                {
                    await c.Conversations.DeleteAsync(conversation.Id).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            await runner.RunAsync("Chat streaming emits typed events (preview)", async () =>
            {
                await RequireRouteAsync(() => c.Conversations.ListAsync()).ConfigureAwait(false);
                AgentProfile? profile = null;
                await foreach (AgentProfile p in c.AgentProfiles.EnumerateAsync().ConfigureAwait(false))
                {
                    if (!String.IsNullOrEmpty(p.InferenceEndpointId))
                    {
                        profile = p;
                        break;
                    }
                }
                if (profile == null) throw new TestSkippedException("no agent profile with an inference endpoint in the test tenant");
                ConversationRequest create = new ConversationRequest();
                create.AgentProfileId = profile.Id;
                Conversation conversation = await c.Conversations.CreateAsync(create).ConfigureAwait(false);
                try
                {
                    List<string> types = new List<string>();
                    await foreach (ChatEvent item in c.Conversations.StreamMessageAsync(conversation.Id, "Say hello.").ConfigureAwait(false))
                    {
                        types.Add(item.Type);
                    }
                    Check.True(types.Contains(ChatEventTypes.TurnStarted), "turn.started");
                    Check.True(types.Contains(ChatEventTypes.TurnCompleted) || types.Contains(ChatEventTypes.Error), "terminal event");
                }
                finally
                {
                    await c.Conversations.DeleteAsync(conversation.Id).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static ResourceRequest HttpResource(string name)
        {
            ResourceRequest request = new ResourceRequest();
            request.Name = name;
            request.ConnectorType = "http";
            request.DisplayName = name;
            request.Description = "Hugo C# SDK harness resource";
            request.Environment = "test";
            request.Settings = new Dictionary<string, object?> { { "baseUrl", "http://127.0.0.1:9" } };
            return request;
        }

        private static Task<AgentProfile> CreateProfileAsync(HugoClient c)
        {
            AgentProfileRequest create = new AgentProfileRequest();
            create.Name = Unique("profile");
            create.Description = "harness profile";
            return c.AgentProfiles.CreateAsync(create);
        }

        private static Task<Binding> BindAsync(HugoClient c, string profileId, string resourceId, string alias, string accessMode)
        {
            BindingRequest bind = new BindingRequest();
            bind.ResourceId = resourceId;
            bind.Alias = alias;
            bind.AccessMode = accessMode;
            return c.AgentProfiles.CreateBindingAsync(profileId, bind);
        }

        private static ToolCallRequest Call(string profileId, string method, string path)
        {
            ToolCallRequest request = new ToolCallRequest();
            request.ProfileId = profileId;
            request.Resource = "api";
            request.ToolName = "http_request";
            request.Arguments = new { method = method, path = path };
            return request;
        }

        #endregion
    }
}
