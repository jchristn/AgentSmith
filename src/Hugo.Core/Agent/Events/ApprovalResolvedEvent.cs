namespace Hugo.Core.Agent.Events
{
    using System;

    /// <summary>
    /// <c>approval.resolved</c>: the approval was approved, rejected, or expired.
    /// </summary>
    public class ApprovalResolvedEvent : AgentEvent
    {
        /// <summary>
        /// Event name.
        /// </summary>
        public override string EventName
        {
            get
            {
                return "approval.resolved";
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
        /// Outcome: Approved, Rejected, or Expired.
        /// </summary>
        public AgentToolDecisionEnum Decision { get; set; } = AgentToolDecisionEnum.Approved;

        /// <summary>
        /// Who decided, when known.
        /// </summary>
        public string? DecidedBy { get; set; } = null;

        /// <summary>
        /// Milliseconds the call waited.
        /// </summary>
        public long WaitMs { get; set; } = 0;
    }
}
