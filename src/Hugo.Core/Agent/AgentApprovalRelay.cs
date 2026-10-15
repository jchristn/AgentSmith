namespace Hugo.Core.Agent
{
    using System;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Agent.Events;

    /// <summary>
    /// The runtime's <see cref="IAgentApprovalListener"/> for one tool call: publishes <c>approval.required</c> and
    /// <c>approval.resolved</c>, and measures the approval wait (stage <c>approval_wait</c>). Thread-safe.
    /// </summary>
    public class AgentApprovalRelay : IAgentApprovalListener
    {
        #region Public-Members

        /// <summary>
        /// Total milliseconds spent waiting for approvals of this call.
        /// </summary>
        public long WaitMs
        {
            get
            {
                return Interlocked.Read(ref _WaitMs);
            }
        }

        /// <summary>
        /// The last approval ID seen, if any.
        /// </summary>
        public string? ApprovalId { get; private set; } = null;

        /// <summary>
        /// The last approval summary seen, if any.
        /// </summary>
        public string? ApprovalSummary { get; private set; } = null;

        #endregion

        #region Private-Members

        private readonly AgentEventPublisher _Publisher;
        private readonly AgentToolCall _Call;
        private long _WaitStart = 0;
        private long _WaitMs = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="publisher">Event publisher.</param>
        /// <param name="call">The tool call.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public AgentApprovalRelay(AgentEventPublisher publisher, AgentToolCall call)
        {
            _Publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
            _Call = call ?? throw new ArgumentNullException(nameof(call));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task ApprovalRequiredAsync(AgentApprovalRequest approval, CancellationToken token)
        {
            if (approval == null) return;
            ApprovalId = approval.ApprovalId;
            ApprovalSummary = approval.Summary;
            Interlocked.Exchange(ref _WaitStart, Stopwatch.GetTimestamp());
            await _Publisher.PublishAsync(new ApprovalRequiredEvent
            {
                CallId = _Call.CallId,
                ApprovalId = approval.ApprovalId,
                ToolName = String.IsNullOrEmpty(approval.ToolName) ? _Call.ToolName : approval.ToolName,
                Resource = approval.Resource ?? _Call.Resource,
                Effect = approval.Effect,
                Summary = approval.Summary,
                Reason = approval.Reason,
                RedactedArgumentsJson = approval.RedactedArgumentsJson,
                ExpiresUtc = approval.ExpiresUtc
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task ApprovalResolvedAsync(AgentApprovalResolution resolution, CancellationToken token)
        {
            if (resolution == null) return;
            long started = Interlocked.Exchange(ref _WaitStart, 0);
            long waited = started > 0 ? (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds : 0;
            Interlocked.Add(ref _WaitMs, waited);
            AgentTelemetry.RecordStage("approval_wait", waited / 1000.0);
            await _Publisher.PublishAsync(new ApprovalResolvedEvent
            {
                CallId = _Call.CallId,
                ApprovalId = String.IsNullOrEmpty(resolution.ApprovalId) ? (ApprovalId ?? String.Empty) : resolution.ApprovalId,
                Decision = resolution.Decision,
                DecidedBy = resolution.DecidedBy,
                WaitMs = waited
            }, token).ConfigureAwait(false);
        }

        #endregion
    }
}
