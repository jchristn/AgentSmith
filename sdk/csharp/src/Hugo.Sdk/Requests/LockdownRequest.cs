namespace Hugo.Sdk.Requests
{
    /// <summary>
    /// Turn tenant lockdown on (preview).
    /// </summary>
    public class LockdownRequest
    {
        #region Public-Members

        /// <summary>
        /// Reason.
        /// </summary>
        public string? Reason { get; set; } = null;

        #endregion
    }
}
