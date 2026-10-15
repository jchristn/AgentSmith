namespace Test.Shared.Chat
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Test.Shared.Agent.StubLlm;
    using Test.Shared.Infrastructure;
    using Test.Shared.Integration;
    using Touchstone.Core;
    using Voltaic.A2A;

    /// <summary>
    /// A2A surface with Voltaic's own <see cref="A2AClient"/> (P7.17): the card, the tenant card list, send, stream, get,
    /// list, cancel, ownership refusals, and an approval wait that moves the task to input-required and resumes it once a
    /// human approves.
    /// </summary>
    public static class A2ASuite
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
                string id = "a2a-" + provider.ToString().ToLowerInvariant();
                List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
                cases.Add(new TestCaseDescriptor(id, "card", "Agent card from the a2a.skill-description template, one skill per family, external base URL, expose flag (" + provider + ")", ct => CardAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "tasks", "SendMessage, SendStreamingMessage, GetTask, ListTasks, legacy names, and context continuity (" + provider + ")", ct => TasksAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "cancel-ownership", "CancelTask, not cancelable, SubscribeToTask, and -32001 for another principal (" + provider + ")", ct => CancelOwnershipAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "input-required", "An approval wait is input-required, the caller cannot approve, and the task resumes after a human approves (" + provider + ")", ct => InputRequiredAsync(provider, ct)));
                suites.Add(new TestSuiteDescriptor(id, "A2A (" + provider + ")", cases));
            }
            return suites;
        }

        #endregion

        #region Private-Methods

        private static string Endpoint(IntegrationRig rig, string profile = "Hugo")
        {
            return rig.Host.BaseUrl + "/v1.0/a2a/" + rig.TenantId + "/" + profile;
        }

        private static SendMessageRequest Message(string text, string? contextId = null, bool returnImmediately = false)
        {
            SendMessageRequest request = new SendMessageRequest();
            request.Message.Role = Voltaic.A2A.Role.User;
            request.Message.MessageId = Guid.NewGuid().ToString("N");
            request.Message.ContextId = contextId;
            request.Message.Parts.Add(Part.FromText(text));
            if (returnImmediately) request.Configuration = new SendMessageConfiguration { ReturnImmediately = true };
            return request;
        }

        private static string ArtifactText(AgentTask task)
        {
            if (task.Artifacts == null) return String.Empty;
            return String.Join("\n", task.Artifacts.SelectMany(a => a.Parts).Select(p => p.Text ?? String.Empty));
        }

        private static async Task CardAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, s => s.Rest.ExternalBaseUrl = "https://hugo.example.test/", token).ConfigureAwait(false))
            {
                Resource echo = await rig.ResourceAsync("echo-a", token).ConfigureAwait(false);
                await rig.BindAsync(rig.Hugo.Id, echo.Id, AccessModeEnum.ReadOnly, token).ConfigureAwait(false);
                using (HttpClient anonymous = new HttpClient())
                using (A2ACardResolver resolver = new A2ACardResolver(anonymous))
                {
                    AgentCard card = await resolver.GetAgentCardAsync(Endpoint(rig), token).ConfigureAwait(false);
                    Assert.True(card.Skills.Any(s => s.Id == "agent-smith") && card.Skills.Any(s => s.Id == "echo"), "general skill plus one per family: " + String.Join(", ", card.Skills.Select(s => s.Id)));
                    Assert.Equal(2, card.Skills.Count, "skills");
                    AgentSkill general = card.Skills.First(s => s.Id == "agent-smith");
                    Assert.True(general.Description.Contains("echo-a", StringComparison.Ordinal) && general.Description.Contains("Agent Smith", StringComparison.Ordinal), "skill text from the template: " + general.Description);
                    Assert.Equal("https://hugo.example.test/v1.0/a2a/" + rig.TenantId + "/Hugo", card.SupportedInterfaces.Single().Url, "interface URL uses Rest.ExternalBaseUrl");
                    Assert.True(card.Capabilities.Streaming == true && card.SecuritySchemes != null && card.SecuritySchemes.Values.First().HttpAuthSecurityScheme?.Scheme == "bearer", "streaming and bearer");

                    HttpResponseMessage missing = await anonymous.GetAsync(rig.Host.BaseUrl + "/v1.0/a2a/" + rig.TenantId + "/Nobody/.well-known/agent-card.json", token).ConfigureAwait(false);
                    Assert.Equal(404, (int)missing.StatusCode, "unknown profile");
                }

                TestResponse catalog = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Get, "/v1.0/a2a/" + rig.TenantId, null, token).ConfigureAwait(false);
                List<AgentCard> cards = JsonSerializer.Deserialize<List<AgentCard>>(catalog.Body, A2AJson.DefaultOptions)!;
                Assert.True(catalog.StatusCode == 200 && cards.Count == 1 && cards[0].Name == DisplayName(rig), "tenant card list");

                await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + rig.Hugo.Id, new { exposeA2A = false }, 200, token).ConfigureAwait(false);
                using (HttpClient anonymous = new HttpClient())
                {
                    HttpResponseMessage hidden = await anonymous.GetAsync(Endpoint(rig) + "/.well-known/agent-card.json", token).ConfigureAwait(false);
                    Assert.Equal(404, (int)hidden.StatusCode, "a profile with expose_a2a false has no card");
                }
                using (A2AClient client = new A2AClient(Endpoint(rig), rig.Anderson))
                {
                    A2AProtocolException? refused = null;
                    try
                    {
                        await client.SendMessageAsync(Message("hello"), token).ConfigureAwait(false);
                    }
                    catch (A2AProtocolException e)
                    {
                        refused = e;
                    }
                    Assert.True(refused != null && refused.ErrorCode == A2AErrorCode.TaskNotFound, "JSON-RPC to an unexposed profile is not found");
                }
                TestResponse emptyCatalog = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Get, "/v1.0/a2a/" + rig.TenantId, null, token).ConfigureAwait(false);
                Assert.Equal("[]", emptyCatalog.Body.Trim(), "unexposed profiles leave the card list");
            }
        }

        private static string DisplayName(IntegrationRig rig)
        {
            return String.IsNullOrEmpty(rig.Hugo.DisplayName) ? rig.Hugo.Name : rig.Hugo.DisplayName!;
        }

        private static async Task TasksAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (StubLlmServer stub = ChatTestSupport.NewStub())
            {
                await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
                {
                    Resource echo = await rig.ResourceAsync("echo-a", token).ConfigureAwait(false);
                    await rig.BindAsync(rig.Hugo.Id, echo.Id, AccessModeEnum.ReadOnly, token).ConfigureAwait(false);
                    await ChatTestSupport.UseStubAsync(rig, stub, token).ConfigureAwait(false);
                    using (A2AClient client = new A2AClient(Endpoint(rig), rig.Anderson))
                    {
                        stub.Script.Then(StubLlmResponse.ToolCall("echo_read", "{\"resource\":\"echo-a\",\"text\":\"ping\"}"), StubLlmResponse.Message("Pong, Mr. Anderson."));
                        SendMessageResponse sent = await client.SendMessageAsync(Message("Ping echo-a."), token).ConfigureAwait(false);
                        AgentTask task = sent.Task ?? throw new TestAssertionException("SendMessage returned no task.");
                        Assert.True(task.Id.StartsWith("a2t_", StringComparison.Ordinal) && task.Status.State == TaskState.Completed, "completed task: " + task.Status.State);
                        Assert.True(ArtifactText(task).Contains("Pong, Mr. Anderson.", StringComparison.Ordinal), "result artifact");
                        Assert.True(task.Status.Message != null && task.Status.Message.Parts.Any(p => (p.Text ?? String.Empty).Contains("Pong", StringComparison.Ordinal)), "status message carries the answer");
                        Conversation conversation = (await rig.Context.Database.Conversations.ReadAsync(rig.TenantId, task.ContextId, token).ConfigureAwait(false))!;
                        Assert.True(conversation != null && conversation.Source == ConversationSourceEnum.A2A && conversation.A2AContextId == task.ContextId, "a new context starts an A2A conversation");

                        AgentTask read = await client.GetTaskAsync(new GetTaskRequest { Id = task.Id }, token).ConfigureAwait(false);
                        Assert.True(read.Id == task.Id && read.Status.State == TaskState.Completed && ArtifactText(read).Contains("Pong", StringComparison.Ordinal), "GetTask");

                        stub.Script.Then(StubLlmResponse.Message("Still here, Mr. Anderson."));
                        SendMessageResponse again = await client.SendMessageAsync(Message("Are you still there?", task.ContextId), token).ConfigureAwait(false);
                        Assert.Equal(task.ContextId, again.Task!.ContextId, "same context");
                        Conversation continued = (await rig.Context.Database.Conversations.ReadAsync(rig.TenantId, task.ContextId, token).ConfigureAwait(false))!;
                        Assert.Equal(6, continued.MessageCount, "the context continues one conversation");

                        stub.Script.Then(StubLlmResponse.Message("Streaming, Mr. Anderson."));
                        List<StreamResponse> events = new List<StreamResponse>();
                        await foreach (StreamResponse item in client.SendStreamingMessageAsync(Message("Stream please."), token).ConfigureAwait(false)) events.Add(item);
                        Assert.True(events.Count >= 3 && events.First().Task != null, "the stream starts with the task");
                        Assert.True(events.Any(e => e.ArtifactUpdate != null && e.ArtifactUpdate.Artifact.Parts.Any(p => (p.Text ?? String.Empty).Contains("Streaming", StringComparison.Ordinal))), "artifact update");
                        Assert.True(events.Last().StatusUpdate != null && events.Last().StatusUpdate!.Status.State == TaskState.Completed, "the stream ends completed");

                        ListTasksResponse listed = await client.ListTasksAsync(new ListTasksRequest(), token).ConfigureAwait(false);
                        Assert.Equal(3, listed.TotalSize, "ListTasks");
                        ListTasksResponse inContext = await client.ListTasksAsync(new ListTasksRequest { ContextId = task.ContextId, IncludeArtifacts = true }, token).ConfigureAwait(false);
                        Assert.True(inContext.Tasks.Count == 2 && inContext.Tasks.All(t => t.Artifacts != null && t.Artifacts.Count == 1), "ListTasks by context with artifacts");
                    }

                    stub.Script.Then(StubLlmResponse.Message("Legacy works, Mr. Anderson."));
                    string legacy = "{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"message/send\",\"params\":{\"message\":{\"role\":\"ROLE_USER\",\"messageId\":\"m-legacy\",\"parts\":[{\"text\":\"legacy?\"}]}}}";
                    TestResponse response = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/a2a/" + rig.TenantId + "/Hugo", legacy, token).ConfigureAwait(false);
                    Assert.True(response.StatusCode == 200 && response.Body.Contains("\"id\":7", StringComparison.Ordinal) && response.Body.Contains("Legacy works", StringComparison.Ordinal), "legacy method name: " + response.Body);
                    TestResponse unknown = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/a2a/" + rig.TenantId + "/Hugo", "{\"jsonrpc\":\"2.0\",\"id\":8,\"method\":\"tasks/teleport\"}", token).ConfigureAwait(false);
                    Assert.True(unknown.Body.Contains("-32601", StringComparison.Ordinal), "unknown method");
                    TestResponse garbage = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/a2a/" + rig.TenantId + "/Hugo", "not json", token).ConfigureAwait(false);
                    Assert.True(garbage.Body.Contains("-32700", StringComparison.Ordinal), "parse error");
                    TestResponse empty = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/a2a/" + rig.TenantId + "/Hugo", "{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"SendMessage\",\"params\":{\"message\":{\"role\":\"ROLE_USER\",\"messageId\":\"m\",\"parts\":[]}}}", token).ConfigureAwait(false);
                    Assert.True(empty.Body.Contains("-32602", StringComparison.Ordinal), "a message without parts is invalid: " + empty.Body);
                    Assert.True(rig.Context.Scopes.FindProtocolMethod("A2A", "SendMessage")?.Operation == OperationTypeEnum.Execute && rig.Context.Scopes.ProtocolMethods().Count == 7, "every A2A method is in the operation scope map");
                }
            }
        }

        private static async Task CancelOwnershipAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (StubLlmServer stub = ChatTestSupport.NewStub())
            {
                await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
                {
                    await ChatTestSupport.UseStubAsync(rig, stub, token).ConfigureAwait(false);
                    using (HttpClient operatorClient = await ChatTestSupport.UserAsync(rig, "a2a-operator@chat.local", "Operator", token).ConfigureAwait(false))
                    using (A2AClient anderson = new A2AClient(Endpoint(rig), rig.Anderson))
                    using (A2AClient other = new A2AClient(Endpoint(rig), operatorClient))
                    {
                        stub.Script.Then(StubLlmResponse.Message("Very slow.").WithDelay(20000));
                        SendMessageResponse started = await anderson.SendMessageAsync(Message("Take your time.", null, true), token).ConfigureAwait(false);
                        AgentTask task = started.Task!;
                        Assert.Equal(TaskState.Working, task.Status.State, "ReturnImmediately returns the working task");

                        A2AProtocolException? notFound = null;
                        try
                        {
                            await other.GetTaskAsync(new GetTaskRequest { Id = task.Id }, token).ConfigureAwait(false);
                        }
                        catch (A2AProtocolException e)
                        {
                            notFound = e;
                        }
                        Assert.True(notFound != null && notFound.ErrorCode == A2AErrorCode.TaskNotFound, "another principal gets -32001");
                        A2AProtocolException? cannotCancel = null;
                        try
                        {
                            await other.CancelTaskAsync(new CancelTaskRequest { Id = task.Id }, token).ConfigureAwait(false);
                        }
                        catch (A2AProtocolException e)
                        {
                            cannotCancel = e;
                        }
                        Assert.True(cannotCancel != null && cannotCancel.ErrorCode == A2AErrorCode.TaskNotFound, "another principal cannot cancel");
                        ListTasksResponse othersList = await other.ListTasksAsync(new ListTasksRequest(), token).ConfigureAwait(false);
                        Assert.Equal(0, othersList.TotalSize, "another principal lists none of them");

                        await ChatTestSupport.UntilAsync(() => Task.FromResult(stub.ChatRequests.Count > 0), "the task to reach the model", token).ConfigureAwait(false);
                        AgentTask cancelled = await anderson.CancelTaskAsync(new CancelTaskRequest { Id = task.Id }, token).ConfigureAwait(false);
                        Assert.Equal(TaskState.Canceled, cancelled.Status.State, "CancelTask");
                        A2AProtocolException? again = null;
                        try
                        {
                            await anderson.CancelTaskAsync(new CancelTaskRequest { Id = task.Id }, token).ConfigureAwait(false);
                        }
                        catch (A2AProtocolException e)
                        {
                            again = e;
                        }
                        Assert.True(again != null && again.ErrorCode == A2AErrorCode.TaskNotCancelable, "a finished task is not cancelable");

                        List<StreamResponse> replay = new List<StreamResponse>();
                        await foreach (StreamResponse item in anderson.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = task.Id }, token).ConfigureAwait(false)) replay.Add(item);
                        Assert.True(replay.First().Task != null && replay.Last().StatusUpdate?.Status.State == TaskState.Canceled, "SubscribeToTask on a finished task ends at once");
                    }
                }
            }
        }

        private static async Task InputRequiredAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (StubLlmServer stub = ChatTestSupport.NewStub())
            {
                await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
                {
                    Resource echo = await rig.ResourceAsync("echo-a", token).ConfigureAwait(false);
                    await rig.BindAsync(rig.Hugo.Id, echo.Id, AccessModeEnum.ReadWriteWithApproval, token).ConfigureAwait(false);
                    await ChatTestSupport.UseStubAsync(rig, stub, token).ConfigureAwait(false);
                    using (HttpClient operatorClient = await ChatTestSupport.UserAsync(rig, "a2a-caller@chat.local", "Operator", token).ConfigureAwait(false))
                    using (A2AClient caller = new A2AClient(Endpoint(rig), operatorClient))
                    {
                        stub.Script.Then(StubLlmResponse.ToolCall("echo_write", "{\"resource\":\"echo-a\",\"text\":\"rotate logs\"}"), StubLlmResponse.Message("Logs rotated, Mr. Anderson."));
                        SendMessageResponse sent = await caller.SendMessageAsync(Message("Rotate the logs on echo-a."), token).ConfigureAwait(false);
                        AgentTask task = sent.Task!;
                        Assert.Equal(TaskState.InputRequired, task.Status.State, "the approval wait is input-required");
                        string approvalId = task.Metadata != null && task.Metadata.TryGetValue("hugo.pendingApprovalId", out object? value) ? value?.ToString() ?? String.Empty : String.Empty;
                        Assert.True(approvalId.StartsWith("apr_", StringComparison.Ordinal), "the task names the approval");
                        Assert.True(task.Status.Message != null && task.Status.Message.Parts.Any(p => (p.Text ?? String.Empty).Contains(approvalId, StringComparison.Ordinal)), "the status message names the approval");

                        TestResponse self = await rig.DecideAsync(approvalId, "approve", token, 409, operatorClient).ConfigureAwait(false);
                        Assert.True(self.Body.Contains("SelfApprovalNotAllowed", StringComparison.Ordinal), "an A2A caller cannot approve its own request");
                        string reply = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"SendMessage\",\"params\":{\"message\":{\"role\":\"ROLE_USER\",\"messageId\":\"m2\",\"taskId\":\"" + task.Id + "\",\"parts\":[{\"text\":\"yes\"}]}}}";
                        TestResponse answer = await HttpTestHelpers.SendAsync(operatorClient, HttpMethod.Post, "/v1.0/a2a/" + rig.TenantId + "/Hugo", reply, token).ConfigureAwait(false);
                        Assert.True(answer.Body.Contains("-32007", StringComparison.Ordinal) && answer.Body.Contains("ApprovalDecidedByHuman", StringComparison.Ordinal), "an input-required task is not answered over A2A: " + answer.Body);

                        await rig.DecideAsync(approvalId, "approve", token).ConfigureAwait(false);
                        AgentTask? finished = null;
                        await ChatTestSupport.UntilAsync(async () =>
                        {
                            finished = await caller.GetTaskAsync(new GetTaskRequest { Id = task.Id }, token).ConfigureAwait(false);
                            return finished.Status.State == TaskState.Completed;
                        }, "the task to resume and complete", token).ConfigureAwait(false);
                        Assert.True(ArtifactText(finished!).Contains("Logs rotated", StringComparison.Ordinal), "the resumed task answered");
                        Assert.True(finished!.Metadata == null || !finished.Metadata.ContainsKey("hugo.pendingApprovalId"), "no approval pending after completion");
                    }
                    using (A2AClient admin = new A2AClient(Endpoint(rig), rig.Anderson))
                    {
                        string taskId = (await rig.Context.Database.A2ATasks.ListByStateAsync(new List<A2ATaskStateEnum> { A2ATaskStateEnum.Completed }, token).ConfigureAwait(false)).First().Id;
                        AgentTask adminView = await admin.GetTaskAsync(new GetTaskRequest { Id = taskId }, token).ConfigureAwait(false);
                        Assert.Equal(TaskState.Completed, adminView.Status.State, "a tenant administrator reads any task in the tenant");
                    }
                }
            }
        }

        #endregion
    }
}
