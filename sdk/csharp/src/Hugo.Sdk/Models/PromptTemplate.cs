namespace Hugo.Sdk.Models
{
    using System;
    using System.Collections.Generic;
    using Hugo.Sdk.Enums;

    /// <summary>
    /// A prompt template (tenant-owned, or built-in with a null tenant).
    /// </summary>
    public class PromptTemplate
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
        /// Name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Kind of template.
        /// </summary>
        public PromptTemplateKindEnum Kind { get; set; } = PromptTemplateKindEnum.Other;

        /// <summary>
        /// Template content.
        /// </summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>
        /// Current version.
        /// </summary>
        public int CurrentVersion { get; set; } = 0;

        /// <summary>
        /// Is built in.
        /// </summary>
        public bool IsBuiltIn { get; set; } = false;

        /// <summary>
        /// Key of the shipped default this template derives from.
        /// </summary>
        public string? BuiltInKey { get; set; } = null;

        /// <summary>
        /// Variables.
        /// </summary>
        public List<PromptTemplateVariable> Variables { get; set; } = new List<PromptTemplateVariable>();

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
