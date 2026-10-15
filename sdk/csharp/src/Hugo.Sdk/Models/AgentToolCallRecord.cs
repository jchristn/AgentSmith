namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>
    /// A tool call made during an agent turn, with its policy decision and outcome.
    /// </summary>
    public class AgentToolCallRecord
    {
        #region Public-Members

        /// <summary>
        /// Call identifier.
        /// </summary>
        public string CallId { get; set; } = String.Empty;

        /// <summary>
        /// Tool name.
        /// </summary>
        public string ToolName { get; set; } = String.Empty;

        /// <summary>
        /// Resource alias.
        /// </summary>
        public string? Resource { get; set; } = null;

        /// <summary>
        /// Arguments as JSON text.
        /// </summary>
        public string ArgumentsJson { get; set; } = String.Empty;

        /// <summary>
        /// Read, Write, or Destructive.
        /// </summary>
        public string Effect { get; set; } = String.Empty;

        /// <summary>
        /// Allowed, Denied, ApprovalRequired, Approved, Rejected, or Expired.
        /// </summary>
        public string Decision { get; set; } = String.Empty;

        /// <summary>
        /// Approval identifier, when one was created.
        /// </summary>
        public string? ApprovalId { get; set; } = null;

        /// <summary>
        /// Duration, milliseconds.
        /// </summary>
        public long DurationMs { get; set; } = 0;

        /// <summary>
        /// Time spent waiting for approval, milliseconds.
        /// </summary>
        public long ApprovalWaitMs { get; set; } = 0;

        /// <summary>
        /// Whether the call failed.
        /// </summary>
        public bool IsError { get; set; } = false;

        /// <summary>
        /// Error code.
        /// </summary>
        public string? ErrorCode { get; set; } = null;

        /// <summary>
        /// Whether the output was truncated.
        /// </summary>
        public bool Truncated { get; set; } = false;

        /// <summary>
        /// Output preview.
        /// </summary>
        public string OutputPreview { get; set; } = String.Empty;

        /// <summary>
        /// Loop iteration that made the call.
        /// </summary>
        public int Iteration { get; set; } = 0;

        /// <summary>
        /// Whether the call ran in parallel with others.
        /// </summary>
        public bool Parallel { get; set; } = false;

        #endregion
    }
}
