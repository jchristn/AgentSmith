namespace Test.Automated.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Hugo.Sdk;
    using Hugo.Sdk.Enumeration;
    using Hugo.Sdk.Enums;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Requests;
    using Hugo.Sdk.Responses;

    /// <summary>
    /// Model-backed routes against a stub OpenAI-compatible model the harness runs on 127.0.0.1: model endpoint
    /// validation (saved and draft), health history, provider models, conversations with real agent turns (JSON and
    /// streamed), ask, and analytics.
    /// </summary>
    public class ModelSuite : TestSuite
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Name
        {
            get
            {
                return "Model";
            }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override async Task RunAsync(TestRunner runner, HarnessContext ctx)
        {
            HugoClient c = ctx.Owner;
            using (StubLlmServer stub = new StubLlmServer())
            {
                Secret? key = null;
                ModelEndpoint? endpoint = null;
                AgentProfile? profile = null;
                DateTime started = DateTime.UtcNow.AddMinutes(-1);
                try
                {
                    await runner.RunAsync("Model endpoint against a stub model: validate, draft, history, models", async () =>
                    {
                        SecretRequest secret = new SecretRequest();
                        secret.Name = Unique("stub-key");
                        secret.Kind = SecretKindEnum.BearerToken;
                        secret.Values = new Dictionary<string, string> { { "token", "stub-api-key-" + HarnessContext.RandomHex(4) } };
                        key = await c.Secrets.CreateAsync(secret).ConfigureAwait(false);

                        ModelEndpointRequest draft = Endpoint(stub, "draft:" + Unique("stub"), key.Id);
                        ModelValidationResult checkedDraft = await c.ModelEndpoints.ValidateDraftAsync(draft).ConfigureAwait(false);
                        Check.True(checkedDraft.Success, "draft validation against the stub: " + checkedDraft.Error);
                        Check.NotEmpty(checkedDraft.ResponsePreview, "draft reply preview");

                        endpoint = await c.ModelEndpoints.CreateAsync(Endpoint(stub, "stub:" + Unique("model") + " @harness", key.Id)).ConfigureAwait(false);
                        Check.Equal(key.Id, endpoint.ApiKeySecretId, "BearerToken secret accepted as the API key");
                        ModelValidationResult validation = await c.ModelEndpoints.ValidateAsync(endpoint.Id).ConfigureAwait(false);
                        Check.True(validation.Success, "validation against the stub: " + validation.Error);
                        ModelEndpoint healthy = await c.ModelEndpoints.GetAsync(endpoint.Id).ConfigureAwait(false);
                        Check.Equal("Healthy", healthy.HealthStatus, "health recorded");
                        List<ModelEndpointHealthRecord> history = await c.ModelEndpoints.GetHealthHistoryAsync(endpoint.Id).ConfigureAwait(false);
                        Check.True(history.Count >= 1 && history[0].Status == "Healthy" && history[0].Source == "Validate", "health history records the validation");
                        ModelEndpointFilter filter = new ModelEndpointFilter();
                        filter.Health = "Healthy";
                        filter.Provider = "OpenAICompatible";
                        EnumerationResult<ModelEndpoint> page = await c.ModelEndpoints.ListAsync(filter).ConfigureAwait(false);
                        Check.True(page.Objects.Any(m => m.Id == endpoint.Id), "health=Healthy filter");
                        List<string> models = await c.ModelEndpoints.ListModelsAsync(endpoint.Id).ConfigureAwait(false);
                        Check.True(models.Contains(stub.ModelName), "provider models listed");

                        AgentProfileRequest create = new AgentProfileRequest();
                        create.Name = Unique("stubprofile");
                        create.Description = "profile backed by the harness stub model";
                        create.InferenceEndpointId = endpoint.Id;
                        profile = await c.AgentProfiles.CreateAsync(create).ConfigureAwait(false);
                        Check.Equal(endpoint.Id, profile.InferenceEndpointId, "profile uses the stub endpoint");
                    }).ConfigureAwait(false);

                    await runner.RunAsync("Conversation turn as JSON, messages page, export, cancel", async () =>
                    {
                        AgentProfile p = Require(profile);
                        ConversationRequest create = new ConversationRequest();
                        create.AgentProfileId = p.Id;
                        create.Title = Unique("chat");
                        Conversation conversation = await c.Conversations.CreateAsync(create).ConfigureAwait(false);
                        try
                        {
                            Check.Equal(p.Id, conversation.AgentProfileId, "conversation profile");
                            EnumerationResult<ConversationMessage> empty = await c.Conversations.ListMessagesAsync(conversation.Id).ConfigureAwait(false);
                            Check.Equal(0, empty.Objects.Count, "no messages yet");

                            AgentTurnResult turn = await c.Conversations.SendMessageAsync(conversation.Id, "Say hello.").ConfigureAwait(false);
                            Check.Equal("Completed", turn.FinishReason, "finish reason: " + turn.ErrorMessage);
                            Check.Equal(stub.Reply, turn.FinalText, "final text from the stub");
                            Check.Equal(conversation.Id, turn.ConversationId, "turn conversation");
                            Check.NotEmpty(turn.TurnId, "turn id");
                            Check.True(turn.Messages.Any(m => m.Role == "Assistant"), "assistant message in the turn");

                            EnumerationResult<ConversationMessage> messages = await c.Conversations.ListMessagesAsync(conversation.Id).ConfigureAwait(false);
                            Check.True(messages.Objects.Count >= 2, "user and assistant messages stored");
                            Check.True(messages.Objects.Any(m => m.Role == "User" && m.Content == "Say hello."), "user message stored");
                            Check.True(messages.Objects.Any(m => m.Role == "Assistant" && m.Content == stub.Reply), "assistant message stored");
                            Check.True(messages.Objects.Select(m => m.Sequence).SequenceEqual(messages.Objects.Select(m => m.Sequence).OrderBy(s => s)), "messages in order");
                            EnumerationQuery one = new EnumerationQuery();
                            one.MaxResults = 1;
                            EnumerationResult<ConversationMessage> first = await c.Conversations.ListMessagesAsync(conversation.Id, one).ConfigureAwait(false);
                            Check.True(first.Objects.Count == 1 && !first.EndOfResults, "messages are paged");

                            Conversation read = await c.Conversations.GetAsync(conversation.Id).ConfigureAwait(false);
                            Check.True(read.MessageCount >= 2, "message count");
                            string markdown = await c.Conversations.ExportAsync(conversation.Id).ConfigureAwait(false);
                            Check.True(markdown.Contains(stub.Reply, StringComparison.Ordinal), "Markdown export carries the reply");
                            string json = await c.Conversations.ExportAsync(conversation.Id, "json").ConfigureAwait(false);
                            Check.True(json.TrimStart().StartsWith("{", StringComparison.Ordinal), "JSON export");
                            await Check.FailsAsync(() => c.Conversations.ExportAsync(conversation.Id, "pdf"), 400).ConfigureAwait(false);
                            ConversationCancelResponse cancel = await c.Conversations.CancelAsync(conversation.Id).ConfigureAwait(false);
                            Check.True(cancel.ConversationId == conversation.Id && !cancel.Cancelled, "nothing to cancel when idle");

                            ConversationRequest rename = new ConversationRequest();
                            rename.Title = "renamed by the harness";
                            Conversation renamed = await c.Conversations.UpdateAsync(conversation.Id, rename).ConfigureAwait(false);
                            Check.Equal("renamed by the harness", renamed.Title, "rename");

                            ConversationFilter byProfile = new ConversationFilter();
                            byProfile.AgentProfileId = p.Id;
                            byProfile.Source = "Dashboard";
                            EnumerationResult<Conversation> listed = await c.Conversations.ListAsync(byProfile).ConfigureAwait(false);
                            Check.True(listed.Objects.Any(x => x.Id == conversation.Id), "agentProfileId and source filters");
                            ConversationFilter badSource = new ConversationFilter();
                            badSource.Source = "Telepathy";
                            await Check.FailsAsync(() => c.Conversations.ListAsync(badSource), 400, "ValidationFailed").ConfigureAwait(false);
                        }
                        finally
                        {
                            await c.Conversations.DeleteAsync(conversation.Id).ConfigureAwait(false);
                        }
                        await Check.FailsAsync(() => c.Conversations.GetAsync(conversation.Id), 404).ConfigureAwait(false);
                    }).ConfigureAwait(false);

                    await runner.RunAsync("Chat streaming emits typed events", async () =>
                    {
                        AgentProfile p = Require(profile);
                        ConversationRequest create = new ConversationRequest();
                        create.AgentProfileId = p.Id;
                        Conversation conversation = await c.Conversations.CreateAsync(create).ConfigureAwait(false);
                        try
                        {
                            List<string> types = new List<string>();
                            StringBuilder text = new StringBuilder();
                            ChatTurnCompleted? completed = null;
                            ChatStreamClosed? closed = null;
                            await foreach (ChatEvent item in c.Conversations.StreamMessageAsync(conversation.Id, "Say hello.").ConfigureAwait(false))
                            {
                                types.Add(item.Type);
                                if (item.Type == ChatEventTypes.AssistantDelta) text.Append(item.DataAs<ChatAssistantDelta>()?.Text);
                                if (item.Type == ChatEventTypes.TurnCompleted) completed = item.DataAs<ChatTurnCompleted>();
                                if (item.Type == ChatEventTypes.StreamClosed) closed = item.DataAs<ChatStreamClosed>();
                            }
                            Check.True(types.Contains(ChatEventTypes.TurnStarted), "turn.started");
                            Check.True(types.Contains(ChatEventTypes.AssistantDelta), "assistant.delta");
                            Check.True(completed != null, "turn.completed, got: " + String.Join(", ", types));
                            Check.Equal(stub.Reply, completed!.FinalText, "final text");
                            Check.Equal(stub.Reply, text.ToString(), "deltas add up to the reply");
                            Check.Equal(ChatEventTypes.StreamClosed, types.Last(), "stream.closed is last");
                            Check.Equal(conversation.Id, closed!.ConversationId, "stream.closed conversation");
                        }
                        finally
                        {
                            await c.Conversations.DeleteAsync(conversation.Id).ConfigureAwait(false);
                        }
                    }).ConfigureAwait(false);

                    await runner.RunAsync("Ask an agent profile", async () =>
                    {
                        AgentProfile p = Require(profile);
                        AgentDelegationResult answer = await c.AgentProfiles.AskAsync(p.Id, "Is the stub up?").ConfigureAwait(false);
                        Check.Equal("Completed", answer.Status, "status");
                        Check.Equal(stub.Reply, answer.FinalText, "final text");
                        Check.NotEmpty(answer.ConversationId, "conversation id");
                        AgentDelegationResult followUp = await c.AgentProfiles.AskAsync(p.Id, "And again?", answer.ConversationId).ConfigureAwait(false);
                        Check.Equal(answer.ConversationId, followUp.ConversationId, "follow-up continues the conversation");
                        EnumerationResult<ConversationMessage> messages = await c.Conversations.ListMessagesAsync(answer.ConversationId!).ConfigureAwait(false);
                        Check.True(messages.Objects.Count(m => m.Role == "User") >= 2, "both questions stored");
                        await Check.FailsAsync(() => c.AgentProfiles.AskAsync("prof_does_not_exist", "hello"), 404).ConfigureAwait(false);
                    }).ConfigureAwait(false);

                    await runner.RunAsync("Analytics overview, tokens, tools, and models", async () =>
                    {
                        string endpointId = Require(endpoint).Id;
                        DateTime to = DateTime.UtcNow.AddMinutes(1);
                        AnalyticsReport overview = await c.Analytics.GetOverviewAsync(started, to).ConfigureAwait(false);
                        Check.Equal("overview", overview.Kind, "kind");
                        Check.Equal("minute", overview.Interval, "interval chosen by window length");
                        Check.True(overview.Totals.Turns >= 2, "turns of the kept ask conversation counted (deleted conversations take their turns with them) (" + overview.Totals.Turns + ")");
                        Check.True(overview.Conversations >= 1, "conversations counted");
                        Check.True(overview.Buckets.Count >= 1 && overview.Buckets.Sum(b => b.Turns) == overview.Totals.Turns, "buckets add up to the totals");
                        AnalyticsReport tokens = await c.Analytics.GetTokensAsync(started, to, "hour").ConfigureAwait(false);
                        Check.Equal("hour", tokens.Interval, "explicit interval");
                        Check.True(tokens.Totals.InputTokens > 0 && tokens.Totals.OutputTokens > 0, "token totals");
                        AnalyticsReport tools = await c.Analytics.GetToolsAsync().ConfigureAwait(false);
                        Check.Equal("tools", tools.Kind, "tools kind with the default window");
                        AnalyticsReport models = await c.Analytics.GetModelsAsync(started, to).ConfigureAwait(false);
                        Check.True(models.Models.Any(m => m.ModelEndpointId == endpointId && m.Turns >= 2), "per-model row for the stub endpoint");
                        await Check.FailsAsync(() => c.Analytics.GetOverviewAsync(started, to, "fortnight"), 400, "ValidationFailed").ConfigureAwait(false);
                        await Check.FailsAsync(() => c.Analytics.GetOverviewAsync(to, started), 400, "ValidationFailed").ConfigureAwait(false);
                    }).ConfigureAwait(false);
                }
                finally
                {
                    if (profile != null) await Quietly(() => c.AgentProfiles.DeleteAsync(profile.Id)).ConfigureAwait(false);
                    if (endpoint != null) await Quietly(() => c.ModelEndpoints.DeleteAsync(endpoint.Id)).ConfigureAwait(false);
                    if (key != null) await Quietly(() => c.Secrets.DeleteAsync(key.Id)).ConfigureAwait(false);
                }
            }
        }

        #endregion

        #region Private-Methods

        private static ModelEndpointRequest Endpoint(StubLlmServer stub, string name, string keySecretId)
        {
            ModelEndpointRequest request = new ModelEndpointRequest();
            request.Name = name;
            request.Kind = "Inference";
            request.Provider = "OpenAICompatible";
            request.BaseUrl = stub.BaseUrl;
            request.Model = stub.ModelName;
            request.ApiKeySecretId = keySecretId;
            request.MaxRetries = 0;
            request.TimeoutSeconds = 30;
            return request;
        }

        private static T Require<T>(T? value) where T : class
        {
            if (value == null) throw new TestAssertionException("an earlier Model test did not create its fixture");
            return value;
        }

        private static async Task Quietly(Func<Task> action)
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (HugoException)
            {
            }
        }

        #endregion
    }
}
