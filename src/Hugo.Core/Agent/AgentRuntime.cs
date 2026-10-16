namespace Hugo.Core.Agent
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Agent.Events;
    using Hugo.Core.Inference;
    using PolyPrompt.Models;

    /// <summary>
    /// Hugo's own agent loop (plan 13.3), used by dashboard chat, A2A, the OpenAI-compatible surface, and
    /// <c>ask_agent_smith</c>. Built on Pneuma <c>AgenticChatService</c> (streaming loop, think blocks, forced final
    /// answer, compaction), Wilson <c>ToolAgentService</c> (approval round trip), and AssistantHub
    /// <c>AssistantToolPolicy</c> (limits).
    /// <para>
    /// Parallelism: the pipeline decides each call's effect, and the runtime cannot know a per-call classification
    /// in advance. Calls run in parallel only when the catalog declares the tool's effect is always Read
    /// (<see cref="AgentToolDefinition.KnownEffect"/>); consecutive such calls form a group of up to
    /// <see cref="AgentRuntimeSettings.MaxParallelToolCalls"/>. Every other call runs alone, in request order.
    /// </para>
    /// Thread-safe: one instance serves every turn; a per-conversation lock admits one turn per conversation.
    /// </summary>
    public class AgentRuntime
    {
        #region Public-Members

        /// <summary>
        /// Default settings, used when a request carries none. Never null.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null.</exception>
        public AgentRuntimeSettings Settings
        {
            get
            {
                return _Settings;
            }
            set
            {
                _Settings = value ?? throw new ArgumentNullException(nameof(Settings));
            }
        }

        /// <summary>
        /// Per-conversation locks.
        /// </summary>
        public ConversationLockManager Locks
        {
            get
            {
                return _Locks;
            }
        }

        /// <summary>
        /// Tool description embedding cache.
        /// </summary>
        public ToolEmbeddingCache ToolEmbeddings
        {
            get
            {
                return _Cache;
            }
        }

        /// <summary>
        /// Creates IDs from a prefix (<c>trn_</c>, <c>msg_</c>, <c>call_</c>). The server sets this to PrettyId
        /// K-sortable generation. Default: prefix plus a random GUID in hex. Never null.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null.</exception>
        public Func<string, string> IdFactory
        {
            get
            {
                return _IdFactory;
            }
            set
            {
                _IdFactory = value ?? throw new ArgumentNullException(nameof(IdFactory));
            }
        }

        #endregion

        #region Private-Members

        private readonly IModelGateway _Gateway;
        private readonly IConversationStore _Store;
        private readonly ConversationLockManager _Locks;
        private readonly ToolEmbeddingCache _Cache;
        private readonly ToolRetriever _Retriever;
        private AgentRuntimeSettings _Settings;
        private Func<string, string> _IdFactory = prefix => prefix + Guid.NewGuid().ToString("N");

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="gateway">Model gateway.</param>
        /// <param name="store">Conversation store.</param>
        /// <param name="settings">Default settings; null uses defaults.</param>
        /// <param name="locks">Lock manager; null creates one.</param>
        /// <param name="cache">Tool embedding cache; null creates one.</param>
        /// <exception cref="ArgumentNullException">Thrown when gateway or store is null.</exception>
        public AgentRuntime(IModelGateway gateway, IConversationStore store, AgentRuntimeSettings? settings = null, ConversationLockManager? locks = null, ToolEmbeddingCache? cache = null)
        {
            _Gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _Store = store ?? throw new ArgumentNullException(nameof(store));
            _Settings = settings ?? new AgentRuntimeSettings();
            _Locks = locks ?? new ConversationLockManager();
            _Cache = cache ?? new ToolEmbeddingCache();
            _Retriever = new ToolRetriever(_Gateway, _Cache);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run one turn: lock the conversation, build context (with tool retrieval and compaction), loop over the
        /// model and tools, force a final answer on the last iteration, persist, and emit events. Cancellation does
        /// not throw: the turn ends with finish reason Cancelled and its partial messages are persisted. Model
        /// failures end the turn with finish reason Error.
        /// </summary>
        /// <param name="request">Turn request.</param>
        /// <param name="token">Cancellation token (client abort or the cancel route).</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="ArgumentException">Thrown when a required request field is missing.</exception>
        /// <exception cref="AgentTurnConflictException">Thrown when another turn holds the conversation.</exception>
        public async Task<AgentTurnResult> RunTurnAsync(AgentTurnRequest request, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (String.IsNullOrWhiteSpace(request.ConversationId)) throw new ArgumentException("ConversationId is required.", nameof(request));
            if (request.InferenceEndpoint == null) throw new ArgumentException("InferenceEndpoint is required.", nameof(request));
            if (request.UserMessage == null) throw new ArgumentException("UserMessage is required.", nameof(request));
            if (request.ToolCatalog != null && request.ToolExecutor == null) throw new ArgumentException("ToolExecutor is required when ToolCatalog is set.", nameof(request));
            if (String.IsNullOrEmpty(request.TurnId)) request.TurnId = _IdFactory("trn_");
            AgentRuntimeSettings settings = request.Settings ?? _Settings;

            using (ConversationLockLease lease = await _Locks.AcquireAsync(request.ConversationId, TimeSpan.FromSeconds(settings.ConcurrentTurnWaitSeconds), token).ConfigureAwait(false))
            {
                AgentEventPublisher publisher = new AgentEventPublisher(request.EventSink, request.TurnId, request.ConversationId);
                AgentTurnState state = new AgentTurnState(request, settings, request.InferenceEndpoint, publisher);
                return await RunLockedAsync(state, token).ConfigureAwait(false);
            }
        }

        #endregion

        #region Private-Methods

        private async Task<AgentTurnResult> RunLockedAsync(AgentTurnState state, CancellationToken token)
        {
            AgentTurnRequest request = state.Request;
            AgentTurnResult result = state.Result;
            Activity? span = AgentTelemetry.StartActivity("agent.turn");
            AgentTelemetry.SetTag(span, "hugo.surface", request.Surface);
            AgentTelemetry.SetTag(span, "hugo.conversation_id", request.ConversationId);
            AgentTelemetry.SetTag(span, "hugo.turn_id", request.TurnId);

            try
            {
                long stageStart = Stopwatch.GetTimestamp();
                List<AgentMessage> history = await _Store.LoadHistoryAsync(request.ConversationId, token).ConfigureAwait(false);
                List<AgentToolDefinition> catalog = request.ToolCatalog != null
                    ? (await request.ToolCatalog.GetToolsAsync(request, token).ConfigureAwait(false) ?? new List<AgentToolDefinition>())
                    : new List<AgentToolDefinition>();
                result.ToolsAvailable = catalog.Count;

                AgentMessage userMessage = NewMessage(state, AgentMessageRoleEnum.User, request.UserMessage);
                await _Store.AppendMessagesAsync(request.ConversationId, new List<AgentMessage> { userMessage }, token).ConfigureAwait(false);
                state.UserMessagePersisted = true;
                result.Messages.Add(userMessage);
                Stage("context_build", stageStart);

                await state.Publisher.PublishAsync(new TurnStartedEvent
                {
                    Surface = request.Surface,
                    ProfileId = request.ProfileId,
                    PrincipalId = request.PrincipalId,
                    EndpointKey = state.Endpoint.Key,
                    Model = state.Endpoint.Model,
                    UserMessageId = userMessage.Id
                }, token).ConfigureAwait(false);

                List<AgentToolDefinition> offered = await SelectToolsAsync(state, catalog, token).ConfigureAwait(false);
                history = await CompactIfNeededAsync(state, history, offered, token).ConfigureAwait(false);
                List<ChatMessage> messages = BuildContext(state, history);
                await LoopAsync(state, messages, catalog, offered, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                result.FinishReason = AgentFinishReasonEnum.Cancelled;
            }
            catch (ModelEndpointException e)
            {
                await FailAsync(state, "model_unavailable", e.Message).ConfigureAwait(false);
                AgentTelemetry.SetError(span, e.Code);
            }
            catch (Exception e)
            {
                await FailAsync(state, "internal_error", e.Message).ConfigureAwait(false);
                AgentTelemetry.SetError(span, "internal_error");
            }

            await FinishAsync(state).ConfigureAwait(false);
            AgentTelemetry.SetTag(span, "hugo.finish_reason", result.FinishReason.ToString());
            AgentTelemetry.SetTag(span, "hugo.iterations", result.Iterations);
            AgentTelemetry.Stop(span);
            return result;
        }

        private async Task<List<AgentToolDefinition>> SelectToolsAsync(AgentTurnState state, List<AgentToolDefinition> catalog, CancellationToken token)
        {
            if (!state.Endpoint.SupportsTools || catalog.Count == 0)
            {
                state.Result.ToolsOffered = 0;
                return new List<AgentToolDefinition>();
            }

            long start = Stopwatch.GetTimestamp();
            ToolRetrievalResult selection = await _Retriever.SelectAsync(
                catalog,
                state.Request.UserMessage,
                state.Request.EmbeddingEndpoint,
                state.Settings.ToolRetrievalThreshold,
                state.Settings.ToolRetrievalTopK,
                token).ConfigureAwait(false);
            if (selection.UsedRetrieval || (selection.Reason != null && selection.Reason.StartsWith("retrieval_failed", StringComparison.Ordinal)))
                Stage("tool_retrieval", start);
            if (selection.Reason != null && selection.Reason.StartsWith("retrieval_failed", StringComparison.Ordinal))
            {
                await state.Publisher.PublishAsync(new AgentErrorEvent { Code = "tool_retrieval_failed", Message = selection.Reason, Fatal = false }, token).ConfigureAwait(false);
            }
            state.Result.ToolRetrievalUsed = selection.UsedRetrieval;
            state.Result.ToolsOffered = selection.Tools.Count;
            return selection.Tools;
        }

        private async Task<List<AgentMessage>> CompactIfNeededAsync(AgentTurnState state, List<AgentMessage> history, List<AgentToolDefinition> offered, CancellationToken token)
        {
            AgentRuntimeSettings settings = state.Settings;
            string referenceContext = await FreshContextAsync(state, token).ConfigureAwait(false);
            long estimate = TokenEstimator.Estimate(state.Request.SystemPrompt)
                + TokenEstimator.Estimate(referenceContext)
                + TokenEstimator.Estimate(settings.ToolOutputPosture)
                + history.Sum(m => TokenEstimator.Estimate(m))
                + TokenEstimator.Estimate(state.Request.UserMessage)
                + TokenEstimator.Estimate(offered);
            long budget = (long)(state.Endpoint.ContextWindowTokens * settings.CompactionThresholdRatio);
            if (estimate <= budget) return history;

            int split = history.Count - settings.CompactionKeepRecentMessages;
            while (split > 0 && split < history.Count && history[split].Role == AgentMessageRoleEnum.Tool) split--;
            if (split <= 0) return history;
            List<AgentMessage> covered = history.Take(split).ToList();
            if (covered.Count == 1 && covered[0].Role == AgentMessageRoleEnum.Summary) return history;

            long start = Stopwatch.GetTimestamp();
            Activity? span = AgentTelemetry.StartActivity("stage:compaction");
            try
            {
                StringBuilder transcript = new StringBuilder();
                foreach (AgentMessage m in covered)
                {
                    string label;
                    switch (m.Role)
                    {
                        case AgentMessageRoleEnum.User: label = "User"; break;
                        case AgentMessageRoleEnum.Assistant: label = "Assistant"; break;
                        case AgentMessageRoleEnum.Tool: label = "Tool result (" + (m.ToolName ?? "tool") + ")"; break;
                        case AgentMessageRoleEnum.Summary: label = "Earlier summary"; break;
                        default: label = "Note"; break;
                    }
                    string content = m.Content;
                    if (content.Length > settings.CompactionMessageMaxChars) content = content.Substring(0, settings.CompactionMessageMaxChars) + " [truncated]";
                    transcript.Append(label).Append(": ").AppendLine(content);
                    foreach (AgentMessageToolCall c in m.ToolCalls) transcript.Append("Assistant called ").Append(c.Name).Append(' ').AppendLine(c.ArgumentsJson);
                }

                ModelChatCall call = new ModelChatCall
                {
                    Endpoint = state.Endpoint,
                    Fallback = state.Request.FallbackEndpoint,
                    Messages = await ModelMessagesAsync(state, new List<ChatMessage> { ChatMessage.System(settings.CompactionPrompt), ChatMessage.User(transcript.ToString()) }, token).ConfigureAwait(false),
                    Stream = false,
                    Kind = "chat"
                };
                ModelChatResult summary = await _Gateway.ChatAsync(call, token).ConfigureAwait(false);
                state.AddUsage(summary);
                string text = ThinkBlockParser.Strip(summary.Text).Trim();
                if (String.IsNullOrEmpty(text)) throw new InvalidOperationException("The model returned an empty summary.");

                AgentMessage summaryMessage = NewMessage(state, AgentMessageRoleEnum.Summary, text);
                summaryMessage.TokensIn = summary.InputTokens;
                summaryMessage.TokensOut = summary.OutputTokens;
                List<AgentMessage> compacted = new List<AgentMessage> { summaryMessage };
                compacted.AddRange(history.Skip(split));
                AgentCompaction compaction = new AgentCompaction
                {
                    Summary = summaryMessage,
                    CoveredMessageIds = covered.Select(m => m.Id).ToList(),
                    TokensBefore = history.Sum(m => TokenEstimator.Estimate(m)),
                    TokensAfter = compacted.Sum(m => TokenEstimator.Estimate(m))
                };
                await _Store.SaveCompactionAsync(state.Request.ConversationId, compaction, token).ConfigureAwait(false);
                state.Result.Compacted = true;
                return compacted;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                AgentTelemetry.SetError(span, "compaction_failed");
                await state.Publisher.PublishAsync(new AgentErrorEvent { Code = "compaction_failed", Message = "Conversation compaction failed; continuing with the full history. " + e.Message, Fatal = false }, token).ConfigureAwait(false);
                return history;
            }
            finally
            {
                AgentTelemetry.Stop(span);
                Stage("compaction", start);
            }
        }

        private static List<ChatMessage> BuildContext(AgentTurnState state, List<AgentMessage> history)
        {
            StringBuilder system = new StringBuilder(state.Request.SystemPrompt ?? String.Empty);
            if (!String.IsNullOrWhiteSpace(state.Settings.ToolOutputPosture))
            {
                if (system.Length > 0) system.Append("\n\n");
                system.Append(state.Settings.ToolOutputPosture);
            }
            foreach (AgentMessage m in history.Where(m => m.Role == AgentMessageRoleEnum.Summary || m.Role == AgentMessageRoleEnum.System))
            {
                if (system.Length > 0) system.Append("\n\n");
                if (m.Role == AgentMessageRoleEnum.Summary) system.Append("## Summary of the earlier conversation\n");
                system.Append(m.Content);
            }

            List<ChatMessage> messages = new List<ChatMessage> { ChatMessage.System(system.ToString()) };
            foreach (AgentMessage m in history)
            {
                switch (m.Role)
                {
                    case AgentMessageRoleEnum.User:
                        messages.Add(ChatMessage.User(m.Content));
                        break;
                    case AgentMessageRoleEnum.Assistant:
                        if (m.ToolCalls.Count > 0)
                        {
                            ChatMessage assistant = new ChatMessage { Role = "assistant", Content = m.Content };
                            assistant.ToolCalls.AddRange(m.ToolCalls.Select(c => new ToolCall { Id = c.Id, Name = c.Name, ArgumentsJson = c.ArgumentsJson }));
                            messages.Add(assistant);
                        }
                        else
                        {
                            messages.Add(ChatMessage.Assistant(m.Content));
                        }
                        break;
                    case AgentMessageRoleEnum.Tool:
                        messages.Add(ChatMessage.ToolResult(m.ToolCallId, m.ToolName ?? "tool", m.Content));
                        break;
                    default:
                        break;
                }
            }
            messages.Add(ChatMessage.User(state.Request.UserMessage));
            return messages;
        }

        private async Task LoopAsync(AgentTurnState state, List<ChatMessage> messages, List<AgentToolDefinition> catalog, List<AgentToolDefinition> offered, CancellationToken token)
        {
            AgentRuntimeSettings settings = state.Settings;
            AgentTurnResult result = state.Result;
            List<ToolDefinition> toolDefs = offered.Select(ToToolDefinition).ToList();
            Dictionary<string, AgentToolDefinition> byName = new Dictionary<string, AgentToolDefinition>(StringComparer.Ordinal);
            foreach (AgentToolDefinition t in catalog) byName[t.Name] = t;
            int toolCallsUsed = 0;
            bool forceFinal = false;
            AgentFinishReasonEnum forcedReason = AgentFinishReasonEnum.MaxIterations;

            for (int iteration = 1; iteration <= settings.MaxIterations; iteration++)
            {
                token.ThrowIfCancellationRequested();
                result.Iterations = iteration;
                bool final = iteration == settings.MaxIterations || forceFinal;
                bool forced = final && (forceFinal || iteration > 1);
                if (forced) messages.Add(ChatMessage.User(settings.FinalAnswerInstruction));

                ThinkBlockParser parser = new ThinkBlockParser();
                int current = iteration;
                ModelChatCall call = new ModelChatCall
                {
                    Endpoint = state.Endpoint,
                    Fallback = state.Request.FallbackEndpoint,
                    Messages = await ModelMessagesAsync(state, messages, token).ConfigureAwait(false),
                    Tools = toolDefs,
                    ToolChoice = toolDefs.Count == 0 ? null : (final ? "none" : "auto"),
                    Stream = settings.Streaming,
                    OnChunk = (chunk, ct) => OnChunkAsync(state, parser, current, chunk, ct)
                };

                long inferenceStart = Stopwatch.GetTimestamp();
                Activity? inferenceSpan = AgentTelemetry.StartActivity("stage:inference");
                ModelChatResult response;
                try
                {
                    response = await _Gateway.ChatAsync(call, token).ConfigureAwait(false);
                }
                finally
                {
                    AgentTelemetry.Stop(inferenceSpan);
                    Stage("inference", inferenceStart);
                }
                state.AddUsage(response);

                string tail = parser.Finish();
                await EmitTextAsync(state, current, tail, parser.LastThinkingDelta, token).ConfigureAwait(false);
                string visible = parser.Visible;
                string iterationThinking = parser.Thinking;
                if (!String.IsNullOrEmpty(response.Reasoning) && !response.Text.Contains(response.Reasoning, StringComparison.Ordinal) && String.IsNullOrEmpty(iterationThinking))
                    iterationThinking = response.Reasoning;
                if (!String.IsNullOrEmpty(iterationThinking))
                {
                    if (state.Thinking.Length > 0) state.Thinking.Append('\n');
                    state.Thinking.Append(iterationThinking.Trim());
                }

                List<ToolCall> toolCalls = final ? new List<ToolCall>() : response.ToolCalls.Where(c => !String.IsNullOrWhiteSpace(c.Name)).ToList();
                if (toolCalls.Count == 0)
                {
                    string finalText = visible.Trim();
                    if (String.IsNullOrEmpty(finalText))
                    {
                        finalText = settings.EmptyAnswerText;
                        state.MarkFirstToken();
                        await state.Publisher.PublishAsync(new AssistantDeltaEvent { Text = finalText, Iteration = current }, token).ConfigureAwait(false);
                    }
                    AgentMessage answer = NewMessage(state, AgentMessageRoleEnum.Assistant, finalText);
                    answer.Thinking = String.IsNullOrWhiteSpace(iterationThinking) ? null : iterationThinking.Trim();
                    answer.TokensIn = response.InputTokens;
                    answer.TokensOut = response.OutputTokens;
                    result.Messages.Add(answer);
                    result.FinalText = finalText;
                    result.FinishReason = forced ? forcedReason : AgentFinishReasonEnum.Completed;
                    return;
                }

                foreach (ToolCall tc in toolCalls)
                {
                    if (String.IsNullOrWhiteSpace(tc.Id)) tc.Id = _IdFactory("call_");
                    if (String.IsNullOrWhiteSpace(tc.ArgumentsJson)) tc.ArgumentsJson = "{}";
                }

                AgentMessage toolRequest = NewMessage(state, AgentMessageRoleEnum.Assistant, visible.Trim());
                toolRequest.Thinking = String.IsNullOrWhiteSpace(iterationThinking) ? null : iterationThinking.Trim();
                toolRequest.TokensIn = response.InputTokens;
                toolRequest.TokensOut = response.OutputTokens;
                toolRequest.ToolCalls = toolCalls.Select(c => new AgentMessageToolCall { Id = c.Id ?? String.Empty, Name = c.Name, ArgumentsJson = c.ArgumentsJson }).ToList();
                result.Messages.Add(toolRequest);
                ChatMessage assistantMessage = new ChatMessage { Role = "assistant", Content = visible.Trim() };
                assistantMessage.ToolCalls.AddRange(toolCalls);
                messages.Add(assistantMessage);

                List<AgentToolCall> calls = new List<AgentToolCall>();
                foreach (ToolCall tc in toolCalls)
                {
                    AgentToolDefinition? definition;
                    byName.TryGetValue(tc.Name, out definition);
                    AgentToolCall agentCall = new AgentToolCall
                    {
                        CallId = tc.Id ?? String.Empty,
                        ToolName = tc.Name,
                        ArgumentsJson = tc.ArgumentsJson,
                        Resource = ResourceArgument.Read(tc.ArgumentsJson),
                        Definition = definition,
                        Iteration = current
                    };
                    calls.Add(agentCall);
                    await state.Publisher.PublishAsync(new ToolProposedEvent
                    {
                        CallId = agentCall.CallId,
                        ToolName = agentCall.ToolName,
                        Resource = agentCall.Resource,
                        ArgumentsJson = agentCall.ArgumentsJson,
                        Iteration = current
                    }, token).ConfigureAwait(false);
                }

                int budget = Math.Max(0, settings.MaxToolCallsPerTurn - toolCallsUsed);
                List<AgentToolCall> executable = calls.Take(budget).ToList();
                toolCallsUsed += executable.Count;
                if (calls.Count > executable.Count || toolCallsUsed >= settings.MaxToolCallsPerTurn)
                {
                    forceFinal = true;
                    forcedReason = AgentFinishReasonEnum.ToolCallLimit;
                }

                Dictionary<string, AgentToolExecution> outcomes = await ExecuteCallsAsync(state, executable, token).ConfigureAwait(false);
                string? pendingApproval = null;
                string? pendingSummary = null;
                foreach (AgentToolCall c in calls)
                {
                    AgentToolExecution? outcome;
                    if (!outcomes.TryGetValue(c.CallId, out outcome))
                    {
                        outcome = new AgentToolExecution
                        {
                            Decision = AgentToolDecisionEnum.Denied,
                            DecisionCode = "tool_call_limit",
                            Result = ToolResult.Error("tool_call_limit", "Not executed: this turn reached its limit of " + settings.MaxToolCallsPerTurn + " tool calls. Answer with the information already gathered."),
                            Effect = c.Definition?.KnownEffect ?? EffectEnum.Read
                        };
                        result.ToolCalls.Add(Record(state, c, outcome, false));
                        await PublishCompletedAsync(state, c, outcome, token).ConfigureAwait(false);
                    }
                    string wrapped = ToolOutputWrapper.Wrap(outcome.Resource ?? c.Resource, c.ToolName, outcome.Result);
                    messages.Add(ChatMessage.ToolResult(c.CallId, c.ToolName, wrapped));
                    AgentMessage toolMessage = NewMessage(state, AgentMessageRoleEnum.Tool, wrapped);
                    toolMessage.ToolCallId = c.CallId;
                    toolMessage.ToolName = c.ToolName;
                    result.Messages.Add(toolMessage);
                    if (outcome.Decision == AgentToolDecisionEnum.ApprovalRequired && pendingApproval == null)
                    {
                        pendingApproval = outcome.ApprovalId ?? String.Empty;
                        pendingSummary = outcome.ApprovalSummary ?? outcome.Result.Text;
                    }
                }

                if (pendingApproval != null)
                {
                    string text = "Approval " + pendingApproval + " is pending: " + (pendingSummary ?? "a tool call needs approval") + ". I will continue once it is decided.";
                    state.MarkFirstToken();
                    await state.Publisher.PublishAsync(new AssistantDeltaEvent { Text = text, Iteration = current }, token).ConfigureAwait(false);
                    result.Messages.Add(NewMessage(state, AgentMessageRoleEnum.Assistant, text));
                    result.FinalText = text;
                    result.PendingApprovalId = pendingApproval;
                    result.FinishReason = AgentFinishReasonEnum.ApprovalPending;
                    return;
                }
            }
        }

        private static async Task<string> FreshContextAsync(AgentTurnState state, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (state.Request.ContextProvider == null) return String.Empty;
            return await state.Request.ContextProvider.GetContextAsync(state.Request, token).ConfigureAwait(false) ?? String.Empty;
        }

        private static async Task<List<ChatMessage>> ModelMessagesAsync(AgentTurnState state, List<ChatMessage> messages, CancellationToken token)
        {
            string referenceContext = await FreshContextAsync(state, token).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(referenceContext)) return messages;
            List<ChatMessage> current = new List<ChatMessage>(messages);
            current.Insert(current.Count > 0 && current[0].Role == "system" ? 1 : 0, ChatMessage.System(referenceContext));
            return current;
        }

        private async Task OnChunkAsync(AgentTurnState state, ThinkBlockParser parser, int iteration, ToolChatStreamingChunk chunk, CancellationToken token)
        {
            if (!String.IsNullOrEmpty(chunk.ReasoningText))
            {
                state.MarkFirstToken();
                await state.Publisher.PublishAsync(new AssistantThinkingEvent { Text = chunk.ReasoningText, Iteration = iteration }, token).ConfigureAwait(false);
            }
            if (!String.IsNullOrEmpty(chunk.Text))
            {
                string visible = parser.Feed(chunk.Text);
                await EmitTextAsync(state, iteration, visible, parser.LastThinkingDelta, token).ConfigureAwait(false);
            }
        }

        private static async Task EmitTextAsync(AgentTurnState state, int iteration, string visible, string thinking, CancellationToken token)
        {
            if (!String.IsNullOrEmpty(thinking))
            {
                state.MarkFirstToken();
                await state.Publisher.PublishAsync(new AssistantThinkingEvent { Text = thinking, Iteration = iteration }, token).ConfigureAwait(false);
            }
            if (!String.IsNullOrEmpty(visible))
            {
                state.MarkFirstToken();
                await state.Publisher.PublishAsync(new AssistantDeltaEvent { Text = visible, Iteration = iteration }, token).ConfigureAwait(false);
            }
        }

        private async Task<Dictionary<string, AgentToolExecution>> ExecuteCallsAsync(AgentTurnState state, List<AgentToolCall> calls, CancellationToken token)
        {
            Dictionary<string, AgentToolExecution> outcomes = new Dictionary<string, AgentToolExecution>(StringComparer.Ordinal);
            int maxParallel = state.Settings.MaxParallelToolCalls;
            int i = 0;
            while (i < calls.Count)
            {
                token.ThrowIfCancellationRequested();
                List<AgentToolCall> group = new List<AgentToolCall> { calls[i] };
                if (maxParallel > 1 && IsKnownRead(calls[i]))
                {
                    int j = i + 1;
                    while (j < calls.Count && group.Count < maxParallel && IsKnownRead(calls[j]))
                    {
                        group.Add(calls[j]);
                        j++;
                    }
                }
                i += group.Count;

                bool parallel = group.Count > 1;
                AgentToolExecution[] results = parallel
                    ? await Task.WhenAll(group.Select(c => ExecuteOneAsync(state, c, true, token))).ConfigureAwait(false)
                    : new AgentToolExecution[] { await ExecuteOneAsync(state, group[0], false, token).ConfigureAwait(false) };
                for (int k = 0; k < group.Count; k++)
                {
                    outcomes[group[k].CallId] = results[k];
                    state.Result.ToolCalls.Add(Record(state, group[k], results[k], parallel));
                }
            }
            return outcomes;
        }

        private static bool IsKnownRead(AgentToolCall call)
        {
            return call.Definition != null && call.Definition.KnownEffect.HasValue && call.Definition.KnownEffect.Value == EffectEnum.Read;
        }

        private async Task<AgentToolExecution> ExecuteOneAsync(AgentTurnState state, AgentToolCall call, bool parallel, CancellationToken token)
        {
            await state.Publisher.PublishAsync(new ToolStartedEvent
            {
                CallId = call.CallId,
                ToolName = call.ToolName,
                Resource = call.Resource,
                Parallel = parallel,
                Iteration = call.Iteration
            }, token).ConfigureAwait(false);

            AgentApprovalRelay relay = new AgentApprovalRelay(state.Publisher, call);
            long start = Stopwatch.GetTimestamp();
            Activity? span = AgentTelemetry.StartActivity("stage:tool");
            AgentTelemetry.SetTag(span, "hugo.tool", call.ToolName);
            AgentToolExecution outcome;
            try
            {
                if (call.Definition == null)
                {
                    outcome = new AgentToolExecution
                    {
                        Decision = AgentToolDecisionEnum.Denied,
                        DecisionCode = "unknown_tool",
                        Result = ToolResult.Error("unknown_tool", "Tool '" + call.ToolName + "' is not available to this profile. Use one of the tools you were given.")
                    };
                }
                else if (state.Request.ToolExecutor == null)
                {
                    outcome = new AgentToolExecution
                    {
                        Decision = AgentToolDecisionEnum.Denied,
                        DecisionCode = "no_executor",
                        Result = ToolResult.Error("no_executor", "Tools cannot run in this context.")
                    };
                }
                else
                {
                    try
                    {
                        outcome = await state.Request.ToolExecutor.ExecuteAsync(state.Request, call, relay, token).ConfigureAwait(false) ?? new AgentToolExecution();
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        outcome = new AgentToolExecution { Result = ToolResult.Error("tool_failed", "The tool call failed: " + e.Message) };
                        AgentTelemetry.SetError(span, "tool_failed");
                    }
                }
            }
            finally
            {
                AgentTelemetry.Stop(span);
            }

            long elapsed = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (outcome.ApprovalWaitMs <= 0) outcome.ApprovalWaitMs = relay.WaitMs;
            if (outcome.DurationMs <= 0 && outcome.Executed()) outcome.DurationMs = Math.Max(0, elapsed - outcome.ApprovalWaitMs);
            if (String.IsNullOrEmpty(outcome.ApprovalId)) outcome.ApprovalId = relay.ApprovalId;
            if (String.IsNullOrEmpty(outcome.ApprovalSummary)) outcome.ApprovalSummary = relay.ApprovalSummary;
            if (String.IsNullOrEmpty(outcome.Resource)) outcome.Resource = call.Resource;
            AgentTelemetry.RecordStage("tool", Math.Max(0, elapsed - outcome.ApprovalWaitMs) / 1000.0);
            await PublishCompletedAsync(state, call, outcome, token).ConfigureAwait(false);
            return outcome;
        }

        private static async Task PublishCompletedAsync(AgentTurnState state, AgentToolCall call, AgentToolExecution outcome, CancellationToken token)
        {
            await state.Publisher.PublishAsync(new ToolCompletedEvent
            {
                CallId = call.CallId,
                ToolName = call.ToolName,
                Resource = outcome.Resource ?? call.Resource,
                Effect = outcome.Effect,
                Decision = outcome.Decision,
                DurationMs = outcome.DurationMs,
                IsError = outcome.Result.IsError,
                ErrorCode = outcome.Result.ErrorCode,
                Truncated = outcome.Result.Truncated,
                OutputPreview = Preview(outcome.Result.Text, state.Settings.OutputPreviewChars),
                ApprovalId = outcome.ApprovalId,
                Iteration = call.Iteration
            }, token).ConfigureAwait(false);
        }

        private static AgentToolCallRecord Record(AgentTurnState state, AgentToolCall call, AgentToolExecution outcome, bool parallel)
        {
            return new AgentToolCallRecord
            {
                CallId = call.CallId,
                ToolName = call.ToolName,
                Resource = outcome.Resource ?? call.Resource,
                ArgumentsJson = call.ArgumentsJson,
                Effect = outcome.Effect,
                Decision = outcome.Decision,
                ApprovalId = outcome.ApprovalId,
                DurationMs = outcome.DurationMs,
                ApprovalWaitMs = outcome.ApprovalWaitMs,
                IsError = outcome.Result.IsError,
                ErrorCode = outcome.Result.ErrorCode,
                Truncated = outcome.Result.Truncated,
                OutputPreview = Preview(outcome.Result.Text, state.Settings.OutputPreviewChars),
                Iteration = call.Iteration,
                Parallel = parallel
            };
        }

        private static async Task FailAsync(AgentTurnState state, string code, string message)
        {
            state.Result.FinishReason = AgentFinishReasonEnum.Error;
            state.Result.ErrorCode = code;
            state.Result.ErrorMessage = message;
            await state.Publisher.PublishAsync(new AgentErrorEvent { Code = code, Message = message, Fatal = true, Iteration = state.Result.Iterations }, CancellationToken.None).ConfigureAwait(false);
        }

        private async Task FinishAsync(AgentTurnState state)
        {
            AgentTurnResult result = state.Result;
            CloseDanglingToolCalls(state);
            if (state.Thinking.Length > 0) result.Thinking = state.Thinking.ToString();

            if (state.UserMessagePersisted && result.Messages.Count > 1)
            {
                long start = Stopwatch.GetTimestamp();
                try
                {
                    await _Store.AppendMessagesAsync(state.Request.ConversationId, result.Messages.Skip(1).ToList(), CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    await state.Publisher.PublishAsync(new AgentErrorEvent { Code = "persist_failed", Message = "The turn's messages could not be saved: " + e.Message, Fatal = false, Iteration = result.Iterations }, CancellationToken.None).ConfigureAwait(false);
                }
                Stage("persist", start);
            }

            result.TimeToFirstTokenMs = state.TimeToFirstTokenMs();
            result.DurationMs = state.ElapsedMs();
            AgentMessage? last = result.Messages.LastOrDefault();
            await state.Publisher.PublishAsync(new TurnCompletedEvent
            {
                FinishReason = result.FinishReason,
                FinalText = result.FinalText,
                AssistantMessageId = last != null && last.Role == AgentMessageRoleEnum.Assistant && last.ToolCalls.Count == 0 ? last.Id : null,
                TokensIn = result.TokensIn,
                TokensOut = result.TokensOut,
                TimeToFirstTokenMs = result.TimeToFirstTokenMs,
                DurationMs = result.DurationMs,
                Iterations = result.Iterations,
                ToolCallCount = result.ToolCalls.Count,
                UsedFallback = result.UsedFallback,
                Compacted = result.Compacted
            }, CancellationToken.None).ConfigureAwait(false);
            AgentTelemetry.RecordTurn(state.Request.Surface, ReasonLabel(result.FinishReason), result.DurationMs / 1000.0, result.Iterations);
        }

        private void CloseDanglingToolCalls(AgentTurnState state)
        {
            List<AgentMessage> messages = state.Result.Messages;
            HashSet<string> answered = new HashSet<string>(messages.Where(m => m.Role == AgentMessageRoleEnum.Tool && m.ToolCallId != null).Select(m => m.ToolCallId!), StringComparer.Ordinal);
            List<AgentMessage> closing = new List<AgentMessage>();
            foreach (AgentMessage m in messages.Where(m => m.Role == AgentMessageRoleEnum.Assistant))
            {
                foreach (AgentMessageToolCall c in m.ToolCalls.Where(c => !answered.Contains(c.Id)))
                {
                    AgentMessage tool = NewMessage(state, AgentMessageRoleEnum.Tool, ToolOutputWrapper.Wrap(ResourceArgument.Read(c.ArgumentsJson), c.Name, ToolResult.Error("cancelled", "The turn ended before this tool call completed.")));
                    tool.ToolCallId = c.Id;
                    tool.ToolName = c.Name;
                    closing.Add(tool);
                }
            }
            messages.AddRange(closing);
        }

        private AgentMessage NewMessage(AgentTurnState state, AgentMessageRoleEnum role, string content)
        {
            return new AgentMessage
            {
                Id = _IdFactory("msg_"),
                ConversationId = state.Request.ConversationId,
                TurnId = state.Request.TurnId,
                Role = role,
                Content = content ?? String.Empty,
                CreatedUtc = DateTime.UtcNow
            };
        }

        private static ToolDefinition ToToolDefinition(AgentToolDefinition tool)
        {
            Dictionary<string, object>? parameters = null;
            try
            {
                parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(tool.InputSchemaJson);
            }
            catch (JsonException)
            {
                parameters = null;
            }
            if (parameters == null)
            {
                parameters = new Dictionary<string, object>
                {
                    { "type", "object" },
                    { "properties", new Dictionary<string, object>() }
                };
            }
            return ToolDefinition.Function(tool.Name, tool.Description, parameters);
        }

        private static string Preview(string text, int max)
        {
            if (String.IsNullOrEmpty(text) || max <= 0) return String.Empty;
            return text.Length <= max ? text : text.Substring(0, max) + "...";
        }

        private static string ReasonLabel(AgentFinishReasonEnum reason)
        {
            switch (reason)
            {
                case AgentFinishReasonEnum.Completed: return "completed";
                case AgentFinishReasonEnum.MaxIterations: return "max_iterations";
                case AgentFinishReasonEnum.ToolCallLimit: return "tool_call_limit";
                case AgentFinishReasonEnum.ApprovalPending: return "approval_pending";
                case AgentFinishReasonEnum.Cancelled: return "cancelled";
                default: return "error";
            }
        }

        private static void Stage(string stage, long startTimestamp)
        {
            AgentTelemetry.RecordStage(stage, Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds);
        }

        #endregion
    }
}
