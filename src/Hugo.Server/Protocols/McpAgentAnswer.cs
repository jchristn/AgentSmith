namespace Hugo.Server.Protocols
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Result of ask_agent_smith.
    /// </summary>
    public class McpAgentAnswer
    {
        /// <summary>
        /// completed, approval_pending, or failed.
        /// </summary>
        public string Status { get; set; } = "completed";

        /// <summary>
        /// Final answer.
        /// </summary>
        public string Answer { get; set; } = String.Empty;

        /// <summary>
        /// Conversation to pass back to continue.
        /// </summary>
        public string? ConversationId { get; set; } = null;

        /// <summary>
        /// Approval the turn waits for, or null.
        /// </summary>
        public string? ApprovalId { get; set; } = null;

        /// <summary>
        /// What to do next, when the turn waits for an approval.
        /// </summary>
        public string? Next { get; set; } = null;

        /// <summary>
        /// Compact tool trace.
        /// </summary>
        public List<string> ToolTrace { get; set; } = new List<string>();
    }
}
