namespace Hugo.Core.Agent.Events
{
    using System;

    /// <summary>
    /// <c>error</c>: something went wrong. A fatal error ends the turn (a <c>turn.completed</c> with finish reason
    /// Error follows); a non-fatal one (for example compaction or tool retrieval failing) is informational.
    /// </summary>
    public class AgentErrorEvent : AgentEvent
    {
        /// <summary>
        /// Event name.
        /// </summary>
        public override string EventName
        {
            get
            {
                return "error";
            }
        }

        /// <summary>
        /// Stable error code (for example <c>model_unavailable</c>, <c>compaction_failed</c>).
        /// </summary>
        public string Code { get; set; } = String.Empty;

        /// <summary>
        /// Message without secrets.
        /// </summary>
        public string Message { get; set; } = String.Empty;

        /// <summary>
        /// Whether the turn ends because of this error.
        /// </summary>
        public bool Fatal { get; set; } = false;

        /// <summary>
        /// Iteration (1-based), or 0 outside the loop.
        /// </summary>
        public int Iteration { get; set; } = 0;
    }
}
