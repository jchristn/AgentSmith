namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>
    /// A knowledge entry stored for a resource.
    /// </summary>
    public class ResourceKnowledge
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Tenant Identifier.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Resource Identifier.
        /// </summary>
        public string? ResourceId { get; set; } = null;

        /// <summary>
        /// Scope.
        /// </summary>
        public string Scope { get; set; } = String.Empty;

        /// <summary>
        /// Entry key; _root for the scope root.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Content.
        /// </summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>
        /// Operator, Generated, Agent, or Derived.
        /// </summary>
        public string? Source { get; set; } = null;

        /// <summary>
        /// Updated by.
        /// </summary>
        public string? UpdatedBy { get; set; } = null;

        /// <summary>
        /// Version.
        /// </summary>
        public int Version { get; set; } = 0;

        /// <summary>
        /// Creation timestamp, UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update timestamp, UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
