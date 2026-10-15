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
        ApprovalRequired = 2
    }
}
