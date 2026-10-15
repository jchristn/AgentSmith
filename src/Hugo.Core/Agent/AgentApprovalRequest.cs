namespace Hugo.Core.Agent
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// A pending approval for a tool call, as shown on the dashboard approval card (plan 11.4).
    /// </summary>
    public class AgentApprovalRequest
    {
        /// <summary>
        /// Approval ID (<c>apr_...</c>).
        /// </summary>
        public string ApprovalId { get; set; } = String.Empty;

        /// <summary>
        /// One-line human summary of what will happen.
        /// </summary>
        public string Summary { get; set; } = String.Empty;

        /// <summary>
        /// Classified effect.
        /// </summary>
        public EffectEnum Effect { get; set; } = EffectEnum.Write;

        /// <summary>
        /// Classifier or policy reason.
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// Resource alias.
        /// </summary>
        public string? Resource { get; set; } = null;

        /// <summary>
        /// Tool name.
        /// </summary>
        public string ToolName { get; set; } = String.Empty;

        /// <summary>
        /// Arguments with secret-looking values redacted, as a JSON object string.
        /// </summary>
        public string? RedactedArgumentsJson { get; set; } = null;

        /// <summary>
        /// When the approval expires (UTC), if known.
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;
    }
}
