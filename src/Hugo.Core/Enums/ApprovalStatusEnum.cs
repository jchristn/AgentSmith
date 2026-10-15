namespace Hugo.Core.Enums
{
    /// <summary>
    /// State of an approval.
    /// </summary>
    public enum ApprovalStatusEnum
    {
        /// <summary>
        /// Waiting for a human decision.
        /// </summary>
        Pending = 0,

        /// <summary>
        /// Approved; the call runs.
        /// </summary>
        Approved = 1,

        /// <summary>
        /// Rejected; the call does not run.
        /// </summary>
        Rejected = 2,

        /// <summary>
        /// No decision before the expiry time; the call does not run.
        /// </summary>
        Expired = 3,

        /// <summary>
        /// Cancelled by the requester, an administrator, or tenant lockdown.
        /// </summary>
        Cancelled = 4
    }
}
