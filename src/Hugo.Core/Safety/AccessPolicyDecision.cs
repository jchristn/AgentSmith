namespace Hugo.Core.Safety
{
    using System;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Enums;

    /// <summary>
    /// Result of the access policy for one tool call.
    /// </summary>
    public class AccessPolicyDecision
    {
        /// <summary>
        /// Decision.
        /// </summary>
        public InvocationDecisionEnum Decision { get; set; } = InvocationDecisionEnum.Denied;

        /// <summary>
        /// Effective access mode after applying the resource ceiling.
        /// </summary>
        public AccessModeEnum EffectiveAccessMode { get; set; } = AccessModeEnum.ReadOnly;

        /// <summary>
        /// Stable reason code (for example <c>readonly_binding</c>, <c>tool_denied</c>, <c>approval_required</c>).
        /// </summary>
        public string ReasonCode { get; set; } = String.Empty;

        /// <summary>
        /// English explanation suitable for the calling LLM and the audit trail.
        /// </summary>
        public string Reason { get; set; } = String.Empty;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="decision">Decision.</param>
        /// <param name="mode">Effective access mode.</param>
        /// <param name="code">Reason code.</param>
        /// <param name="reason">Explanation.</param>
        public AccessPolicyDecision(InvocationDecisionEnum decision, AccessModeEnum mode, string code, string reason)
        {
            Decision = decision;
            EffectiveAccessMode = mode;
            ReasonCode = code ?? String.Empty;
            Reason = reason ?? String.Empty;
        }
    }
}
