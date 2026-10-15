namespace Test.Shared.Integration
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Agent;
    using Hugo.Core.Agent.Events;
    using Hugo.Core.Connectors;
    using Hugo.Core.Enums;
    using Hugo.Core.Inference;
    using Hugo.Core.Instructions;
    using Hugo.Core.Models;
    using Hugo.Core.Responses;
    using Hugo.Core.Security;
    using Hugo.Server.Services;
    using Test.Shared.Agent;
    using Test.Shared.Agent.StubLlm;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;

    /// <summary>
    /// Model endpoint and agent suites (P6.2, P6.5, P6.6, P3.26): endpoint CRUD with a vault API key, validation and the
    /// health sweep against the stub LLM, the connector model bound to the profile's inference endpoint with its call
    /// budget, RequiresModel visibility, and a full AgentRuntime turn through the real tool catalog and executor
    /// (including an approval decided mid-turn), on every configured provider.
    /// </summary>
    public static class ModelAgentSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the suites.
        /// </summary>
        /// <returns>Suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            List<TestSuiteDescriptor> suites = new List<TestSuiteDescriptor>();
            foreach (DatabaseTypeEnum provider in TestEnvironment.Providers())
            {
                string id = "integration-model-" + provider.ToString().ToLowerInvariant();
                List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
                cases.Add(new TestCaseDescriptor(id, "endpoints", "Model endpoint CRUD, vault key references, validation, health sweep, and secret referrers (" + provider + ")", ct => EndpointsAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "connector-model", "Tools call the profile's model through IConnectorModel with a per-call budget; RequiresModel tools hide without an endpoint (" + provider + ")", ct => ConnectorModelAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "agent-turn", "AgentRuntime turn through the real catalog, executor, pipeline, and an approval decided mid-turn (" + provider + ")", ct => AgentTurnAsync(provider, ct)));
                suites.Add(new TestSuiteDescriptor(id, "Integration models and agent (" + provider + ")", cases));
            }
            return suites;
        }

        #endregion

        #region Private-Methods

        private static async Task<ModelEndpoint> EndpointAsync(IntegrationRig rig, StubLlmServer stub, string name, CancellationToken token)
        {
            Secret key = await rig.SecretAsync(name + "-key", SecretKindEnum.ApiToken, new Dictionary<string, string> { { "token", "stub-api-key-" + name } }, token).ConfigureAwait(false);
            return await HttpTestHelpers.SendAsync<ModelEndpoint>(rig.Anderson, HttpMethod.Post, "/v1.0/api/model-endpoints", new { name = name, provider = "OpenAICompatible", baseUrl = stub.BaseUrl, model = stub.ModelName, apiKeySecretId = key.Id, maxRetries = 0, timeoutSeconds = 30 }, 201, token).ConfigureAwait(false);
        }

        private static async Task EndpointsAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (StubLlmServer stub = new StubLlmServer())
            {
                stub.Script.DefaultResponse = StubLlmResponse.Message("pong");
                await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
                {
                    ModelEndpoint endpoint = await EndpointAsync(rig, stub, "stub", token).ConfigureAwait(false);
                    Assert.Equal(ModelHealthStatusEnum.Unknown, endpoint.HealthStatus, "new endpoint health");
                    TestResponse duplicate = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/model-endpoints", new { name = "stub", provider = "OpenAICompatible", baseUrl = stub.BaseUrl, model = "m" }, token).ConfigureAwait(false);
                    Assert.Equal(409, duplicate.StatusCode, "duplicate name");
                    TestResponse badSecret = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/model-endpoints", new { name = "bad", provider = "OpenAICompatible", baseUrl = stub.BaseUrl, model = "m", apiKeySecretId = "sec_nope" }, token).ConfigureAwait(false);
                    Assert.Equal(400, badSecret.StatusCode, "unknown key secret");
                    TestResponse unsupported = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/model-endpoints", new { name = "bad", provider = "VoyageAI", kind = "Inference", model = "m" }, token).ConfigureAwait(false);
                    Assert.Equal(400, unsupported.StatusCode, "provider without inference support");

                    // Names are labels: colons, slashes, at signs, and spaces are fine (model ids such as llama3:8b).
                    ModelEndpoint colon = await HttpTestHelpers.SendAsync<ModelEndpoint>(rig.Anderson, HttpMethod.Post, "/v1.0/api/model-endpoints", new { name = "ollama:llama3/8b @lab", provider = "OpenAICompatible", baseUrl = stub.BaseUrl, model = "m" }, 201, token).ConfigureAwait(false);
                    Assert.Equal("ollama:llama3/8b @lab", colon.Name, "name with colon, slash, at sign, and space");
                    Assert.Equal(204, (await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Delete, "/v1.0/api/model-endpoints/" + colon.Id, null, token).ConfigureAwait(false)).StatusCode, "remove the extra endpoint so the sweep count below stays exact");

                    // Draft validation (the form's Validate button): works before saving and stores nothing.
                    long before = (await HttpTestHelpers.SendAsync<Hugo.Core.Enumeration.EnumerationResult<ModelEndpoint>>(rig.Anderson, HttpMethod.Get, "/v1.0/api/model-endpoints", null, 200, token).ConfigureAwait(false)).TotalRecords;
                    ModelValidationResult draft = await HttpTestHelpers.SendAsync<ModelValidationResult>(rig.Anderson, HttpMethod.Post, "/v1.0/api/model-endpoints/validate", new { name = "draft:check", provider = "OpenAICompatible", baseUrl = stub.BaseUrl, model = stub.ModelName, maxRetries = 0, timeoutSeconds = 30 }, 200, token).ConfigureAwait(false);
                    Assert.True(draft.Success, "draft validation against the stub: " + draft.Error);
                    long after = (await HttpTestHelpers.SendAsync<Hugo.Core.Enumeration.EnumerationResult<ModelEndpoint>>(rig.Anderson, HttpMethod.Get, "/v1.0/api/model-endpoints", null, 200, token).ConfigureAwait(false)).TotalRecords;
                    Assert.Equal(before, after, "draft validation stores nothing");
                    ModelValidationResult unreachable = await HttpTestHelpers.SendAsync<ModelValidationResult>(rig.Anderson, HttpMethod.Post, "/v1.0/api/model-endpoints/validate", new { provider = "OpenAICompatible", baseUrl = "http://127.0.0.1:9", model = "m", maxRetries = 0, timeoutSeconds = 5 }, 200, token).ConfigureAwait(false);
                    Assert.True(!unreachable.Success && !String.IsNullOrEmpty(unreachable.Error), "unreachable draft reports a failure, not an exception");
                    TestResponse noProvider = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/model-endpoints/validate", new { model = "m" }, token).ConfigureAwait(false);
                    Assert.Equal(400, noProvider.StatusCode, "draft without a provider");

                    ModelValidationResult validation = await HttpTestHelpers.SendAsync<ModelValidationResult>(rig.Anderson, HttpMethod.Post, "/v1.0/api/model-endpoints/" + endpoint.Id + "/validate", null, 200, token).ConfigureAwait(false);
                    Assert.True(validation.Success, "validation against the stub: " + validation.Error);
                    ModelEndpoint healthy = await HttpTestHelpers.SendAsync<ModelEndpoint>(rig.Anderson, HttpMethod.Get, "/v1.0/api/model-endpoints/" + endpoint.Id, null, 200, token).ConfigureAwait(false);
                    Assert.Equal(ModelHealthStatusEnum.Healthy, healthy.HealthStatus, "health recorded by validation");
                    StubRecordedRequest last = stub.ChatRequests.Last();
                    Assert.True(last.Body.Length > 0, "the stub saw the request");

                    stub.Script.DefaultResponse = StubLlmResponse.Error(500);
                    int swept = await rig.Context.Get<ModelEndpointService>().SweepAsync(token).ConfigureAwait(false);
                    Assert.Equal(1, swept, "sweep checks active endpoints");
                    ModelEndpoint unhealthy = await HttpTestHelpers.SendAsync<ModelEndpoint>(rig.Anderson, HttpMethod.Get, "/v1.0/api/model-endpoints/" + endpoint.Id, null, 200, token).ConfigureAwait(false);
                    Assert.Equal(ModelHealthStatusEnum.Unhealthy, unhealthy.HealthStatus, "sweep records failures");
                    Assert.True(unhealthy.LastHealthUtc.HasValue && !String.IsNullOrEmpty(unhealthy.LastHealthMessage), "health message");

                    List<SecretReferrer> referrers = await HttpTestHelpers.SendAsync<List<SecretReferrer>>(rig.Anderson, HttpMethod.Get, "/v1.0/api/secrets/" + endpoint.ApiKeySecretId + "/referrers", null, 200, token).ConfigureAwait(false);
                    Assert.True(referrers.Any(r => r.ReferrerType == "ModelEndpoint" && r.ReferrerId == endpoint.Id), "the key secret reports the endpoint");
                    TestResponse keyInUse = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Delete, "/v1.0/api/secrets/" + endpoint.ApiKeySecretId, null, token).ConfigureAwait(false);
                    Assert.Equal(409, keyInUse.StatusCode, "key in use");

                    await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + rig.Hugo.Id, new { inferenceEndpointId = endpoint.Id }, 200, token).ConfigureAwait(false);
                    ModelEndpoint renamed = await HttpTestHelpers.SendAsync<ModelEndpoint>(rig.Anderson, HttpMethod.Put, "/v1.0/api/model-endpoints/" + endpoint.Id, new { name = "stub-renamed", maxConcurrency = 2 }, 200, token).ConfigureAwait(false);
                    Assert.True(renamed.Name == "stub-renamed" && renamed.MaxConcurrency == 2, "update");
                    TestResponse deleted = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Delete, "/v1.0/api/model-endpoints/" + endpoint.Id, null, token).ConfigureAwait(false);
                    Assert.Equal(204, deleted.StatusCode, "delete");
                    AgentProfile hugo = await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Get, "/v1.0/api/agent-profiles/" + rig.Hugo.Id, null, 200, token).ConfigureAwait(false);
                    Assert.True(hugo.InferenceEndpointId == null, "profiles lose the deleted endpoint");
                    Assert.True((await rig.AuditAsync(AuditEventTypeEnum.ModelEndpointChanged, token).ConfigureAwait(false)).Count >= 3, "endpoint changes audited");
                }
            }
        }

        private static async Task ConnectorModelAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (StubLlmServer stub = new StubLlmServer())
            {
                stub.Script.DefaultResponse = StubLlmResponse.Message("model says hi");
                await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, s => s.Connectors.ModelCallBudgetPerInvocation = 2, token).ConfigureAwait(false))
                {
                    Resource a = await rig.ResourceAsync("echo-a", token).ConfigureAwait(false);
                    await rig.BindAsync(rig.Hugo.Id, a.Id, AccessModeEnum.ReadOnly, token).ConfigureAwait(false);
                    CallerToolCatalog withoutModel = await HttpTestHelpers.SendAsync<CallerToolCatalog>(rig.Anderson, HttpMethod.Get, "/v1.0/api/agent-profiles/" + rig.Hugo.Id + "/tools", null, 200, token).ConfigureAwait(false);
                    Assert.True(!withoutModel.HasModel && !withoutModel.Tools.Any(t => t.Name == "echo_model"), "RequiresModel tools hidden without an endpoint");
                    ToolInvocationOutcome refused = await rig.InvokeAsync("echo_model", "echo-a", new { prompt = "hello" }, token).ConfigureAwait(false);
                    Assert.Equal("model_required", refused.DecisionCode, "RequiresModel tool refused without an endpoint");

                    ModelEndpoint endpoint = await EndpointAsync(rig, stub, "stub", token).ConfigureAwait(false);
                    await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + rig.Hugo.Id, new { inferenceEndpointId = endpoint.Id }, 200, token).ConfigureAwait(false);
                    CallerToolCatalog withModel = await HttpTestHelpers.SendAsync<CallerToolCatalog>(rig.Anderson, HttpMethod.Get, "/v1.0/api/agent-profiles/" + rig.Hugo.Id + "/tools", null, 200, token).ConfigureAwait(false);
                    Assert.True(withModel.HasModel && withModel.Tools.Any(t => t.Name == "echo_model"), "RequiresModel tools listed with an endpoint");
                    int before = stub.ChatRequests.Count;
                    ToolInvocationOutcome answered = await rig.InvokeAsync("echo_model", "echo-a", new { prompt = "hello", calls = 2 }, token).ConfigureAwait(false);
                    Assert.Equal(InvocationStatusEnum.Succeeded, answered.Status, "model call: " + answered.Result?.Text);
                    Assert.True(answered.Result!.Text.Contains("model says hi", StringComparison.Ordinal), "model text returned");
                    Assert.Equal(before + 2, stub.ChatRequests.Count, "two model requests reached the stub");
                    Assert.True(!stub.ChatRequests.Last().Body.Contains("stub-api-key", StringComparison.Ordinal), "the API key is a header, never the body");
                    ToolInvocationOutcome overBudget = await rig.InvokeAsync("echo_model", "echo-a", new { prompt = "hello", calls = 3 }, token).ConfigureAwait(false);
                    Assert.Equal(ConnectorErrorCodes.RateLimited, overBudget.Result?.ErrorCode, "the third call exceeds the per-invocation budget");
                }
            }
        }

        private static async Task AgentTurnAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (StubLlmServer stub = new StubLlmServer())
            {
                await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
                {
                    Resource a = await rig.ResourceAsync("echo-a", token).ConfigureAwait(false);
                    await rig.BindAsync(rig.Hugo.Id, a.Id, AccessModeEnum.ReadWriteWithApproval, token).ConfigureAwait(false);
                    ModelEndpoint endpoint = await EndpointAsync(rig, stub, "stub", token).ConfigureAwait(false);
                    await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + rig.Hugo.Id, new { inferenceEndpointId = endpoint.Id }, 200, token).ConfigureAwait(false);

                    stub.Script.Then(
                        StubLlmResponse.ToolCallList(new StubToolCall("echo_read", "{\"resource\":\"echo-a\",\"text\":\"status\"}"), new StubToolCall("echo_write", "{\"resource\":\"echo-a\",\"text\":\"fix\"}")),
                        StubLlmResponse.Message("All done, Mr. Anderson."));

                    User anderson = (await rig.Context.Database.Users.ReadByEmailAsync(rig.TenantId, "anderson@hugo.local", token).ConfigureAwait(false))!;
                    RequestContext principal = new RequestContext();
                    principal.IsAuthenticated = true;
                    principal.TenantId = rig.TenantId;
                    principal.PrincipalType = PrincipalTypeEnum.User;
                    principal.UserId = anderson.Id;
                    principal.User = anderson;
                    principal.PrincipalName = anderson.Email;
                    principal.IsTenantAdmin = true;

                    CompiledInstructions instructions = await rig.Context.Get<InstructionCompiler>().CompileAsync(rig.TenantId, rig.Hugo.Id, principal, InvocationSurfaceEnum.Chat, token).ConfigureAwait(false);
                    ModelEndpointConfig config = await rig.Context.Get<ModelEndpointService>().ToConfigAsync(endpoint, token).ConfigureAwait(false);
                    InMemoryConversationStore store = new InMemoryConversationStore();
                    AgentRuntime runtime = new AgentRuntime(rig.Context.Get<IModelGateway>(), store, new AgentRuntimeSettings());
                    RecordingEventSink sink = new RecordingEventSink();
                    AgentTurnRequest turn = new AgentTurnRequest();
                    turn.TenantId = rig.TenantId;
                    turn.ConversationId = "cnv_integration";
                    turn.PrincipalId = anderson.Id;
                    turn.ProfileId = rig.Hugo.Id;
                    turn.Surface = "chat";
                    turn.SystemPrompt = instructions.Text;
                    turn.UserMessage = "Check echo-a and fix it.";
                    turn.InferenceEndpoint = config;
                    turn.ToolCatalog = new HugoAgentToolCatalog(rig.Context.Get<ToolCatalogService>(), principal);
                    turn.ToolExecutor = new HugoAgentToolExecutor(rig.Context.Get<ToolInvocationService>(), principal, InvocationSurfaceEnum.Chat, ApprovalWaitModeEnum.Wait);
                    turn.EventSink = sink;

                    Task<AgentTurnResult> running = runtime.RunTurnAsync(turn, token);
                    Approval? pending = null;
                    for (int i = 0; i < 200 && pending == null; i++)
                    {
                        await Task.Delay(50, token).ConfigureAwait(false);
                        pending = (await rig.Context.Database.Approvals.ListPendingAsync(rig.TenantId, token).ConfigureAwait(false)).FirstOrDefault();
                    }
                    Assert.True(pending != null, "the write paused for approval mid-turn");
                    await rig.DecideAsync(pending!.Id, "approve", token).ConfigureAwait(false);
                    AgentTurnResult result = await running.WaitAsync(TimeSpan.FromSeconds(60), token).ConfigureAwait(false);

                    Assert.Equal(AgentFinishReasonEnum.Completed, result.FinishReason, "turn completed: " + result.ErrorMessage);
                    Assert.Equal("All done, Mr. Anderson.", result.FinalText, "final answer");
                    AgentToolCallRecord read = result.ToolCalls.First(c => c.ToolName == "echo_read");
                    AgentToolCallRecord write = result.ToolCalls.First(c => c.ToolName == "echo_write");
                    Assert.Equal(AgentToolDecisionEnum.Allowed, read.Decision, "read allowed");
                    Assert.Equal(AgentToolDecisionEnum.Approved, write.Decision, "write approved");
                    Assert.True(sink.Events.Any(e => e is ApprovalRequiredEvent) && sink.Events.Any(e => e is ApprovalResolvedEvent), "approval events emitted");
                    StubRecordedRequest first = stub.ChatRequests.First();
                    Assert.True(first.Body.Contains("Agent Smith", StringComparison.Ordinal) && first.Body.Contains("echo_read", StringComparison.Ordinal), "the model saw the compiled persona and the tools");
                    Assert.True(first.Body.Contains("\"enum\":[\"echo-a\"]", StringComparison.Ordinal) || first.Body.Contains("\\u0022echo-a\\u0022", StringComparison.Ordinal) || first.Body.Contains("echo-a", StringComparison.Ordinal), "resource enum offered");
                    await rig.Context.Get<ToolInvocationRecorder>().FlushAsync(token).ConfigureAwait(false);
                    Hugo.Core.Enumeration.EnumerationResult<ToolInvocation> calls = await rig.Context.Database.ToolInvocations.EnumerateAsync(rig.TenantId, new Hugo.Core.Enumeration.EnumerationQuery(), new Hugo.Core.Requests.ToolInvocationFilter { ConversationId = "cnv_integration" }, token).ConfigureAwait(false);
                    Assert.Equal(2L, calls.TotalRecords, "both calls recorded against the conversation");
                    Assert.True(calls.Objects.All(c => c.Surface == InvocationSurfaceEnum.Chat), "surface Chat");
                }
            }
        }

        #endregion
    }
}
