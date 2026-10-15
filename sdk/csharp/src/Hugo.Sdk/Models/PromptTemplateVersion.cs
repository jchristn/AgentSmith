namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>
    /// One stored version of a prompt template.
    /// </summary>
    public class PromptTemplateVersion
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
        /// Template Identifier.
        /// </summary>
        public string TemplateId { get; set; } = String.Empty;

        /// <summary>
        /// Version.
        /// </summary>
        public int Version { get; set; } = 0;

        /// <summary>
        /// Content.
        /// </summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>
        /// Change note.
        /// </summary>
        public string? ChangeNote { get; set; } = null;

        /// <summary>
        /// Created by.
        /// </summary>
        public string? CreatedBy { get; set; } = null;

        /// <summary>
        /// Whether the record is active.
        /// </summary>
        public bool Active { get; set; } = false;

        /// <summary>
        /// Whether the record is protected from deletion.
        /// </summary>
        public bool IsProtected { get; set; } = false;

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
