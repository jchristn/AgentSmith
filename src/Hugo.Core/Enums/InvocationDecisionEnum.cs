namespace Hugo.Core.Enums
{
    /// <summary>
    /// Policy decision for one tool call.
    /// </summary>
    public enum InvocationDecisionEnum
    {
        /// <summary>
        /// The call may run now.
        /// </summary>
        Allowed = 0,

        /// <summary>
        /// The call must not run.
        /// </summary>
        Denied = 1,

        /// <summary>
        /// The call may run only after a human approves it.
        /// </summary>
        ApprovalRequired = 2,

        /// <summary>
        /// Approval was required and a human approved the call (recorded on tool invocations).
        /// </summary>
        Approved = 3,

        /// <summary>
        /// Approval was required and a human rejected the call (recorded on tool invocations).
        /// </summary>
        Rejected = 4,

        /// <summary>
        /// Approval was required and expired before a decision (recorded on tool invocations).
        /// </summary>
        Expired = 5,

        /// <summary>
        /// Approval was required and was cancelled (recorded on tool invocations).
        /// </summary>
        Cancelled = 6
    }
}
