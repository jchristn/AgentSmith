namespace Hugo.Sdk.Models
{
    /// <summary>
    /// A profile binding that uses a resource (preview).
    /// </summary>
    public class ResourceReferrer
    {
        #region Public-Members

        /// <summary>
        /// Agent profile Identifier.
        /// </summary>
        public string? AgentProfileId { get; set; } = null;

        /// <summary>
        /// Agent profile name.
        /// </summary>
        public string? AgentProfileName { get; set; } = null;

        /// <summary>
        /// Binding Identifier.
        /// </summary>
        public string? BindingId { get; set; } = null;

        /// <summary>
        /// Alias.
        /// </summary>
        public string? Alias { get; set; } = null;

        /// <summary>
        /// Access mode.
        /// </summary>
        public string? AccessMode { get; set; } = null;

        /// <summary>
        /// Enabled.
        /// </summary>
        public bool Enabled { get; set; } = false;

        #endregion
    }
}
