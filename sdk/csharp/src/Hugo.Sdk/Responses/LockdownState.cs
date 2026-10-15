namespace Hugo.Sdk.Responses
{
    using Hugo.Sdk.Models;

    /// <summary>
    /// Tenant lockdown state (preview).
    /// </summary>
    public class LockdownState
    {
        #region Public-Members

        /// <summary>
        /// Tenant Identifier.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Lockdown.
        /// </summary>
        public bool Lockdown { get; set; } = false;

        /// <summary>
        /// Approvals cancelled.
        /// </summary>
        public int ApprovalsCancelled { get; set; } = 0;

        #endregion
    }
}
