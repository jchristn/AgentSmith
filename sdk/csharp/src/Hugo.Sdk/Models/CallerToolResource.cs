namespace Hugo.Sdk.Models
{
    /// <summary>
    /// A resource a caller-visible tool can target.
    /// </summary>
    public class CallerToolResource
    {
        #region Public-Members

        /// <summary>
        /// Alias.
        /// </summary>
        public string? Alias { get; set; } = null;

        /// <summary>
        /// Connector type.
        /// </summary>
        public string? ConnectorType { get; set; } = null;

        /// <summary>
        /// Access mode.
        /// </summary>
        public string? AccessMode { get; set; } = null;

        /// <summary>
        /// Rule.
        /// </summary>
        public string? Rule { get; set; } = null;

        #endregion
    }
}
