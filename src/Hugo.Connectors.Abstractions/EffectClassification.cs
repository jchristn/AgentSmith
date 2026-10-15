namespace Hugo.Connectors.Abstractions
{
    using System;

    /// <summary>
    /// The classified effect of one call, with the reason and the text shown on an approval card.
    /// </summary>
    public class EffectClassification
    {
        /// <summary>
        /// Effect.
        /// </summary>
        public EffectEnum Effect { get; set; } = EffectEnum.Read;

        /// <summary>
        /// Short reason (for example <c>statement type DELETE</c>, <c>command matches destructive pattern rm</c>).
        /// </summary>
        public string Reason { get; set; } = String.Empty;

        /// <summary>
        /// Human-readable summary of the action for approval cards (for example
        /// <c>Run "systemctl restart nginx" on web-01</c>). Must not contain secrets.
        /// </summary>
        public string ApprovalSummary { get; set; } = String.Empty;

        /// <summary>
        /// When true the call is rejected regardless of access mode (for example a command matching a deny pattern).
        /// </summary>
        public bool Rejected { get; set; } = false;

        /// <summary>
        /// When true the call always requires approval, even if the binding opted out of destructive approvals
        /// (for example terminating cloud instances).
        /// </summary>
        public bool AlwaysRequireApproval { get; set; } = false;

        /// <summary>
        /// Instantiate an empty classification (Read).
        /// </summary>
        public EffectClassification()
        {
        }

        /// <summary>
        /// Instantiate with effect, reason, and approval summary.
        /// </summary>
        /// <param name="effect">Effect.</param>
        /// <param name="reason">Reason.</param>
        /// <param name="approvalSummary">Approval card summary.</param>
        public EffectClassification(EffectEnum effect, string reason, string approvalSummary)
        {
            Effect = effect;
            Reason = reason ?? String.Empty;
            ApprovalSummary = approvalSummary ?? String.Empty;
        }

        /// <summary>
        /// Build a rejected classification.
        /// </summary>
        /// <param name="reason">Reason.</param>
        /// <returns>Classification.</returns>
        public static EffectClassification Reject(string reason)
        {
            EffectClassification c = new EffectClassification(EffectEnum.Destructive, reason, String.Empty);
            c.Rejected = true;
            return c;
        }
    }
}
