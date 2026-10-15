namespace Hugo.Core.Agent
{
    using System;
    using System.Diagnostics;
    using System.Text;
    using System.Threading;
    using Hugo.Core.Inference;

    /// <summary>
    /// Mutable state of one running turn, shared by the steps of <see cref="AgentRuntime"/>. Token counters and the
    /// time to first token are updated atomically; everything else is touched by the turn's own flow only.
    /// </summary>
    public class AgentTurnState
    {
        #region Public-Members

        /// <summary>
        /// The request.
        /// </summary>
        public AgentTurnRequest Request { get; }

        /// <summary>
        /// Effective settings.
        /// </summary>
        public AgentRuntimeSettings Settings { get; }

        /// <summary>
        /// Primary inference endpoint.
        /// </summary>
        public ModelEndpointConfig Endpoint { get; }

        /// <summary>
        /// The result being built.
        /// </summary>
        public AgentTurnResult Result { get; }

        /// <summary>
        /// Event publisher.
        /// </summary>
        public AgentEventPublisher Publisher { get; }

        /// <summary>
        /// Reasoning captured across the turn.
        /// </summary>
        public StringBuilder Thinking { get; } = new StringBuilder();

        /// <summary>
        /// Stopwatch timestamp of the turn start.
        /// </summary>
        public long StartTimestamp { get; } = Stopwatch.GetTimestamp();

        /// <summary>
        /// Whether the user message was persisted (later messages are persisted only then).
        /// </summary>
        public bool UserMessagePersisted { get; set; } = false;

        #endregion

        #region Private-Members

        private long _Ttft = -1;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="settings">Settings.</param>
        /// <param name="endpoint">Primary endpoint.</param>
        /// <param name="publisher">Publisher.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public AgentTurnState(AgentTurnRequest request, AgentRuntimeSettings settings, ModelEndpointConfig endpoint, AgentEventPublisher publisher)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            Publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
            Result = new AgentTurnResult
            {
                TurnId = request.TurnId,
                ConversationId = request.ConversationId,
                Model = endpoint.Model
            };
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Record the first streamed token (only the first call has an effect).
        /// </summary>
        public void MarkFirstToken()
        {
            long elapsed = (long)Stopwatch.GetElapsedTime(StartTimestamp).TotalMilliseconds;
            Interlocked.CompareExchange(ref _Ttft, elapsed, -1);
        }

        /// <summary>
        /// Milliseconds from turn start to the first token, or -1.
        /// </summary>
        /// <returns>Milliseconds.</returns>
        public long TimeToFirstTokenMs()
        {
            return Interlocked.Read(ref _Ttft);
        }

        /// <summary>
        /// Milliseconds since the turn started.
        /// </summary>
        /// <returns>Milliseconds.</returns>
        public long ElapsedMs()
        {
            return (long)Stopwatch.GetElapsedTime(StartTimestamp).TotalMilliseconds;
        }

        /// <summary>
        /// Add model usage to the turn totals.
        /// </summary>
        /// <param name="model">Model call result.</param>
        public void AddUsage(ModelChatResult model)
        {
            if (model == null) return;
            lock (Result)
            {
                Result.TokensIn += model.InputTokens;
                Result.TokensOut += model.OutputTokens;
                if (model.TokensEstimated) Result.TokensEstimated = true;
                if (model.UsedFallback) Result.UsedFallback = true;
                if (!String.IsNullOrEmpty(model.Model)) Result.Model = model.Model;
            }
        }

        #endregion
    }
}
