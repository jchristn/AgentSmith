namespace Hugo.Core.Agent.Events
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>tool.completed</c>: the call finished (executed, denied, rejected, expired, pending, or failed).
    /// </summary>
    public class ToolCompletedEvent : AgentEvent
    {
        /// <summary>
        /// Event name.
        /// </summary>
        public override string EventName
        {
            get
            {
                return "tool.completed";
            }
        }

        /// <summary>
        /// Call ID.
        /// </summary>
        public string CallId { get; set; } = String.Empty;

        /// <summary>
        /// Tool name.
        /// </summary>
        public string ToolName { get; set; } = String.Empty;

        /// <summary>
        /// Resource alias, when known.
        /// </summary>
        public string? Resource { get; set; } = null;

        /// <summary>
        /// Classified effect.
        /// </summary>
        public EffectEnum Effect { get; set; } = EffectEnum.Read;

        /// <summary>
        /// Decision.
        /// </summary>
        public AgentToolDecisionEnum Decision { get; set; } = AgentToolDecisionEnum.Allowed;

        /// <summary>
        /// Execution duration in milliseconds (excluding approval wait).
        /// </summary>
        public long DurationMs { get; set; } = 0;

        /// <summary>
        /// Whether the result is an error.
        /// </summary>
        public bool IsError { get; set; } = false;

        /// <summary>
        /// Error code, when an error.
        /// </summary>
        public string? ErrorCode { get; set; } = null;

        /// <summary>
        /// Whether the output was truncated.
        /// </summary>
        public bool Truncated { get; set; } = false;

        /// <summary>
        /// First characters of the (scrubbed) output.
        /// </summary>
        public string OutputPreview { get; set; } = String.Empty;

        /// <summary>
        /// Approval ID, when an approval was involved.
        /// </summary>
        public string? ApprovalId { get; set; } = null;

        /// <summary>
        /// Iteration (1-based).
        /// </summary>
        public int Iteration { get; set; } = 1;
    }
}
