namespace Test.Shared.Chat
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Agent;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Responses;
    using Hugo.Core.Security;
    using Hugo.Core.Serialization;
    using Hugo.Server.Services;
    using Test.Shared.Agent.StubLlm;
    using Test.Shared.Infrastructure;
    using Test.Shared.Integration;
    using Touchstone.Core;

    /// <summary>
    /// Conversations and dashboard chat through the real stack (P6.15 to P6.18, P6.20, P6.23, P6.24): the stub LLM, the
    /// echo connector, the tool invocation pipeline, approvals decided over REST, SSE event order, cancel, 409, export,
    /// analytics, and delegation, on every configured provider.
    /// </summary>
    public static class ChatSuite
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
                string id = "chat-" + provider.ToString().ToLowerInvariant();
                List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
                cases.Add(new TestCaseDescriptor(id, "data", "Conversation, message, and A2A task data methods (" + provider + ")", ct => DataAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "crud-visibility", "Conversation CRUD; users see their own, tenant administrators see all (" + provider + ")", ct => CrudVisibilityAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "sse-approval", "SSE turn with D-022 event order and an approval decided over REST mid-turn (" + provider + ")", ct => SseApprovalAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "json-export-analytics", "JSON turn, token accounting, auto title, export, and analytics (" + provider + ")", ct => JsonExportAnalyticsAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "cancel-conflict", "A second turn is refused with 409 and the cancel route stops the running turn (" + provider + ")", ct => CancelConflictAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "delegation", "Delegation service and REST ask: tool trace, approval pending, and the follow-up after the decision (" + provider + ")", ct => DelegationAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "errors", "No inference endpoint, empty message, and foreign conversations (" + provider + ")", ct => ErrorsAsync(provider, ct)));
                suites.Add(new TestSuiteDescriptor(id, "Conversations and chat (" + provider + ")", cases));
            }
            return suites;
        }

        /// <summary>
        /// Anderson's request context, for calling services directly.
        /// </summary>
        /// <param name="rig">Rig.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Context.</returns>
        public static async Task<RequestContext> AndersonAsync(IntegrationRig rig, CancellationToken token)
        {
            User anderson = (await rig.Context.Database.Users.ReadByEmailAsync(rig.TenantId, "anderson@hugo.local", token).ConfigureAwait(false))!;
            RequestContext principal = new RequestContext();
            principal.IsAuthenticated = true;
            principal.TenantId = rig.TenantId;
            principal.PrincipalType = PrincipalTypeEnum.User;
            principal.UserId = anderson.Id;
            principal.User = anderson;
            principal.PrincipalName = anderson.Email;
            principal.IsTenantAdmin = true;
            return principal;
        }

        #endregion

        #region Private-Methods

        private static async Task DataAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                Hugo.Core.Database.DatabaseDriverBase db = rig.Context.Database;
                Conversation conversation = new Conversation();
                conversation.TenantId = rig.TenantId;
                conversation.UserId = "usr_data";
                conversation.AgentProfileId = rig.Hugo.Id;
                conversation.Source = ConversationSourceEnum.OpenAI;
                conversation.ExternalKey = "session-1";
                await db.Conversations.CreateAsync(conversation, token).ConfigureAwait(false);

                ConversationMessage user = new ConversationMessage { Role = AgentMessageRoleEnum.User, Content = "hello" };
                ConversationMessage assistant = new ConversationMessage { Role = AgentMessageRoleEnum.Assistant, Content = String.Empty, InputTokens = 10, OutputTokens = 4 };
                assistant.ToolCalls.Add(new ConversationToolCall { CallId = "call_1", Name = "echo_read", ArgumentsJson = "{\"text\":\"a\"}" });
                assistant.ToolCalls.Add(new ConversationToolCall { CallId = "call_2", Name = "echo_write", ArgumentsJson = "{\"text\":\"b\"}" });
                ConversationMessage tool = new ConversationMessage { Role = AgentMessageRoleEnum.Tool, Content = "<tool_output/>", ToolCallId = "call_1", ToolName = "echo_read" };
                await db.Messages.AppendAsync(rig.TenantId, conversation.Id, new List<ConversationMessage> { user, assistant }, token).ConfigureAwait(false);
                await db.Messages.AppendAsync(rig.TenantId, conversation.Id, new List<ConversationMessage> { tool }, token).ConfigureAwait(false);

                List<ConversationMessage> all = await db.Messages.ListAsync(rig.TenantId, conversation.Id, false, token).ConfigureAwait(false);
                Assert.Equal(3, all.Count, "messages");
                Assert.True(all.Select(m => m.Sequence).SequenceEqual(new long[] { 1, 2, 3 }), "sequences 1..3");
                Assert.True(all[1].ToolCalls.Count == 2 && all[1].ToolCalls[0].CallId == "call_1" && all[1].ToolCalls[1].Name == "echo_write", "tool calls in order");
                Conversation? reread = await db.Conversations.ReadAsync(rig.TenantId, conversation.Id, token).ConfigureAwait(false);
                Assert.True(reread != null && reread.MessageCount == 3 && reread.LastMessageUtc.HasValue, "message count and last message time");
                Assert.True(await db.Conversations.ReadAsync("other-tenant", conversation.Id, token).ConfigureAwait(false) == null, "tenant scoped read");

                await db.Messages.MarkCompactedAsync(rig.TenantId, conversation.Id, new List<string> { user.Id, assistant.Id }, token).ConfigureAwait(false);
                List<ConversationMessage> active = await db.Messages.ListAsync(rig.TenantId, conversation.Id, true, token).ConfigureAwait(false);
                Assert.True(active.Count == 1 && active[0].Id == tool.Id, "compacted messages leave the active history");
                await db.Messages.UpdateTurnAsync(rig.TenantId, assistant.Id, 120, 900, "completed", token).ConfigureAwait(false);
                ConversationMessage? turned = await db.Messages.ReadAsync(rig.TenantId, assistant.Id, token).ConfigureAwait(false);
                Assert.True(turned != null && turned.TimeToFirstTokenMs == 120 && turned.DurationMs == 900 && turned.FinishReason == "completed" && turned.ToolCalls.Count == 2, "turn metrics");

                await db.Conversations.AddUsageAsync(rig.TenantId, conversation.Id, 30, 7, token).ConfigureAwait(false);
                Conversation? keyed = await db.Conversations.ReadByExternalKeyAsync(rig.TenantId, rig.Hugo.Id, "usr_data", null, "session-1", token).ConfigureAwait(false);
                Assert.True(keyed != null && keyed.Id == conversation.Id && keyed.TotalInputTokens == 30 && keyed.TotalOutputTokens == 7, "external key lookup and usage");
                Assert.True(await db.Conversations.ReadByExternalKeyAsync(rig.TenantId, rig.Hugo.Id, "usr_other", null, "session-1", token).ConfigureAwait(false) == null, "external keys are per principal");

                A2ATask task = new A2ATask { TenantId = rig.TenantId, AgentProfileId = rig.Hugo.Id, PrincipalId = "usr_data", ConversationId = conversation.Id, ContextId = "ctx-1", State = A2ATaskStateEnum.Working };
                await db.A2ATasks.CreateAsync(task, token).ConfigureAwait(false);
                await db.A2ATasks.AddArtifactAsync(new A2ATaskArtifact { TaskId = task.Id, TenantId = rig.TenantId, ArtifactId = "a1", Name = "result", Content = "done" }, token).ConfigureAwait(false);
                task.State = A2ATaskStateEnum.Completed;
                task.StatusMessage = "done";
                await db.A2ATasks.UpdateAsync(task, token).ConfigureAwait(false);
                EnumerationResult<A2ATask> mine = await db.A2ATasks.EnumerateAsync(rig.TenantId, rig.Hugo.Id, "usr_data", "ctx-1", A2ATaskStateEnum.Completed, new EnumerationQuery(), token).ConfigureAwait(false);
                Assert.Equal(1L, mine.TotalRecords, "task enumeration with filters");
                EnumerationResult<A2ATask> others = await db.A2ATasks.EnumerateAsync(rig.TenantId, rig.Hugo.Id, "usr_other", null, null, new EnumerationQuery(), token).ConfigureAwait(false);
                Assert.Equal(0L, others.TotalRecords, "principal filter");
                Assert.Equal(1, (await db.A2ATasks.ListArtifactsAsync(rig.TenantId, task.Id, token).ConfigureAwait(false)).Count, "artifacts");
                Assert.Equal(0, (await db.A2ATasks.ListByStateAsync(new List<A2ATaskStateEnum> { A2ATaskStateEnum.Working }, token).ConfigureAwait(false)).Count, "no working tasks left");

                Assert.True(await db.Conversations.DeleteAsync(rig.TenantId, conversation.Id, token).ConfigureAwait(false), "delete");
                Assert.Equal(0, (await db.Messages.ListAsync(rig.TenantId, conversation.Id, false, token).ConfigureAwait(false)).Count, "messages deleted with the conversation");
                Assert.True(await db.A2ATasks.ReadAsync(rig.TenantId, task.Id, token).ConfigureAwait(false) == null, "tasks deleted with the conversation");
            }
        }

        private static async Task CrudVisibilityAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                Conversation mine = await ChatTestSupport.ConversationAsync(rig.Anderson, token, "Anderson's").ConfigureAwait(false);
                Assert.True(mine.Id.StartsWith("cnv_", StringComparison.Ordinal) && mine.AgentProfileId == rig.Hugo.Id && mine.Source == ConversationSourceEnum.Dashboard, "created with the default profile");
                Conversation renamed = await HttpTestHelpers.SendAsync<Conversation>(rig.Anderson, HttpMethod.Put, "/v1.0/api/conversations/" + mine.Id, new { title = "Renamed" }, 200, token).ConfigureAwait(false);
                Assert.Equal("Renamed", renamed.Title, "rename");

                using (HttpClient operatorClient = await ChatTestSupport.UserAsync(rig, "operator@chat.local", "Operator", token).ConfigureAwait(false))
                using (HttpClient otherClient = await ChatTestSupport.UserAsync(rig, "other@chat.local", "Operator", token).ConfigureAwait(false))
                {
                    Conversation theirs = await ChatTestSupport.ConversationAsync(operatorClient, token, "Operator's").ConfigureAwait(false);
                    TestResponse hidden = await HttpTestHelpers.SendAsync(operatorClient, HttpMethod.Get, "/v1.0/api/conversations/" + mine.Id, null, token).ConfigureAwait(false);
                    Assert.Equal(404, hidden.StatusCode, "a user cannot read another user's conversation");
                    TestResponse hiddenMessages = await HttpTestHelpers.SendAsync(otherClient, HttpMethod.Get, "/v1.0/api/conversations/" + theirs.Id + "/messages", null, token).ConfigureAwait(false);
                    Assert.Equal(404, hiddenMessages.StatusCode, "or its messages");
                    TestResponse hiddenSend = await HttpTestHelpers.SendAsync(otherClient, HttpMethod.Post, "/v1.0/api/conversations/" + theirs.Id + "/messages", new { message = "hi" }, token).ConfigureAwait(false);
                    Assert.Equal(404, hiddenSend.StatusCode, "or send to it");
                    TestResponse hiddenDelete = await HttpTestHelpers.SendAsync(otherClient, HttpMethod.Delete, "/v1.0/api/conversations/" + theirs.Id, null, token).ConfigureAwait(false);
                    Assert.Equal(404, hiddenDelete.StatusCode, "or delete it");

                    EnumerationResult<Conversation> operatorList = await HttpTestHelpers.SendAsync<EnumerationResult<Conversation>>(operatorClient, HttpMethod.Get, "/v1.0/api/conversations", null, 200, token).ConfigureAwait(false);
                    Assert.True(operatorList.Objects.Count == 1 && operatorList.Objects[0].Id == theirs.Id, "a user lists only their own conversations");
                    EnumerationResult<Conversation> adminList = await HttpTestHelpers.SendAsync<EnumerationResult<Conversation>>(rig.Anderson, HttpMethod.Get, "/v1.0/api/conversations", null, 200, token).ConfigureAwait(false);
                    Assert.True(adminList.Objects.Any(c => c.Id == theirs.Id) && adminList.Objects.Any(c => c.Id == mine.Id), "a tenant administrator lists every conversation");
                    Conversation seen = await HttpTestHelpers.SendAsync<Conversation>(rig.Anderson, HttpMethod.Get, "/v1.0/api/conversations/" + theirs.Id, null, 200, token).ConfigureAwait(false);
                    Assert.Equal("Operator's", seen.Title, "a tenant administrator reads another user's conversation");
                    EnumerationResult<Conversation> searched = await HttpTestHelpers.SendAsync<EnumerationResult<Conversation>>(rig.Anderson, HttpMethod.Get, "/v1.0/api/conversations?search=Renam", null, 200, token).ConfigureAwait(false);
                    Assert.True(searched.Objects.Count == 1 && searched.Objects[0].Id == mine.Id, "title search");

                    TestResponse ownDelete = await HttpTestHelpers.SendAsync(operatorClient, HttpMethod.Delete, "/v1.0/api/conversations/" + theirs.Id, null, token).ConfigureAwait(false);
                    Assert.Equal(204, ownDelete.StatusCode, "owners delete their own conversation without Delete on Conversation");
                }
                TestResponse deleted = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Delete, "/v1.0/api/conversations/" + mine.Id, null, token).ConfigureAwait(false);
                Assert.Equal(204, deleted.StatusCode, "delete");
                TestResponse gone = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Get, "/v1.0/api/conversations/" + mine.Id, null, token).ConfigureAwait(false);
                Assert.Equal(404, gone.StatusCode, "deleted");
            }
        }

        private static async Task SseApprovalAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (StubLlmServer stub = ChatTestSupport.NewStub())
            {
                await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
                {
                    Resource echo = await rig.ResourceAsync("echo-a", token).ConfigureAwait(false);
                    await rig.BindAsync(rig.Hugo.Id, echo.Id, AccessModeEnum.ReadWriteWithApproval, token).ConfigureAwait(false);
                    ModelEndpoint endpoint = await ChatTestSupport.UseStubAsync(rig, stub, token).ConfigureAwait(false);
                    stub.Script.Then(
                        StubLlmResponse.ToolCallList(new StubToolCall("echo_read", "{\"resource\":\"echo-a\",\"text\":\"status\"}"), new StubToolCall("echo_write", "{\"resource\":\"echo-a\",\"text\":\"fix\"}")),
                        StubLlmResponse.Message("All done, Mr. Anderson."));
                    Conversation conversation = await ChatTestSupport.ConversationAsync(rig.Anderson, token).ConfigureAwait(false);

                    Task<SseResult> streaming = ChatTestSupport.PostSseAsync(rig.Anderson, "/v1.0/api/conversations/" + conversation.Id + "/messages", "{\"message\":\"Check echo-a and fix it.\"}", token);
                    Approval pending = await ChatTestSupport.PendingApprovalAsync(rig, token).ConfigureAwait(false);
                    Assert.Equal("echo_write", pending.ToolName, "the write waits for approval");
                    await rig.DecideAsync(pending.Id, "approve", token).ConfigureAwait(false);
                    SseResult result = await streaming.WaitAsync(TimeSpan.FromSeconds(60), token).ConfigureAwait(false);

                    Assert.Equal(200, result.StatusCode, "stream status: " + result.Body);
                    Assert.Equal("text/event-stream", result.ContentType, "content type");
                    List<string> names = result.Events.Select(e => e.Event ?? String.Empty).Where(n => n != "heartbeat").ToList();
                    Assert.Equal("turn.started", names.First(), "first event");
                    Assert.Equal("stream.closed", names.Last(), "last event");
                    int proposed = names.IndexOf("tool.proposed");
                    int started = names.IndexOf("tool.started");
                    int required = names.IndexOf("approval.required");
                    int resolved = names.IndexOf("approval.resolved");
                    int completed = names.LastIndexOf("tool.completed");
                    int delta = names.LastIndexOf("assistant.delta");
                    int turnCompleted = names.IndexOf("turn.completed");
                    Assert.True(proposed > 0 && started > proposed && required > started && resolved > required && completed > resolved && delta > completed && turnCompleted > delta && turnCompleted < names.Count - 1,
                        "D-022 event order: " + String.Join(", ", names));
                    Assert.True(result.Events.Count(e => e.Event == "tool.completed") == 2, "both tools completed");
                    Assert.True(result.Events.First().Data.Contains("\"surface\":\"chat\"", StringComparison.Ordinal), "turn.started carries the surface");
                    SseEvent done = result.Events.First(e => e.Event == "turn.completed");
                    Assert.True(done.Data.Contains("\"finishReason\":\"Completed\"", StringComparison.Ordinal) && done.Data.Contains("All done, Mr. Anderson.", StringComparison.Ordinal), "turn.completed: " + done.Data);
                    List<long> sequences = result.Events.Where(e => e.Id != null).Select(e => Int64.Parse(e.Id!, System.Globalization.CultureInfo.InvariantCulture)).ToList();
                    Assert.True(sequences.Zip(sequences.Skip(1), (a, b) => b > a).All(x => x), "event identifiers increase");

                    EnumerationResult<ConversationMessage> messages = await HttpTestHelpers.SendAsync<EnumerationResult<ConversationMessage>>(rig.Anderson, HttpMethod.Get, "/v1.0/api/conversations/" + conversation.Id + "/messages", null, 200, token).ConfigureAwait(false);
                    List<AgentMessageRoleEnum> roles = messages.Objects.Select(m => m.Role).ToList();
                    Assert.True(roles.SequenceEqual(new[] { AgentMessageRoleEnum.User, AgentMessageRoleEnum.Assistant, AgentMessageRoleEnum.Tool, AgentMessageRoleEnum.Tool, AgentMessageRoleEnum.Assistant }), "persisted roles: " + String.Join(", ", roles));
                    ConversationMessage request = messages.Objects[1];
                    Assert.True(request.ToolCalls.Count == 2 && request.ModelEndpointId == endpoint.Id, "tool calls and endpoint persisted");
                    ConversationMessage final = messages.Objects[4];
                    Assert.True(final.FinishReason == "completed" && final.DurationMs.HasValue && final.Content == "All done, Mr. Anderson.", "final message carries the turn");
                    Assert.True(stub.ChatRequests.First().Body.Contains("Agent Smith", StringComparison.Ordinal), "the model saw the compiled persona");
                    Assert.True(!stub.ChatRequests.First().Body.Contains("ask_agent_smith", StringComparison.Ordinal), "meta tools are not offered to the loop");

                    Hugo.Core.Enumeration.EnumerationResult<ToolInvocation> calls = await rig.Context.Database.ToolInvocations.EnumerateAsync(rig.TenantId, new EnumerationQuery(), new Hugo.Core.Requests.ToolInvocationFilter { ConversationId = conversation.Id }, token).ConfigureAwait(false);
                    Assert.Equal(2L, calls.TotalRecords, "both calls recorded against the conversation");
                    SseEvent closed = result.Events.Last();
                    Assert.True(closed.Data.Contains(ChatTestSupport.StubTitle, StringComparison.Ordinal), "stream.closed carries the generated title: " + closed.Data);
                }
            }
        }

        private static async Task JsonExportAnalyticsAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (StubLlmServer stub = ChatTestSupport.NewStub())
            {
                await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
                {
                    Resource echo = await rig.ResourceAsync("echo-a", token).ConfigureAwait(false);
                    await rig.BindAsync(rig.Hugo.Id, echo.Id, AccessModeEnum.ReadOnly, token).ConfigureAwait(false);
                    ModelEndpoint endpoint = await ChatTestSupport.UseStubAsync(rig, stub, token).ConfigureAwait(false);
                    stub.Script.Then(StubLlmResponse.ToolCall("echo_read", "{\"resource\":\"echo-a\",\"text\":\"uptime\"}"), StubLlmResponse.Message("<think>checking</think>Everything is up, Mr. Anderson."));
                    Conversation conversation = await ChatTestSupport.ConversationAsync(rig.Anderson, token).ConfigureAwait(false);
                    Assert.True(conversation.Title == null, "no title before the first exchange");

                    AgentTurnResult result = await HttpTestHelpers.SendAsync<AgentTurnResult>(rig.Anderson, HttpMethod.Post, "/v1.0/api/conversations/" + conversation.Id + "/messages", new { message = "Is everything up?" }, 200, token).ConfigureAwait(false);
                    Assert.Equal(AgentFinishReasonEnum.Completed, result.FinishReason, "finish: " + result.ErrorMessage);
                    Assert.Equal("Everything is up, Mr. Anderson.", result.FinalText, "think blocks removed");
                    Assert.True(result.TokensIn > 0 && result.TokensOut > 0, "tokens counted");
                    Assert.True(result.ToolCalls.Count == 1 && result.ToolCalls[0].Decision == AgentToolDecisionEnum.Allowed, "tool call");

                    Conversation after = await HttpTestHelpers.SendAsync<Conversation>(rig.Anderson, HttpMethod.Get, "/v1.0/api/conversations/" + conversation.Id, null, 200, token).ConfigureAwait(false);
                    Assert.Equal(ChatTestSupport.StubTitle, after.Title, "auto title after the first exchange");
                    Assert.True(after.TotalInputTokens >= result.TokensIn && after.TotalOutputTokens >= result.TokensOut && after.MessageCount == 4, "conversation token accounting and message count: " + after.MessageCount);

                    TestResponse markdown = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Get, "/v1.0/api/conversations/" + conversation.Id + "/export?format=md", null, token).ConfigureAwait(false);
                    Assert.Equal(200, markdown.StatusCode, "markdown export");
                    Assert.True(markdown.Body.StartsWith("# " + ChatTestSupport.StubTitle, StringComparison.Ordinal) && markdown.Body.Contains("## Mr. Anderson", StringComparison.Ordinal) && markdown.Body.Contains("## Agent Smith", StringComparison.Ordinal) && markdown.Body.Contains("Tool call `echo_read`", StringComparison.Ordinal), "markdown content: " + markdown.Body);
                    ConversationExport json = await HttpTestHelpers.SendAsync<ConversationExport>(rig.Anderson, HttpMethod.Get, "/v1.0/api/conversations/" + conversation.Id + "/export?format=json", null, 200, token).ConfigureAwait(false);
                    Assert.True(json.Conversation.Id == conversation.Id && json.Messages.Count == 4, "json export");
                    TestResponse badFormat = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Get, "/v1.0/api/conversations/" + conversation.Id + "/export?format=pdf", null, token).ConfigureAwait(false);
                    Assert.Equal(400, badFormat.StatusCode, "unknown export format");

                    await rig.Context.Get<ToolInvocationRecorder>().FlushAsync(token).ConfigureAwait(false);
                    AnalyticsReport overview = await HttpTestHelpers.SendAsync<AnalyticsReport>(rig.Anderson, HttpMethod.Get, "/v1.0/api/analytics/overview?interval=hour", null, 200, token).ConfigureAwait(false);
                    Assert.True(overview.Totals.Turns == 1 && overview.Totals.InputTokens > 0 && overview.Totals.ToolCalls == 1 && overview.Conversations == 1, "overview totals: turns " + overview.Totals.Turns + ", tools " + overview.Totals.ToolCalls);
                    Assert.True(overview.Buckets.Count == 168 || overview.Buckets.Count == 169, "seven days of hourly buckets, gap filled: " + overview.Buckets.Count);
                    Assert.True(overview.Buckets.Zip(overview.Buckets.Skip(1), (a, b) => b.StartUtc - a.StartUtc == TimeSpan.FromHours(1)).All(x => x), "contiguous buckets");
                    Assert.Equal(1L, overview.Buckets.Sum(b => b.Turns), "the turn lands in one bucket");
                    Assert.True(overview.Totals.AvgTurnDurationMs.HasValue, "turn latency");
                    AnalyticsReport tokens = await HttpTestHelpers.SendAsync<AnalyticsReport>(rig.Anderson, HttpMethod.Get, "/v1.0/api/analytics/tokens?from=" + Uri.EscapeDataString(DateTime.UtcNow.AddHours(-1).ToString("o")), null, 200, token).ConfigureAwait(false);
                    Assert.True(tokens.Interval == "minute" && tokens.Buckets.Count >= 60 && tokens.Totals.OutputTokens == overview.Totals.OutputTokens, "minute buckets for a one-hour window");
                    AnalyticsReport tools = await HttpTestHelpers.SendAsync<AnalyticsReport>(rig.Anderson, HttpMethod.Get, "/v1.0/api/analytics/tools", null, 200, token).ConfigureAwait(false);
                    Assert.True(tools.Tools != null && tools.Tools.Count == 1 && tools.Tools[0].ToolName == "echo_read" && tools.Tools[0].Calls == 1, "per-tool rows");
                    AnalyticsReport models = await HttpTestHelpers.SendAsync<AnalyticsReport>(rig.Anderson, HttpMethod.Get, "/v1.0/api/analytics/models", null, 200, token).ConfigureAwait(false);
                    Assert.True(models.Models != null && models.Models.Any(m => m.ModelEndpointId == endpoint.Id && m.Name == "stub" && m.Turns == 1), "per-model rows");
                    TestResponse tooMany = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Get, "/v1.0/api/analytics/tokens?interval=minute&from=2020-01-01T00:00:00Z", null, token).ConfigureAwait(false);
                    Assert.Equal(400, tooMany.StatusCode, "too many buckets");
                }
            }
        }

        private static async Task CancelConflictAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (StubLlmServer stub = ChatTestSupport.NewStub())
            {
                await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
                {
                    await ChatTestSupport.UseStubAsync(rig, stub, token).ConfigureAwait(false);
                    stub.Script.Then(StubLlmResponse.Message("This answer is very slow.").WithDelay(20000));
                    Conversation conversation = await ChatTestSupport.ConversationAsync(rig.Anderson, token).ConfigureAwait(false);
                    AgentTurnHost host = rig.Context.Get<AgentTurnHost>();
                    Task<TestResponse> first = HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/conversations/" + conversation.Id + "/messages", new { message = "Take your time." }, token);
                    await ChatTestSupport.UntilAsync(() => Task.FromResult(host.Turns.IsActive(conversation.Id) && stub.ChatRequests.Count > 0), "the first turn to reach the model", token).ConfigureAwait(false);

                    TestResponse second = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/conversations/" + conversation.Id + "/messages", new { message = "Me too." }, token).ConfigureAwait(false);
                    Assert.True(second.StatusCode == 409 && second.Body.Contains("TurnInProgress", StringComparison.Ordinal), "concurrent turn: " + second.StatusCode + " " + second.Body);
                    SseResult secondStream = await ChatTestSupport.PostSseAsync(rig.Anderson, "/v1.0/api/conversations/" + conversation.Id + "/messages", "{\"message\":\"Streaming too.\"}", token).ConfigureAwait(false);
                    Assert.Equal(409, secondStream.StatusCode, "a streaming request is refused before the stream starts");

                    TestResponse cancel = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/conversations/" + conversation.Id + "/cancel", null, token).ConfigureAwait(false);
                    Assert.True(cancel.StatusCode == 200 && cancel.Body.Contains("\"cancelled\":true", StringComparison.Ordinal), "cancel: " + cancel.Body);
                    TestResponse cancelled = await first.WaitAsync(TimeSpan.FromSeconds(30), token).ConfigureAwait(false);
                    AgentTurnResult result = Serializer.Deserialize<AgentTurnResult>(cancelled.Body)!;
                    Assert.Equal(AgentFinishReasonEnum.Cancelled, result.FinishReason, "the running turn was cancelled: " + cancelled.Body);
                    await ChatTestSupport.UntilAsync(() => Task.FromResult(!host.Turns.IsActive(conversation.Id)), "the conversation to be released", token).ConfigureAwait(false);

                    List<ConversationMessage> messages = await rig.Context.Database.Messages.ListAsync(rig.TenantId, conversation.Id, false, token).ConfigureAwait(false);
                    Assert.True(messages.Count == 2 && messages[1].FinishReason == "cancelled", "the cancelled turn is closed in the transcript");
                    TestResponse idle = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/conversations/" + conversation.Id + "/cancel", null, token).ConfigureAwait(false);
                    Assert.True(idle.Body.Contains("\"cancelled\":false", StringComparison.Ordinal), "nothing to cancel");
                    AgentTurnResult next = await HttpTestHelpers.SendAsync<AgentTurnResult>(rig.Anderson, HttpMethod.Post, "/v1.0/api/conversations/" + conversation.Id + "/messages", new { message = "Again." }, 200, token).ConfigureAwait(false);
                    Assert.Equal(AgentFinishReasonEnum.Completed, next.FinishReason, "the conversation accepts turns again");
                }
            }
        }

        private static async Task DelegationAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (StubLlmServer stub = ChatTestSupport.NewStub())
            {
                await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, s => s.Approvals.McpWaitSeconds = 1, token).ConfigureAwait(false))
                {
                    Resource echo = await rig.ResourceAsync("echo-a", token).ConfigureAwait(false);
                    await rig.BindAsync(rig.Hugo.Id, echo.Id, AccessModeEnum.ReadWriteWithApproval, token).ConfigureAwait(false);
                    await ChatTestSupport.UseStubAsync(rig, stub, token).ConfigureAwait(false);
                    Assert.True(rig.Context.AgentDelegation != null, "delegation registered on the server context");

                    stub.Script.Then(StubLlmResponse.ToolCall("echo_read", "{\"resource\":\"echo-a\",\"text\":\"disk\"}"), StubLlmResponse.Message("Disk is fine, Mr. Anderson."));
                    AgentDelegationResult rest = await HttpTestHelpers.SendAsync<AgentDelegationResult>(rig.Anderson, HttpMethod.Post, "/v1.0/api/agent-profiles/" + rig.Hugo.Id + "/ask", new { message = "Check the disk." }, 200, token).ConfigureAwait(false);
                    Assert.True(rest.Status == AgentDelegationStatusEnum.Completed && rest.FinalText == "Disk is fine, Mr. Anderson.", "REST ask: " + rest.Status + " " + rest.FinalText);
                    Assert.True(rest.ToolTrace.Count == 1 && rest.ToolTrace[0].StartsWith("echo_read on echo-a: Allowed", StringComparison.Ordinal), "compact tool trace: " + String.Join(" | ", rest.ToolTrace));
                    Conversation restConversation = await HttpTestHelpers.SendAsync<Conversation>(rig.Anderson, HttpMethod.Get, "/v1.0/api/conversations/" + rest.ConversationId, null, 200, token).ConfigureAwait(false);
                    Assert.Equal(ConversationSourceEnum.Rest, restConversation.Source, "REST asks map onto a conversation");
                    TestResponse byName = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/agent-profiles/missing_profile/ask", new { message = "x" }, token).ConfigureAwait(false);
                    Assert.Equal(404, byName.StatusCode, "unknown profile");

                    RequestContext anderson = await AndersonAsync(rig, token).ConfigureAwait(false);
                    stub.Script.Then(StubLlmResponse.ToolCall("echo_write", "{\"resource\":\"echo-a\",\"text\":\"restart\"}"));
                    AgentDelegationResult pending = await rig.Context.AgentDelegation!.AskAsync(anderson, rig.TenantId, "Hugo", "Restart echo.", null, token).ConfigureAwait(false);
                    Assert.Equal(AgentDelegationStatusEnum.ApprovalPending, pending.Status, "the write waits for approval: " + pending.FinalText);
                    Assert.True(pending.ApprovalId != null && pending.ApprovalId.StartsWith("apr_", StringComparison.Ordinal) && pending.FinalText.Contains(pending.ApprovalId, StringComparison.Ordinal), "the pending approval is named");
                    Conversation mcpConversation = (await rig.Context.Database.Conversations.ReadAsync(rig.TenantId, pending.ConversationId!, token).ConfigureAwait(false))!;
                    Assert.Equal(ConversationSourceEnum.Mcp, mcpConversation.Source, "ask_agent_smith maps onto a conversation");

                    await rig.DecideAsync(pending.ApprovalId!, "approve", token).ConfigureAwait(false);
                    stub.Script.Then(StubLlmResponse.Message("The restart went through, Mr. Anderson."));
                    AgentDelegationResult followUp = await rig.Context.AgentDelegation.AskAsync(anderson, rig.TenantId, rig.Hugo.Id, "Did it work?", pending.ConversationId, token).ConfigureAwait(false);
                    Assert.Equal(AgentDelegationStatusEnum.Completed, followUp.Status, "follow-up");
                    Assert.Equal(pending.ConversationId, followUp.ConversationId, "same conversation");
                    string lastRequest = stub.ChatRequests.Last(r => !r.Body.Contains("Write a short title", StringComparison.Ordinal)).Body;
                    Assert.True(lastRequest.Contains("[approval " + pending.ApprovalId + "]", StringComparison.Ordinal) && lastRequest.Contains("approved", StringComparison.Ordinal), "the next turn learns the approval outcome");
                    List<ConversationMessage> notes = (await rig.Context.Database.Messages.ListAsync(rig.TenantId, pending.ConversationId!, true, token).ConfigureAwait(false)).Where(m => m.Role == AgentMessageRoleEnum.System).ToList();
                    Assert.Equal(1, notes.Count, "one follow-up note");
                }
            }
        }

        private static async Task ErrorsAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                Conversation conversation = await ChatTestSupport.ConversationAsync(rig.Anderson, token).ConfigureAwait(false);
                TestResponse noModel = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/conversations/" + conversation.Id + "/messages", new { message = "hello" }, token).ConfigureAwait(false);
                Assert.True(noModel.StatusCode == 400 && noModel.Body.Contains("ModelEndpointRequired", StringComparison.Ordinal), "no inference endpoint: " + noModel.StatusCode + " " + noModel.Body);
                SseResult noModelStream = await ChatTestSupport.PostSseAsync(rig.Anderson, "/v1.0/api/conversations/" + conversation.Id + "/messages", "{\"message\":\"hello\"}", token).ConfigureAwait(false);
                Assert.Equal(400, noModelStream.StatusCode, "the SSE request fails before streaming");
                using (StubLlmServer stub = ChatTestSupport.NewStub())
                {
                    await ChatTestSupport.UseStubAsync(rig, stub, token).ConfigureAwait(false);
                    TestResponse empty = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/conversations/" + conversation.Id + "/messages", new { message = " " }, token).ConfigureAwait(false);
                    Assert.True(empty.StatusCode == 400 && empty.Body.Contains("MessageRequired", StringComparison.Ordinal), "empty message");
                    stub.Script.Then(StubLlmResponse.Error(500));
                    AgentTurnResult failed = await HttpTestHelpers.SendAsync<AgentTurnResult>(rig.Anderson, HttpMethod.Post, "/v1.0/api/conversations/" + conversation.Id + "/messages", new { message = "hello" }, 200, token).ConfigureAwait(false);
                    Assert.Equal(AgentFinishReasonEnum.Error, failed.FinishReason, "a model failure ends the turn with an error");
                    List<ConversationMessage> messages = await rig.Context.Database.Messages.ListAsync(rig.TenantId, conversation.Id, false, token).ConfigureAwait(false);
                    Assert.True(messages.Last().FinishReason == "error" && messages.Last().Content.StartsWith("The turn failed", StringComparison.Ordinal), "the failed turn is closed in the transcript");
                }
                TestResponse missing = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/conversations/cnv_missing/messages", new { message = "hello" }, token).ConfigureAwait(false);
                Assert.Equal(404, missing.StatusCode, "unknown conversation");
            }
        }

        #endregion
    }
}
