namespace Test.Shared.Connectors.Agents
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Agents.A2A;
    using Touchstone.Core;

    /// <summary>
    /// <c>a2a-agent</c> connector cases: descriptor and validation, plus cases against <see cref="TestA2AAgent"/> (an
    /// in-process Voltaic A2A agent on 127.0.0.1): card resolution, delegation that completes, needs input, fails, or
    /// replies directly, artifacts, context continuation, streaming, task reads, timeouts, authentication, endpoint
    /// origin checks, and secret hygiene.
    /// </summary>
    public static class A2AAgentCases
    {
        #region Private-Members

        private static readonly string Token = "a2a-test-token-9a8b7c";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the cases.
        /// </summary>
        /// <param name="suiteId">Suite identifier.</param>
        /// <returns>Cases.</returns>
        public static List<TestCaseDescriptor> Build(string suiteId)
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(new TestCaseDescriptor(suiteId, "descriptor", "Descriptor: type, family, three tools with effects, slot, embedded instructions", Descriptor));
            cases.Add(new TestCaseDescriptor(suiteId, "validate", "Validation covers the card URL, skillId, timeout, booleans, and the bearer token", Validate));
            cases.Add(new TestCaseDescriptor(suiteId, "test-connection", "TestConnectionAsync resolves the card from a card URL or base URL and checks skillId", TestConnection));
            cases.Add(new TestCaseDescriptor(suiteId, "get-card", "a2a_get_card summarizes the card and fences its text", GetCard));
            cases.Add(new TestCaseDescriptor(suiteId, "delegate-completed", "a2a_delegate returns the final message, contextId, and skillId metadata", DelegateCompleted));
            cases.Add(new TestCaseDescriptor(suiteId, "delegate-context", "A follow-up with contextId stays in the same conversation", DelegateContext));
            cases.Add(new TestCaseDescriptor(suiteId, "delegate-input-required", "input-required is a normal result naming what is needed; answering with taskId completes", DelegateInputRequired));
            cases.Add(new TestCaseDescriptor(suiteId, "delegate-artifacts", "Artifacts are returned bounded, with data parts as JSON", DelegateArtifacts));
            cases.Add(new TestCaseDescriptor(suiteId, "delegate-failed-direct", "A failed task is upstream_error; a direct message reply is completed", DelegateFailedAndDirect));
            cases.Add(new TestCaseDescriptor(suiteId, "delegate-streaming", "With streaming on, delegation completes, collects artifacts, and stops at input-required", DelegateStreaming));
            cases.Add(new TestCaseDescriptor(suiteId, "get-task", "a2a_get_task reads a finished task and reports unknown tasks as not_found", GetTask));
            cases.Add(new TestCaseDescriptor(suiteId, "timeout", "A slow task times out with the taskId to check later", TimeoutCase));
            cases.Add(new TestCaseDescriptor(suiteId, "auth", "The bearer token is sent; a wrong token is auth_failed and never echoed", Auth));
            cases.Add(new TestCaseDescriptor(suiteId, "cross-origin", "A card that points the endpoint at another origin is refused", CrossOrigin));
            cases.Add(new TestCaseDescriptor(suiteId, "unreachable", "A closed port is connection_failed and a missing card is not_found", Unreachable));
            return cases;
        }

        #endregion

        #region Private-Methods

        private static ConnectorResourceContext Res(TestA2AAgent agent, NoPoolSessionPool pool, Dictionary<string, Dictionary<string, string>>? secrets = null, params string[] extra)
        {
            List<string> pairs = new List<string> { "agentCardUrl", agent.CardUrl };
            pairs.AddRange(extra);
            return AgentsTestHelper.Resource("a2a-agent", AgentsTestHelper.Settings(pairs.ToArray()), secrets, pool);
        }

        private static A2AAgentConnector Connector()
        {
            return new A2AAgentConnector(new A2AAgentConnectorOptions { PollInterval = TimeSpan.FromMilliseconds(100) });
        }

        private static Task Descriptor(CancellationToken token)
        {
            A2AAgentConnector c = new A2AAgentConnector();
            AgentsTestHelper.Assert(c.Descriptor.Type == "a2a-agent" && c.Descriptor.Family == "a2a", "type and family");
            AgentsTestHelper.Assert(c.Descriptor.Category == ConnectorCategoryEnum.Agent, "category");
            string[] names = c.Tools.Select(t => t.Descriptor.Name).ToArray();
            AgentsTestHelper.Assert(names.SequenceEqual(new[] { "a2a_get_card", "a2a_delegate", "a2a_get_task" }), "tool names: " + String.Join(",", names));
            AgentsTestHelper.Assert(AgentsTestHelper.Tool(c, "a2a_get_card").Descriptor.BaseEffect == EffectEnum.Read, "card is Read");
            AgentsTestHelper.Assert(AgentsTestHelper.Tool(c, "a2a_delegate").Descriptor.BaseEffect == EffectEnum.Write, "delegate is Write");
            AgentsTestHelper.Assert(AgentsTestHelper.Tool(c, "a2a_get_task").Descriptor.BaseEffect == EffectEnum.Read, "task is Read");
            AgentsTestHelper.Assert(c.Descriptor.SecretSlots.Any(s => s.Name == "bearerToken" && s.AcceptedKinds.Contains(SecretKindEnum.ApiToken)), "bearerToken slot");
            foreach (string key in new[] { "agentCardUrl", "skillId", "timeoutSeconds", "streaming", "allowCrossOriginEndpoint" })
                AgentsTestHelper.Assert(c.Descriptor.SettingsSchema.Find(key) != null, "setting " + key);
            AgentsTestHelper.Assert(c.Descriptor.DefaultInstructionsKey == "connector.a2a-agent.instructions", "instructions key");
            AgentsTestHelper.AssertContains(c.Descriptor.DefaultInstructions, "a2a_delegate", "instructions");
            AgentsTestHelper.Assert(!c.Descriptor.DefaultInstructions.Contains((char)0x2014), "no em-dash");

            using (NoPoolSessionPool pool = new NoPoolSessionPool())
            {
                ConnectorResourceContext r = AgentsTestHelper.Resource("a2a-agent", AgentsTestHelper.Settings("agentCardUrl", "http://127.0.0.1:1/"), null, pool);
                EffectClassification e = AgentsTestHelper.Classify(c, "a2a_delegate", "{\"message\":\"Restart the billing job\"}", AgentsTestHelper.Context(r));
                AgentsTestHelper.Assert(e.Effect == EffectEnum.Write, "delegate classifies Write");
                AgentsTestHelper.AssertContains(e.ApprovalSummary, "Delegate to remote agent remote", "summary names the target");
                AgentsTestHelper.AssertContains(e.ApprovalSummary, "Restart the billing job", "summary shows the message");
            }
            return Task.CompletedTask;
        }

        private static async Task Validate(CancellationToken token)
        {
            A2AAgentConnector c = new A2AAgentConnector();
            await ExpectValid(c, AgentsTestHelper.Settings("agentCardUrl", "https://agent.example.com/.well-known/agent-card.json", "skillId", "echo", "timeoutSeconds", "300", "streaming", "true"), null, token).ConfigureAwait(false);
            await ExpectError(c, AgentsTestHelper.Settings(), null, "agentCardUrl", "required", token).ConfigureAwait(false);
            await ExpectError(c, AgentsTestHelper.Settings("agentCardUrl", "agent.example.com"), null, "agentCardUrl", "invalid_format", token).ConfigureAwait(false);
            await ExpectError(c, AgentsTestHelper.Settings("agentCardUrl", "https://u:p@agent.example.com/"), null, "agentCardUrl", "invalid_format", token).ConfigureAwait(false);
            await ExpectError(c, AgentsTestHelper.Settings("agentCardUrl", "https://agent.example.com/", "skillId", "two words"), null, "skillId", "invalid_format", token).ConfigureAwait(false);
            await ExpectError(c, AgentsTestHelper.Settings("agentCardUrl", "https://agent.example.com/", "timeoutSeconds", "0"), null, "timeoutSeconds", "out_of_range", token).ConfigureAwait(false);
            await ExpectError(c, AgentsTestHelper.Settings("agentCardUrl", "https://agent.example.com/", "streaming", "sometimes"), null, "streaming", "invalid_value", token).ConfigureAwait(false);
            Dictionary<string, Dictionary<string, string>> empty = new Dictionary<string, Dictionary<string, string>> { { "bearerToken", new Dictionary<string, string> { { "token", " " } } } };
            await ExpectError(c, AgentsTestHelper.Settings("agentCardUrl", "https://agent.example.com/"), empty, "bearerToken", "required", token).ConfigureAwait(false);
        }

        private static async Task TestConnection(CancellationToken token)
        {
            A2AAgentConnector c = Connector();
            using (TestA2AAgent agent = await TestA2AAgent.StartAsync(token).ConfigureAwait(false))
            using (NoPoolSessionPool pool = new NoPoolSessionPool())
            {
                ConnectorHealthResult byCard = await c.TestConnectionAsync(Res(agent, pool, null, "skillId", "echo"), token).ConfigureAwait(false);
                AgentsTestHelper.Assert(byCard.Success, "card URL: " + byCard.ErrorCode + " " + byCard.Message);
                AgentsTestHelper.Assert(byCard.Details["agent"] == "Echo Agent", "agent name");
                AgentsTestHelper.Assert(byCard.Details["skills"] == "2", "skills");
                AgentsTestHelper.Assert(byCard.Details["binding"] == "JSONRPC", "binding");
                AgentsTestHelper.Assert(byCard.Details["streaming"] == "true", "streaming");

                ConnectorResourceContext byBase = AgentsTestHelper.Resource("a2a-agent", AgentsTestHelper.Settings("agentCardUrl", agent.BaseUrl + "/"), null, pool);
                ConnectorHealthResult baseResult = await c.TestConnectionAsync(byBase, token).ConfigureAwait(false);
                AgentsTestHelper.Assert(baseResult.Success, "base URL: " + baseResult.ErrorCode + " " + baseResult.Message);

                ConnectorHealthResult badSkill = await c.TestConnectionAsync(Res(agent, pool, null, "skillId", "translate"), token).ConfigureAwait(false);
                AgentsTestHelper.Assert(!badSkill.Success && badSkill.ErrorCode == ConnectorErrorCodes.InvalidSetting, "unknown skill: " + badSkill.ErrorCode);
                AgentsTestHelper.AssertContains(badSkill.Message, "echo", "lists the available skills");
            }
        }

        private static async Task GetCard(CancellationToken token)
        {
            A2AAgentConnector c = Connector();
            using (TestA2AAgent agent = await TestA2AAgent.StartAsync(token).ConfigureAwait(false))
            using (NoPoolSessionPool pool = new NoPoolSessionPool())
            {
                ConnectorInvocationContext ctx = AgentsTestHelper.Context(Res(agent, pool, null, "skillId", "report", "streaming", "true"));
                ToolResult card = await AgentsTestHelper.RunAsync(c, "a2a_get_card", "{}", ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertOk(card, "get card");
                AgentsTestHelper.AssertContains(card.Text, "Name: Echo Agent 2.0.0", "name and version");
                AgentsTestHelper.AssertContains(card.Text, "- echo: Echo", "skill listed");
                AgentsTestHelper.AssertContains(card.Text, "example: say hello", "example listed");
                AgentsTestHelper.AssertContains(card.Text, "<untrusted-content source=\"agent card of Echo Agent\">", "card fenced");
                AgentsTestHelper.AssertContains(card.Text, "&lt;/untrusted-content>", "injected tag neutralized");
                AgentsTestHelper.Assert(card.Text.Split("</untrusted-content>").Length - 1 == 1, "one real fence close");
                AgentsTestHelper.AssertContains(card.Text, "Configured skill: report (on the card)", "configured skill found");
                AgentsTestHelper.AssertContains(card.Text, "(JSONRPC, streaming)", "binding and streaming");
                AgentsTestHelper.Assert(AgentsTestHelper.Structured(card, "skillCount") == "2", "structured skill count");
                AgentsTestHelper.Assert(AgentsTestHelper.Structured(card, "configuredSkillFound") == "true", "structured skill found");

                ToolResult limited = await AgentsTestHelper.RunAsync(c, "a2a_get_card", "{\"refresh\":true}", AgentsTestHelper.Context(ctx.Resource, 30, new OutputLimits { MaxRows = 1 }), token).ConfigureAwait(false);
                AgentsTestHelper.AssertOk(limited, "refresh with row limit");
                AgentsTestHelper.AssertContains(limited.Text, "Skills (2, first 1 shown)", "skills bounded");
            }
        }

        private static async Task DelegateCompleted(CancellationToken token)
        {
            A2AAgentConnector c = Connector();
            using (TestA2AAgent agent = await TestA2AAgent.StartAsync(token).ConfigureAwait(false))
            using (NoPoolSessionPool pool = new NoPoolSessionPool())
            {
                ConnectorInvocationContext ctx = AgentsTestHelper.Context(Res(agent, pool, null, "skillId", "echo"));
                ToolResult result = await AgentsTestHelper.RunAsync(c, "a2a_delegate", "{\"message\":\"hello there\"}", ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertOk(result, "delegate");
                AgentsTestHelper.AssertContains(result.Text, "Status: completed", "status");
                AgentsTestHelper.AssertContains(result.Text, "Echo: hello there skill=echo", "reply with skill metadata");
                AgentsTestHelper.AssertContains(result.Text, "<untrusted-content source=\"reply from remote agent Echo Agent\">", "reply fenced");
                AgentsTestHelper.Assert(AgentsTestHelper.Structured(result, "status") == "completed", "structured status");
                AgentsTestHelper.Assert(AgentsTestHelper.Structured(result, "terminal") == "true", "terminal");
                AgentsTestHelper.Assert(!String.IsNullOrEmpty(AgentsTestHelper.Structured(result, "taskId")), "taskId");
                AgentsTestHelper.Assert(!String.IsNullOrEmpty(AgentsTestHelper.Structured(result, "contextId")), "contextId");
                AgentsTestHelper.AssertContains(AgentsTestHelper.Structured(result, "next"), "contextId", "next explains follow-up");
                AgentsTestHelper.Assert(agent.Handler.SkillIds.Contains("echo"), "skillId metadata reached the agent");
            }
        }

        private static async Task DelegateContext(CancellationToken token)
        {
            A2AAgentConnector c = Connector();
            using (TestA2AAgent agent = await TestA2AAgent.StartAsync(token).ConfigureAwait(false))
            using (NoPoolSessionPool pool = new NoPoolSessionPool())
            {
                ConnectorInvocationContext ctx = AgentsTestHelper.Context(Res(agent, pool));
                ToolResult first = await AgentsTestHelper.RunAsync(c, "a2a_delegate", "{\"message\":\"first\"}", ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertOk(first, "first");
                string contextId = AgentsTestHelper.Structured(first, "contextId") ?? String.Empty;
                ToolResult second = await AgentsTestHelper.RunAsync(c, "a2a_delegate", AgentsTestHelper.Json(new { message = "second", contextId }), ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertOk(second, "second");
                AgentsTestHelper.Assert(AgentsTestHelper.Structured(second, "contextId") == contextId, "same context");
                AgentsTestHelper.AssertContains(second.Text, "ctx=" + contextId, "agent saw the context");
                AgentsTestHelper.Assert(AgentsTestHelper.Structured(second, "taskId") != AgentsTestHelper.Structured(first, "taskId"), "a new task");
            }
        }

        private static async Task DelegateInputRequired(CancellationToken token)
        {
            A2AAgentConnector c = Connector();
            using (TestA2AAgent agent = await TestA2AAgent.StartAsync(token).ConfigureAwait(false))
            using (NoPoolSessionPool pool = new NoPoolSessionPool())
            {
                ConnectorInvocationContext ctx = AgentsTestHelper.Context(Res(agent, pool));
                ToolResult ask = await AgentsTestHelper.RunAsync(c, "a2a_delegate", "{\"message\":\"deploy the app, need input\"}", ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertOk(ask, "input-required is not an error");
                AgentsTestHelper.AssertContains(ask.Text, "Status: input-required", "status");
                AgentsTestHelper.AssertContains(ask.Text, "needs more input", "explains the wait");
                AgentsTestHelper.AssertContains(ask.Text, "Which environment: staging or production?", "the question");
                AgentsTestHelper.Assert(AgentsTestHelper.Structured(ask, "needsInput") == "true", "needsInput");
                AgentsTestHelper.Assert(AgentsTestHelper.Structured(ask, "terminal") == "false", "not terminal");
                string taskId = AgentsTestHelper.Structured(ask, "taskId") ?? String.Empty;
                string contextId = AgentsTestHelper.Structured(ask, "contextId") ?? String.Empty;
                AgentsTestHelper.AssertContains(AgentsTestHelper.Structured(ask, "next"), taskId, "next names the task");

                ToolResult answer = await AgentsTestHelper.RunAsync(c, "a2a_delegate", AgentsTestHelper.Json(new { message = "staging", taskId, contextId }), ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertOk(answer, "answer");
                AgentsTestHelper.AssertContains(answer.Text, "Thanks, deploying to staging.", "continued");
                AgentsTestHelper.Assert(AgentsTestHelper.Structured(answer, "taskId") == taskId, "same task");
                AgentsTestHelper.Assert(AgentsTestHelper.Structured(answer, "status") == "completed", "completed");

                EffectClassification e = AgentsTestHelper.Classify(c, "a2a_delegate", AgentsTestHelper.Json(new { message = "staging", taskId }), ctx);
                AgentsTestHelper.AssertContains(e.ApprovalSummary, "answering task " + taskId, "summary shows the continuation");
            }
        }

        private static async Task DelegateArtifacts(CancellationToken token)
        {
            A2AAgentConnector c = Connector();
            using (TestA2AAgent agent = await TestA2AAgent.StartAsync(token).ConfigureAwait(false))
            using (NoPoolSessionPool pool = new NoPoolSessionPool())
            {
                ConnectorInvocationContext ctx = AgentsTestHelper.Context(Res(agent, pool));
                ToolResult result = await AgentsTestHelper.RunAsync(c, "a2a_delegate", "{\"message\":\"weekly report please\",\"data\":{\"week\":42}}", ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertOk(result, "report");
                AgentsTestHelper.AssertContains(result.Text, "Artifacts: 1", "artifact count");
                AgentsTestHelper.AssertContains(result.Text, "artifact body line", "artifact text");
                AgentsTestHelper.AssertContains(result.Text, "{\"count\":3}", "data part as JSON");
                AgentsTestHelper.AssertContains(result.Text, "Description: Weekly report", "artifact description");
                string artifacts = AgentsTestHelper.Structured(result, "artifacts") ?? String.Empty;
                AgentsTestHelper.AssertContains(artifacts, "\"name\":\"report\"", "structured artifact");
                AgentsTestHelper.Assert(AgentsTestHelper.Structured(result, "artifactCount") == "1", "structured count");

                ToolResult bounded = await AgentsTestHelper.RunAsync(c, "a2a_delegate", "{\"message\":\"report again\"}", AgentsTestHelper.Context(ctx.Resource, 30, new OutputLimits { MaxBytes = 1024 }), token).ConfigureAwait(false);
                AgentsTestHelper.AssertOk(bounded, "bounded");
                AgentsTestHelper.Assert(System.Text.Encoding.UTF8.GetByteCount(bounded.Text) <= 1024 + 100, "output bounded by MaxBytes");
            }
        }

        private static async Task DelegateFailedAndDirect(CancellationToken token)
        {
            A2AAgentConnector c = Connector();
            using (TestA2AAgent agent = await TestA2AAgent.StartAsync(token).ConfigureAwait(false))
            using (NoPoolSessionPool pool = new NoPoolSessionPool())
            {
                ConnectorInvocationContext ctx = AgentsTestHelper.Context(Res(agent, pool));
                ToolResult failed = await AgentsTestHelper.RunAsync(c, "a2a_delegate", "{\"message\":\"please fail\"}", ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertError(failed, ConnectorErrorCodes.UpstreamError, "failed task");
                AgentsTestHelper.AssertContains(failed.Text, "ended as failed", "status named");
                AgentsTestHelper.AssertContains(failed.Text, "Cannot do that: the backend is down.", "agent message");
                AgentsTestHelper.Assert(AgentsTestHelper.Structured(failed, "status") == "failed", "structured status");

                ToolResult direct = await AgentsTestHelper.RunAsync(c, "a2a_delegate", "{\"message\":\"direct answer please\"}", ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertOk(direct, "direct reply");
                AgentsTestHelper.AssertContains(direct.Text, "direct reply", "message text");
                AgentsTestHelper.Assert(AgentsTestHelper.Structured(direct, "status") == "completed", "direct is completed");
            }
        }

        private static async Task DelegateStreaming(CancellationToken token)
        {
            A2AAgentConnector c = Connector();
            using (TestA2AAgent agent = await TestA2AAgent.StartAsync(token).ConfigureAwait(false))
            using (NoPoolSessionPool pool = new NoPoolSessionPool())
            {
                ConnectorInvocationContext ctx = AgentsTestHelper.Context(Res(agent, pool, null, "streaming", "true"));
                ToolResult done = await AgentsTestHelper.RunAsync(c, "a2a_delegate", "{\"message\":\"stream a report\"}", ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertOk(done, "streamed delegation");
                AgentsTestHelper.AssertContains(done.Text, "Status: completed", "completed");
                AgentsTestHelper.AssertContains(done.Text, "Echo: stream a report", "final message");
                AgentsTestHelper.AssertContains(done.Text, "artifact body line", "streamed artifact");

                ToolResult ask = await AgentsTestHelper.RunAsync(c, "a2a_delegate", "{\"message\":\"need input to continue\"}", ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertOk(ask, "streamed input-required");
                AgentsTestHelper.Assert(AgentsTestHelper.Structured(ask, "needsInput") == "true", "needsInput over a stream");
                string taskId = AgentsTestHelper.Structured(ask, "taskId") ?? String.Empty;
                string contextId = AgentsTestHelper.Structured(ask, "contextId") ?? String.Empty;
                ToolResult answer = await AgentsTestHelper.RunAsync(c, "a2a_delegate", AgentsTestHelper.Json(new { message = "production", taskId, contextId }), ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertOk(answer, "streamed answer");
                AgentsTestHelper.AssertContains(answer.Text, "Thanks, deploying to production.", "continued over a stream");
            }
        }

        private static async Task GetTask(CancellationToken token)
        {
            A2AAgentConnector c = Connector();
            using (TestA2AAgent agent = await TestA2AAgent.StartAsync(token).ConfigureAwait(false))
            using (NoPoolSessionPool pool = new NoPoolSessionPool())
            {
                ConnectorInvocationContext ctx = AgentsTestHelper.Context(Res(agent, pool));
                ToolResult delegated = await AgentsTestHelper.RunAsync(c, "a2a_delegate", "{\"message\":\"report for get task\"}", ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertOk(delegated, "delegate");
                string taskId = AgentsTestHelper.Structured(delegated, "taskId") ?? String.Empty;

                ToolResult task = await AgentsTestHelper.RunAsync(c, "a2a_get_task", AgentsTestHelper.Json(new { taskId, historyLength = 10 }), ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertOk(task, "get task");
                AgentsTestHelper.AssertContains(task.Text, "Status: completed", "status");
                AgentsTestHelper.AssertContains(task.Text, "Echo: report for get task", "message");
                AgentsTestHelper.AssertContains(task.Text, "artifact body line", "artifact");
                AgentsTestHelper.Assert(AgentsTestHelper.Structured(task, "taskId") == taskId, "same id");

                ToolResult unknown = await AgentsTestHelper.RunAsync(c, "a2a_get_task", "{\"taskId\":\"no-such-task\"}", ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertError(unknown, ConnectorErrorCodes.NotFound, "unknown task");
            }
        }

        private static async Task TimeoutCase(CancellationToken token)
        {
            A2AAgentConnector c = Connector();
            using (TestA2AAgent agent = await TestA2AAgent.StartAsync(token).ConfigureAwait(false))
            using (NoPoolSessionPool pool = new NoPoolSessionPool())
            {
                ConnectorInvocationContext ctx = AgentsTestHelper.Context(Res(agent, pool, null, "streaming", "true", "timeoutSeconds", "2"));
                ToolResult slow = await AgentsTestHelper.RunAsync(c, "a2a_delegate", "{\"message\":\"slow job\"}", ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertError(slow, ConnectorErrorCodes.Timeout, "slow delegation");
                string? taskId = AgentsTestHelper.Structured(slow, "taskId");
                AgentsTestHelper.Assert(!String.IsNullOrEmpty(taskId), "streaming timeout reports the taskId");
                AgentsTestHelper.AssertContains(slow.Text, "a2a_get_task", "points at a2a_get_task");

                ToolResult task = await AgentsTestHelper.RunAsync(c, "a2a_get_task", AgentsTestHelper.Json(new { taskId }), ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertOk(task, "task readable after timeout");

                ConnectorInvocationContext blocking = AgentsTestHelper.Context(Res(agent, pool, null, "timeoutSeconds", "1"));
                ToolResult slowBlocking = await AgentsTestHelper.RunAsync(c, "a2a_delegate", "{\"message\":\"slow again\"}", blocking, token).ConfigureAwait(false);
                AgentsTestHelper.AssertError(slowBlocking, ConnectorErrorCodes.Timeout, "blocking timeout");
            }
        }

        private static async Task Auth(CancellationToken token)
        {
            A2AAgentConnector c = Connector();
            using (TestA2AAgent agent = await TestA2AAgent.StartAsync(token).ConfigureAwait(false))
            {
                agent.RequiredAuthorization = "Bearer " + Token;
                Dictionary<string, Dictionary<string, string>> secrets = new Dictionary<string, Dictionary<string, string>> { { "bearerToken", new Dictionary<string, string> { { "token", Token } } } };
                using (NoPoolSessionPool pool = new NoPoolSessionPool())
                {
                    ToolResult ok = await AgentsTestHelper.RunAsync(c, "a2a_delegate", "{\"message\":\"authorized hello\"}", AgentsTestHelper.Context(Res(agent, pool, secrets)), token).ConfigureAwait(false);
                    AgentsTestHelper.AssertOk(ok, "authorized");
                    AgentsTestHelper.Assert(agent.AuthorizationHeaders.Any() && agent.AuthorizationHeaders.All(h => h == "Bearer " + Token), "bearer token sent");
                    AgentsTestHelper.AssertNoSecret(ok, Token, "delegate result");
                }

                Dictionary<string, Dictionary<string, string>> wrong = new Dictionary<string, Dictionary<string, string>> { { "bearerToken", new Dictionary<string, string> { { "token", "wrong-" + Token } } } };
                using (NoPoolSessionPool pool = new NoPoolSessionPool())
                {
                    ToolResult denied = await AgentsTestHelper.RunAsync(c, "a2a_delegate", "{\"message\":\"hello\"}", AgentsTestHelper.Context(Res(agent, pool, wrong)), token).ConfigureAwait(false);
                    AgentsTestHelper.AssertError(denied, ConnectorErrorCodes.AuthFailed, "wrong token");
                    AgentsTestHelper.AssertNoSecret(denied, Token, "auth error");
                }
            }
        }

        private static async Task CrossOrigin(CancellationToken token)
        {
            A2AAgentConnector c = Connector();
            using (TestA2AAgent agent = await TestA2AAgent.StartAsync(token, "agents.example.invalid").ConfigureAwait(false))
            using (NoPoolSessionPool pool = new NoPoolSessionPool())
            {
                ToolResult refused = await AgentsTestHelper.RunAsync(c, "a2a_get_card", "{}", AgentsTestHelper.Context(Res(agent, pool)), token).ConfigureAwait(false);
                AgentsTestHelper.AssertError(refused, ConnectorErrorCodes.PermissionDenied, "cross-origin endpoint");
                AgentsTestHelper.AssertContains(refused.Text, "allowCrossOriginEndpoint", "names the setting");
                ConnectorHealthResult health = await c.TestConnectionAsync(Res(agent, pool), token).ConfigureAwait(false);
                AgentsTestHelper.Assert(!health.Success && health.ErrorCode == ConnectorErrorCodes.PermissionDenied, "health refuses too");

                ToolResult allowed = await AgentsTestHelper.RunAsync(c, "a2a_get_card", "{}", AgentsTestHelper.Context(Res(agent, pool, null, "allowCrossOriginEndpoint", "true")), token).ConfigureAwait(false);
                AgentsTestHelper.AssertOk(allowed, "allowed when opted in");
                AgentsTestHelper.AssertContains(allowed.Text, "agents.example.invalid", "endpoint shown");
            }
        }

        private static async Task Unreachable(CancellationToken token)
        {
            A2AAgentConnector c = Connector();
            using (NoPoolSessionPool pool = new NoPoolSessionPool())
            {
                int port = AgentsTestHelper.FreePort();
                ConnectorResourceContext r = AgentsTestHelper.Resource("a2a-agent", AgentsTestHelper.Settings("agentCardUrl", "http://127.0.0.1:" + port + "/"), null, pool);
                ToolResult closed = await AgentsTestHelper.RunAsync(c, "a2a_get_card", "{}", AgentsTestHelper.Context(r), token).ConfigureAwait(false);
                AgentsTestHelper.AssertError(closed, ConnectorErrorCodes.ConnectionFailed, "closed port");
                ConnectorHealthResult health = await c.TestConnectionAsync(r, token).ConfigureAwait(false);
                AgentsTestHelper.Assert(!health.Success && health.ErrorCode == ConnectorErrorCodes.ConnectionFailed, "health connection_failed: " + health.ErrorCode);
            }
            using (TestA2AAgent agent = await TestA2AAgent.StartAsync(token).ConfigureAwait(false))
            using (NoPoolSessionPool pool = new NoPoolSessionPool())
            {
                ConnectorResourceContext r = AgentsTestHelper.Resource("a2a-agent", AgentsTestHelper.Settings("agentCardUrl", agent.BaseUrl + "/cards/missing.json"), null, pool);
                ConnectorHealthResult health = await c.TestConnectionAsync(r, token).ConfigureAwait(false);
                AgentsTestHelper.Assert(!health.Success && health.ErrorCode == ConnectorErrorCodes.NotFound, "missing card not_found: " + health.ErrorCode + " " + health.Message);
            }
        }

        private static async Task ExpectValid(A2AAgentConnector c, Dictionary<string, string> settings, Dictionary<string, Dictionary<string, string>>? secrets, CancellationToken token)
        {
            using (NoPoolSessionPool pool = new NoPoolSessionPool())
            {
                ConnectorValidationResult v = await c.ValidateAsync(AgentsTestHelper.Resource("a2a-agent", settings, secrets, pool), token).ConfigureAwait(false);
                AgentsTestHelper.Assert(v.IsValid, "expected valid but got: " + String.Join("; ", v.Errors.Select(e => e.Field + "/" + e.Code + ": " + e.Message)));
            }
        }

        private static async Task ExpectError(A2AAgentConnector c, Dictionary<string, string> settings, Dictionary<string, Dictionary<string, string>>? secrets, string field, string code, CancellationToken token)
        {
            using (NoPoolSessionPool pool = new NoPoolSessionPool())
            {
                ConnectorValidationResult v = await c.ValidateAsync(AgentsTestHelper.Resource("a2a-agent", settings, secrets, pool), token).ConfigureAwait(false);
                AgentsTestHelper.Assert(v.Errors.Any(e => e.Field == field && e.Code == code), "expected " + field + "/" + code + " but got: " + String.Join("; ", v.Errors.Select(e => e.Field + "/" + e.Code)));
            }
        }

        #endregion
    }
}
