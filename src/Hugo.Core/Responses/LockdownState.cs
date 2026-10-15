namespace Hugo.Core.Responses
{
    using System;

    /// <summary>
    /// Tenant lockdown state (plan section 11.6). Not thread-safe.
    /// </summary>
    public class LockdownState
    {
        #region Public-Members

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string TenantId
        {
            get
            {
                return _TenantId;
            }
            set
            {
                _TenantId = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Whether lockdown is on.
        /// </summary>
        public bool Lockdown { get; set; } = false;

        /// <summary>
        /// Pending approvals cancelled when lockdown was turned on.
        /// </summary>
        public int ApprovalsCancelled { get; set; } = 0;

        #endregion

        #region Private-Members

        private string _TenantId = String.Empty;

        #endregion
    }
}
