namespace Test.Shared.Chat
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Server.Protocols.OpenAI;
    using Test.Shared.Agent.StubLlm;
    using Test.Shared.Infrastructure;
    using Test.Shared.Integration;
    using Touchstone.Core;

    /// <summary>
    /// OpenAI-compatible surface (P7.19) driven with raw JSON fixtures shaped like the official OpenAI clients' requests
    /// (Fixtures/openai): non-streaming and streaming completions, client tools rejected, the model list, session
    /// continuity, history seeding, and an approval that ends the turn with an explanation.
    /// </summary>
    public static class OpenAISuite
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
                string id = "openai-" + provider.ToString().ToLowerInvariant();
                List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
                cases.Add(new TestCaseDescriptor(id, "completions", "Non-streaming and streaming completions, system messages as caller context, usage (" + provider + ")", ct => CompletionsAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "rejections-models", "Client tools rejected, model list, expose flag, unknown model (" + provider + ")", ct => RejectionsModelsAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "sessions-approvals", "Sessions by user and x-hugo-conversation, stateless history, approval pending explained (" + provider + ")", ct => SessionsApprovalsAsync(provider, ct)));
                suites.Add(new TestSuiteDescriptor(id, "OpenAI-compatible (" + provider + ")", cases));
            }
            return suites;
        }

        #endregion

        #region Private-Methods

        private static readonly string _Path = "/v1.0/openai/v1/chat/completions";

        private static string Fixture(string name)
        {
            return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openai", name));
        }

        private static OpenAIChatResponse Completion(SseResult result)
        {
            if (result.StatusCode != 200) throw new TestAssertionException("Completion returned " + result.StatusCode + ": " + result.Body);
            return JsonSerializer.Deserialize<OpenAIChatResponse>(result.Body) ?? throw new TestAssertionException("Empty completion.");
        }

        private static async Task<SseResult> PostAsync(HttpClient client, string json, CancellationToken token, string? conversationId = null)
        {
            if (conversationId == null) return await ChatTestSupport.PostSseAsync(client, _Path, json, token).ConfigureAwait(false);
            using (HttpClient withHeader = new HttpClient())
            {
                withHeader.BaseAddress = client.BaseAddress;
                withHeader.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
                withHeader.DefaultRequestHeaders.Add("x-hugo-conversation", conversationId);
                return await ChatTestSupport.PostSseAsync(withHeader, _Path, json, token).ConfigureAwait(false);
            }
        }

        private static async Task CompletionsAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (StubLlmServer stub = ChatTestSupport.NewStub())
            {
                await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
                {
                    await ChatTestSupport.UseStubAsync(rig, stub, token).ConfigureAwait(false);
                    stub.Script.Then(StubLlmResponse.Message("Echo-a is healthy, Mr. Anderson."));
                    SseResult plain = await PostAsync(rig.Anderson, Fixture("chat-nonstream.json"), token).ConfigureAwait(false);
                    OpenAIChatResponse completion = Completion(plain);
                    Assert.True(completion.Object == "chat.completion" && completion.Model == "Hugo" && completion.Id.StartsWith("chatcmpl-", StringComparison.Ordinal), "completion envelope");
                    Assert.True(completion.Choices.Count == 1 && completion.Choices[0].Message?.Role == "assistant" && completion.Choices[0].Message?.Content == "Echo-a is healthy, Mr. Anderson." && completion.Choices[0].FinishReason == "stop", "choice: " + plain.Body);
                    Assert.True(completion.Usage != null && completion.Usage.TotalTokens == completion.Usage.PromptTokens + completion.Usage.CompletionTokens && completion.Usage.PromptTokens > 0, "usage");
                    Assert.True(plain.Body.Contains("\"finish_reason\":\"stop\"", StringComparison.Ordinal) && plain.Body.Contains("\"prompt_tokens\"", StringComparison.Ordinal), "snake_case wire names");
                    string conversationId = plain.Headers.TryGetValue("x-hugo-conversation", out string? header) ? header : String.Empty;
                    Assert.True(conversationId.StartsWith("cnv_", StringComparison.Ordinal) && completion.Hugo?.ConversationId == conversationId, "conversation header and extension");
                    Conversation conversation = (await rig.Context.Database.Conversations.ReadAsync(rig.TenantId, conversationId, token).ConfigureAwait(false))!;
                    Assert.Equal(ConversationSourceEnum.OpenAI, conversation.Source, "OpenAI sessions map onto conversations");
                    string modelBody = stub.ChatRequests.First().Body;
                    Assert.True(modelBody.Contains("Caller context", StringComparison.Ordinal) && modelBody.Contains("Answer in one sentence.", StringComparison.Ordinal) && modelBody.Contains("Agent Smith", StringComparison.Ordinal), "client system messages follow the compiled instructions");

                    stub.Script.Then(StubLlmResponse.Message("All systems nominal, Mr. Anderson."));
                    SseResult streamed = await PostAsync(rig.Anderson, Fixture("chat-stream.json"), token).ConfigureAwait(false);
                    Assert.True(streamed.StatusCode == 200 && streamed.ContentType == "text/event-stream", "stream: " + streamed.StatusCode + " " + streamed.Body);
                    Assert.Equal("[DONE]", streamed.Events.Last().Data, "the stream ends with [DONE]");
                    List<OpenAIChatResponse> chunks = streamed.Events.Take(streamed.Events.Count - 1).Select(e => JsonSerializer.Deserialize<OpenAIChatResponse>(e.Data)!).ToList();
                    Assert.True(chunks.All(c => c.Object == "chat.completion.chunk" && c.Id == chunks[0].Id), "chunk envelope");
                    Assert.Equal("assistant", chunks[0].Choices[0].Delta?.Role, "first chunk carries the role");
                    string text = String.Concat(chunks.Where(c => c.Choices.Count > 0).Select(c => c.Choices[0].Delta?.Content ?? String.Empty));
                    Assert.Equal("All systems nominal, Mr. Anderson.", text, "deltas concatenate to the answer");
                    Assert.True(chunks.Any(c => c.Choices.Count == 1 && c.Choices[0].FinishReason == "stop"), "a chunk carries finish_reason");
                    OpenAIChatResponse usage = chunks.Last();
                    Assert.True(usage.Choices.Count == 0 && usage.Usage != null && usage.Usage.TotalTokens > 0, "include_usage adds a usage chunk");
                    Assert.True(stub.ChatRequests.Last(r => !r.Body.Contains("Write a short title", StringComparison.Ordinal)).Body.Contains("Stream a status report.", StringComparison.Ordinal), "content parts are read as text");
                }
            }
        }

        private static async Task RejectionsModelsAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (StubLlmServer stub = ChatTestSupport.NewStub())
            {
                await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
                {
                    await ChatTestSupport.UseStubAsync(rig, stub, token).ConfigureAwait(false);
                    SseResult tools = await PostAsync(rig.Anderson, Fixture("chat-tools.json"), token).ConfigureAwait(false);
                    OpenAIErrorResponse error = JsonSerializer.Deserialize<OpenAIErrorResponse>(tools.Body)!;
                    Assert.True(tools.StatusCode == 400 && error.Error.Code == "tools_not_supported" && error.Error.Param == "tools" && error.Error.Type == "invalid_request_error", "client tools rejected: " + tools.Body);
                    Assert.Equal(0, stub.ChatRequests.Count, "a rejected request never reaches the model");

                    TestResponse models = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Get, "/v1.0/openai/v1/models", null, token).ConfigureAwait(false);
                    OpenAIModelList list = JsonSerializer.Deserialize<OpenAIModelList>(models.Body)!;
                    Assert.True(models.StatusCode == 200 && list.Object == "list" && list.Data.Any(m => m.Id == "Hugo" && m.Object == "model" && m.OwnedBy == "hugo"), "models: " + models.Body);

                    SseResult unknown = await PostAsync(rig.Anderson, Fixture("chat-nonstream.json").Replace("\"Hugo\"", "\"Nobody\""), token).ConfigureAwait(false);
                    Assert.True(unknown.StatusCode == 404 && unknown.Body.Contains("model_not_found", StringComparison.Ordinal), "unknown model");
                    SseResult notUser = await PostAsync(rig.Anderson, "{\"model\":\"Hugo\",\"messages\":[{\"role\":\"assistant\",\"content\":\"hi\"}]}", token).ConfigureAwait(false);
                    Assert.Equal(400, notUser.StatusCode, "the last message must be a user message");

                    await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + rig.Hugo.Id, new { exposeOpenAI = false }, 200, token).ConfigureAwait(false);
                    TestResponse hiddenModels = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Get, "/v1.0/openai/v1/models", null, token).ConfigureAwait(false);
                    Assert.True(!JsonSerializer.Deserialize<OpenAIModelList>(hiddenModels.Body)!.Data.Any(m => m.Id == "Hugo"), "unexposed profiles are not listed");
                    SseResult hidden = await PostAsync(rig.Anderson, Fixture("chat-nonstream.json"), token).ConfigureAwait(false);
                    Assert.True(hidden.StatusCode == 404 && hidden.Body.Contains("model_not_found", StringComparison.Ordinal), "expose_openai false refuses the model");

                    using (HttpClient anonymous = new HttpClient())
                    {
                        HttpResponseMessage unauthenticated = await anonymous.PostAsync(rig.Host.BaseUrl + _Path, new StringContent(Fixture("chat-nonstream.json"), Encoding.UTF8, "application/json"), token).ConfigureAwait(false);
                        Assert.Equal(401, (int)unauthenticated.StatusCode, "authentication required");
                    }
                }
            }
        }

        private static async Task SessionsApprovalsAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (StubLlmServer stub = ChatTestSupport.NewStub())
            {
                await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
                {
                    Resource echo = await rig.ResourceAsync("echo-a", token).ConfigureAwait(false);
                    await rig.BindAsync(rig.Hugo.Id, echo.Id, AccessModeEnum.ReadWriteWithApproval, token).ConfigureAwait(false);
                    await ChatTestSupport.UseStubAsync(rig, stub, token).ConfigureAwait(false);

                    stub.Script.Then(StubLlmResponse.Message("Noted, Mr. Anderson."));
                    SseResult first = await PostAsync(rig.Anderson, Fixture("chat-session-1.json"), token).ConfigureAwait(false);
                    string conversationId = Completion(first).Hugo!.ConversationId;
                    stub.Script.Then(StubLlmResponse.Message("The word was swordfish."));
                    SseResult second = await PostAsync(rig.Anderson, Fixture("chat-session-2.json"), token).ConfigureAwait(false);
                    Assert.Equal(conversationId, Completion(second).Hugo!.ConversationId, "the user field keeps one conversation");
                    Conversation session = (await rig.Context.Database.Conversations.ReadAsync(rig.TenantId, conversationId, token).ConfigureAwait(false))!;
                    Assert.True(session.ExternalKey == "session-42" && session.MessageCount == 4, "server-side history, not the client's copy: " + session.MessageCount);

                    stub.Script.Then(StubLlmResponse.Message("Still swordfish."));
                    SseResult byHeader = await PostAsync(rig.Anderson, "{\"model\":\"Hugo\",\"messages\":[{\"role\":\"user\",\"content\":\"Once more?\"}]}", token, conversationId).ConfigureAwait(false);
                    Assert.Equal(conversationId, Completion(byHeader).Hugo!.ConversationId, "x-hugo-conversation continues a conversation");
                    Assert.True(stub.ChatRequests.Last(r => !r.Body.Contains("Write a short title", StringComparison.Ordinal)).Body.Contains("swordfish", StringComparison.Ordinal), "the model sees the earlier turns");
                    SseResult foreign = await PostAsync(rig.Anderson, "{\"model\":\"Hugo\",\"messages\":[{\"role\":\"user\",\"content\":\"x\"}]}", token, "cnv_missing").ConfigureAwait(false);
                    Assert.Equal(404, foreign.StatusCode, "unknown conversation header");

                    stub.Script.Then(StubLlmResponse.Message("Your name is Neo."));
                    SseResult stateless = await PostAsync(rig.Anderson, Fixture("chat-history.json"), token).ConfigureAwait(false);
                    string statelessId = Completion(stateless).Hugo!.ConversationId;
                    Assert.True(statelessId != conversationId, "no session key starts a new conversation");
                    Assert.True(stub.ChatRequests.Last(r => !r.Body.Contains("Write a short title", StringComparison.Ordinal)).Body.Contains("Hello, Neo.", StringComparison.Ordinal), "the supplied history is the context");
                    Conversation statelessConversation = (await rig.Context.Database.Conversations.ReadAsync(rig.TenantId, statelessId, token).ConfigureAwait(false))!;
                    Assert.Equal(4, statelessConversation.MessageCount, "seeded history plus the turn");

                    stub.Script.Then(StubLlmResponse.ToolCall("echo_write", "{\"resource\":\"echo-a\",\"text\":\"restart\"}"));
                    SseResult pending = await PostAsync(rig.Anderson, Fixture("chat-approval.json"), token).ConfigureAwait(false);
                    OpenAIChatResponse pendingCompletion = Completion(pending);
                    string? approvalId = pendingCompletion.Hugo?.PendingApprovalId;
                    Assert.True(approvalId != null && approvalId.StartsWith("apr_", StringComparison.Ordinal) && pendingCompletion.Hugo!.FinishReason == "approval_pending", "the turn ends pending: " + pending.Body);
                    string content = pendingCompletion.Choices[0].Message?.Content ?? String.Empty;
                    Assert.True(content.Contains(approvalId!, StringComparison.Ordinal) && content.Contains("pending", StringComparison.Ordinal), "an assistant message explains the pending approval: " + content);
                    Approval approval = (await rig.Context.Database.Approvals.ReadAsync(rig.TenantId, approvalId!, token).ConfigureAwait(false))!;
                    Assert.True(approval.Status == ApprovalStatusEnum.Pending && approval.Surface == InvocationSurfaceEnum.OpenAI, "the approval waits in the inbox");
                }
            }
        }

        #endregion
    }
}
