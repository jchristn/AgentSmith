namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>
    /// One version of a resource knowledge entry (preview).
    /// </summary>
    public class ResourceKnowledgeVersion
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
        /// Knowledge Identifier.
        /// </summary>
        public string? KnowledgeId { get; set; } = null;

        /// <summary>
        /// Resource Identifier.
        /// </summary>
        public string? ResourceId { get; set; } = null;

        /// <summary>
        /// Scope.
        /// </summary>
        public string? Scope { get; set; } = null;

        /// <summary>
        /// Key.
        /// </summary>
        public string? Key { get; set; } = null;

        /// <summary>
        /// Version.
        /// </summary>
        public int Version { get; set; } = 0;

        /// <summary>
        /// Content.
        /// </summary>
        public string? Content { get; set; } = null;

        /// <summary>
        /// Source.
        /// </summary>
        public string? Source { get; set; } = null;

        /// <summary>
        /// Updated by.
        /// </summary>
        public string? UpdatedBy { get; set; } = null;

        /// <summary>
        /// Deleted.
        /// </summary>
        public bool Deleted { get; set; } = false;

        /// <summary>
        /// Creation timestamp, UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
