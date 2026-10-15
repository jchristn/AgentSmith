namespace Hugo.Core.Models
{
    using System;
    using Hugo.Core.Enums;

    /// <summary>
    /// One tool invocation, reduced to the fields analytics needs (P6.18). Not thread-safe.
    /// </summary>
    public class ToolUsageRow
    {
        /// <summary>
        /// Creation time (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Tool name.
        /// </summary>
        public string ToolName { get; set; } = String.Empty;

        /// <summary>
        /// Connector type, when the call reached a resource.
        /// </summary>
        public string? ConnectorType { get; set; } = null;

        /// <summary>
        /// Decision.
        /// </summary>
        public InvocationDecisionEnum Decision { get; set; } = InvocationDecisionEnum.Allowed;

        /// <summary>
        /// Status.
        /// </summary>
        public InvocationStatusEnum Status { get; set; } = InvocationStatusEnum.Succeeded;

        /// <summary>
        /// Execution duration in milliseconds.
        /// </summary>
        public long DurationMs { get; set; } = 0;

        /// <summary>
        /// Whether the call went through an approval.
        /// </summary>
        public bool HadApproval { get; set; } = false;
    }
}
