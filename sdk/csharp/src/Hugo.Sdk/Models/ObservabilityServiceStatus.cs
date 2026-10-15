namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>
    /// Reachability of one configured observability service.
    /// </summary>
    public class ObservabilityServiceStatus
    {
        #region Public-Members

        /// <summary>
        /// Service.
        /// </summary>
        public string Service { get; set; } = String.Empty;

        /// <summary>
        /// Configured.
        /// </summary>
        public bool Configured { get; set; } = false;

        /// <summary>
        /// Reachable.
        /// </summary>
        public bool Reachable { get; set; } = false;

        /// <summary>
        /// Checked UTC.
        /// </summary>
        public DateTime? CheckedUtc { get; set; } = null;

        #endregion
    }
}
