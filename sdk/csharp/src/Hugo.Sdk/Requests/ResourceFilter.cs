namespace Hugo.Sdk.Requests
{
    using System;

    /// <summary>
    /// Resource list filter.
    /// </summary>
    public class ResourceFilter
    {
        #region Public-Members

        /// <summary>
        /// Connector type, for example ssh.
        /// </summary>
        public string? ConnectorType { get; set; } = null;

        /// <summary>
        /// Environment label, for example production.
        /// </summary>
        public string? Environment { get; set; } = null;

        /// <summary>
        /// Healthy, Unhealthy, or Unknown.
        /// </summary>
        public string? Health { get; set; } = null;

        #endregion
    }
}
