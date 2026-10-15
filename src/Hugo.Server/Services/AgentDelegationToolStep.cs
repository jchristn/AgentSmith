namespace Hugo.Server.Services
{
    using System;

    /// <summary>
    /// One entry of a delegated turn's compact tool trace. Not thread-safe.
    /// </summary>
    public class AgentDelegationToolStep
    {
        /// <summary>
        /// Tool name.
        /// </summary>
        public string ToolName { get; set; } = String.Empty;

        /// <summary>
        /// Resource alias the call targeted, when any.
        /// </summary>
        public string? Resource { get; set; } = null;

        /// <summary>
        /// Classified effect (Read, Write, Destructive).
        /// </summary>
        public string Effect { get; set; } = "Read";

        /// <summary>
        /// Decision (Allowed, Denied, ApprovalRequired, Approved, Rejected, Expired).
        /// </summary>
        public string Decision { get; set; } = "Allowed";

        /// <summary>
        /// Whether the call returned an error.
        /// </summary>
        public bool IsError { get; set; } = false;

        /// <summary>
        /// Error code, when the call failed.
        /// </summary>
        public string? ErrorCode { get; set; } = null;

        /// <summary>
        /// Approval identifier, when the call needed one.
        /// </summary>
        public string? ApprovalId { get; set; } = null;

        /// <summary>
        /// Execution time in milliseconds (approval waits excluded).
        /// </summary>
        public long DurationMs { get; set; } = 0;
    }
}
