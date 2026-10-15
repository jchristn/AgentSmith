namespace Hugo.Core.Agent
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Compact record of one tool call in a turn, for the turn result, the <c>ask_agent_smith</c> trace, and
    /// analytics.
    /// </summary>
    public class AgentToolCallRecord
    {
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
        /// Arguments JSON as the model produced it.
        /// </summary>
        public string ArgumentsJson { get; set; } = "{}";

        /// <summary>
        /// Classified effect.
        /// </summary>
        public EffectEnum Effect { get; set; } = EffectEnum.Read;

        /// <summary>
        /// Decision.
        /// </summary>
        public AgentToolDecisionEnum Decision { get; set; } = AgentToolDecisionEnum.Allowed;

        /// <summary>
        /// Approval ID, when one was involved.
        /// </summary>
        public string? ApprovalId { get; set; } = null;

        /// <summary>
        /// Execution duration in milliseconds.
        /// </summary>
        public long DurationMs { get; set; } = 0;

        /// <summary>
        /// Approval wait in milliseconds.
        /// </summary>
        public long ApprovalWaitMs { get; set; } = 0;

        /// <summary>
        /// Whether the result was an error.
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
        /// First characters of the output.
        /// </summary>
        public string OutputPreview { get; set; } = String.Empty;

        /// <summary>
        /// Iteration (1-based).
        /// </summary>
        public int Iteration { get; set; } = 1;

        /// <summary>
        /// Whether the call ran in parallel with other Read calls.
        /// </summary>
        public bool Parallel { get; set; } = false;
    }
}
