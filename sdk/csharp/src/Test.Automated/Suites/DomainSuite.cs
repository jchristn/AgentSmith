namespace Test.Automated.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Hugo.Sdk;
    using Hugo.Sdk.Enumeration;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Requests;
    using Hugo.Sdk.Responses;

    /// <summary>
    /// Preview tests of the Hugo domain routes (Phase 3, 5, and 6). Each test probes its route first and skips with a
    /// clear reason when the server answers 404.
    /// </summary>
    public class DomainSuite : TestSuite
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Name
        {
            get
            {
                return "Domain (preview)";
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
                List<Connector> connectors = new List<Connector>();
                await RequireRouteAsync(async () => connectors = await c.Connectors.ListAsync().ConfigureAwait(false)).ConfigureAwait(false);
                Check.True(connectors.Count > 0, "connectors loaded");
                Connector first = await c.Connectors.GetAsync(connectors[0].Type).ConfigureAwait(false);
                Check.Equal(connectors[0].Type, first.Type, "connector type");
            }).ConfigureAwait(false);

            await runner.RunAsync("Resources CRUD, duplicate, referrers", async () =>
            {
                await RequireRouteAsync(() => c.Resources.ListAsync()).ConfigureAwait(false);
                Resource resource = await c.Resources.CreateAsync(HttpResource(Unique("res"))).ConfigureAwait(false);
                try
                {
                    Check.Equal("http", resource.ConnectorType, "connector type");
                    ResourceRequest update = new ResourceRequest();
                    update.Description = "updated";
                    Resource updated = await c.Resources.UpdateAsync(resource.Id, update).ConfigureAwait(false);
                    Check.Equal("updated", updated.Description, "description");
                    Resource copy = await c.Resources.DuplicateAsync(resource.Id, Unique("rescopy")).ConfigureAwait(false);
                    await c.Resources.DeleteAsync(copy.Id).ConfigureAwait(false);
                    List<ResourceReferrer> referrers = await c.Resources.GetReferrersAsync(resource.Id).ConfigureAwait(false);
                    Check.Equal(0, referrers.Count, "no referrers");
                }
                finally
                {
                    await c.Resources.DeleteAsync(resource.Id).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            await runner.RunAsync("Resource knowledge", async () =>
            {
                await RequireRouteAsync(() => c.Resources.ListAsync()).ConfigureAwait(false);
                Resource resource = await c.Resources.CreateAsync(HttpResource(Unique("know"))).ConfigureAwait(false);
                try
                {
                    await RequireRouteAsync(() => c.Resources.ListKnowledgeAsync(resource.Id)).ConfigureAwait(false);
                    ResourceKnowledge entry = await c.Resources.PutKnowledgeAsync(resource.Id, "service", "notes", "Checkout runs here.").ConfigureAwait(false);
                    Check.Equal("Checkout runs here.", entry.Content, "content");
                    ResourceKnowledge read = await c.Resources.GetKnowledgeAsync(resource.Id, "service", "notes").ConfigureAwait(false);
                    Check.Equal(entry.Id, read.Id, "read entry");
                    List<ResourceKnowledgeVersion> versions = await c.Resources.GetKnowledgeVersionsAsync(resource.Id, "service", "notes").ConfigureAwait(false);
                    Check.True(versions.Count > 0, "versions");
                    await c.Resources.DeleteKnowledgeAsync(resource.Id, "service", "notes").ConfigureAwait(false);
                }
                finally
                {
                    await c.Resources.DeleteAsync(resource.Id).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            await runner.RunAsync("Agent profiles, bindings, compiled instructions, and tools", async () =>
            {
                await RequireRouteAsync(() => c.AgentProfiles.ListAsync()).ConfigureAwait(false);
                AgentProfileRequest create = new AgentProfileRequest();
                create.Name = Unique("profile");
                create.Description = "harness profile";
                AgentProfile profile = await c.AgentProfiles.CreateAsync(create).ConfigureAwait(false);
                Resource? resource = null;
                try
                {
                    await RequireRouteAsync(() => c.Resources.ListAsync()).ConfigureAwait(false);
                    resource = await c.Resources.CreateAsync(HttpResource(Unique("bound"))).ConfigureAwait(false);
                    BindingRequest bind = new BindingRequest();
                    bind.ResourceId = resource.Id;
                    bind.Alias = "api";
                    bind.AccessMode = "ReadOnly";
                    Binding binding = await c.AgentProfiles.CreateBindingAsync(profile.Id, bind).ConfigureAwait(false);
                    List<Binding> bindings = await c.AgentProfiles.ListBindingsAsync(profile.Id).ConfigureAwait(false);
                    Check.True(bindings.Any(b => b.Id == binding.Id), "binding listed");
                    CompiledInstructions compiled = await c.AgentProfiles.GetCompiledInstructionsAsync(profile.Id, "Mcp").ConfigureAwait(false);
                    Check.NotEmpty(compiled.Text, "compiled text");
                    CallerToolCatalog tools = await c.AgentProfiles.GetToolsAsync(profile.Id).ConfigureAwait(false);
                    Check.True(tools.Tools.Count + tools.MetaTools.Count > 0, "tools visible");
                    await c.AgentProfiles.DeleteBindingAsync(profile.Id, binding.Id).ConfigureAwait(false);
                }
                finally
                {
                    if (resource != null) await c.Resources.DeleteAsync(resource.Id).ConfigureAwait(false);
                    await c.AgentProfiles.DeleteAsync(profile.Id).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            await runner.RunAsync("Model endpoints CRUD", async () =>
            {
                await RequireRouteAsync(() => c.ModelEndpoints.ListAsync()).ConfigureAwait(false);
                ModelEndpointRequest create = new ModelEndpointRequest();
                create.Name = Unique("model");
                create.Kind = "Inference";
                create.Provider = "Ollama";
                create.BaseUrl = "http://127.0.0.1:11434";
                create.Model = "qwen3:8b";
                ModelEndpoint endpoint = await c.ModelEndpoints.CreateAsync(create).ConfigureAwait(false);
                try
                {
                    ModelEndpoint read = await c.ModelEndpoints.GetAsync(endpoint.Id).ConfigureAwait(false);
                    Check.Equal("qwen3:8b", read.Model, "model");
                }
                finally
                {
                    await c.ModelEndpoints.DeleteAsync(endpoint.Id).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            await runner.RunAsync("Tool invocations list, summary, and dry run", async () =>
            {
                await RequireRouteAsync(() => c.ToolInvocations.ListAsync()).ConfigureAwait(false);
                ToolInvocationSummary summary = await c.ToolInvocations.GetSummaryAsync().ConfigureAwait(false);
                Check.True(summary.Total >= 0, "summary total");
                ToolCallRequest request = new ToolCallRequest();
                request.Profile = "Hugo";
                request.Resource = "missing";
                request.ToolName = "http_request";
                request.Arguments = new { method = "GET", path = "/" };
                try
                {
                    ToolInvocationOutcome outcome = await c.ToolInvocations.DryRunAsync(request).ConfigureAwait(false);
                    Check.True(outcome.DryRun, "dry run flag");
                }
                catch (HugoException e) when (e.StatusCode == 400 || e.StatusCode == 404 || e.StatusCode == 422)
                {
                    Check.NotEmpty(e.Code, "dry run on a missing binding reports a typed error");
                }
            }).ConfigureAwait(false);

            await runner.RunAsync("Approvals list", async () =>
            {
                EnumerationResult<Approval> page = new EnumerationResult<Approval>();
                await RequireRouteAsync(async () => page = await c.Approvals.ListAsync().ConfigureAwait(false)).ConfigureAwait(false);
                ApprovalFilter filter = new ApprovalFilter();
                filter.Status = "Pending";
                EnumerationResult<Approval> pending = await c.Approvals.ListAsync(filter).ConfigureAwait(false);
                Check.True(pending.Objects.All(a => a.Status == "Pending"), "status filter");
                await Check.FailsAsync(() => c.Approvals.GetAsync("apr_does_not_exist"), 404).ConfigureAwait(false);
            }).ConfigureAwait(false);

            await runner.RunAsync("Tenant lockdown on and off", async () =>
            {
                LockdownState state = new LockdownState();
                await RequireRouteAsync(async () => state = await c.Lockdown.GetAsync().ConfigureAwait(false)).ConfigureAwait(false);
                Check.True(!state.Lockdown, "starts off");
                LockdownState on = await c.Lockdown.EnableAsync("harness check").ConfigureAwait(false);
                Check.True(on.Lockdown, "on");
                LockdownState off = await c.Lockdown.DisableAsync().ConfigureAwait(false);
                Check.True(!off.Lockdown, "off");
            }).ConfigureAwait(false);

            await runner.RunAsync("Conversations CRUD", async () =>
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

            await runner.RunAsync("Chat streaming emits typed events", async () =>
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

        #endregion
    }
}
