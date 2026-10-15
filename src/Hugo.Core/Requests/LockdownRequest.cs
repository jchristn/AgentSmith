namespace Hugo.Core.Requests
{
    using System;

    /// <summary>
    /// Turn tenant lockdown on.
    /// </summary>
    public class LockdownRequest
    {
        #region Public-Members

        /// <summary>
        /// Why (recorded in audit).
        /// </summary>
        public string? Reason { get; set; } = null;

        #endregion
    }
}
