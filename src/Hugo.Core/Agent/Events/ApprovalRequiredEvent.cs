namespace Hugo.Core.Agent.Events
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>approval.required</c>: a tool call waits for a human decision; carries the approval card data.
    /// </summary>
    public class ApprovalRequiredEvent : AgentEvent
    {
        /// <summary>
        /// Event name.
        /// </summary>
        public override string EventName
        {
            get
            {
                return "approval.required";
            }
        }

        /// <summary>
        /// Call ID.
        /// </summary>
        public string CallId { get; set; } = String.Empty;

        /// <summary>
        /// Approval ID.
        /// </summary>
        public string ApprovalId { get; set; } = String.Empty;

        /// <summary>
        /// Tool name.
        /// </summary>
        public string ToolName { get; set; } = String.Empty;

        /// <summary>
        /// Resource alias.
        /// </summary>
        public string? Resource { get; set; } = null;

        /// <summary>
        /// Classified effect.
        /// </summary>
        public EffectEnum Effect { get; set; } = EffectEnum.Write;

        /// <summary>
        /// Human summary.
        /// </summary>
        public string Summary { get; set; } = String.Empty;

        /// <summary>
        /// Classifier or policy reason.
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// Redacted arguments JSON.
        /// </summary>
        public string? RedactedArgumentsJson { get; set; } = null;

        /// <summary>
        /// Expiry (UTC), when known.
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;
    }
}
