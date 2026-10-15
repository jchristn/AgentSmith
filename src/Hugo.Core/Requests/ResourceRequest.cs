namespace Hugo.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;

    /// <summary>
    /// Create or update a resource. On update, null fields keep their value and an empty string clears an optional text
    /// field; Settings and SecretBindings, when present, replace the stored ones.
    /// </summary>
    public class ResourceRequest
    {
        #region Public-Members

        /// <summary>
        /// Name, unique per tenant (lowercase letters, digits, hyphens, underscores; 1 to 64 characters). Required on create.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Connector type (for example sqlite). Required on create; cannot change on update.
        /// </summary>
        public string? ConnectorType { get; set; } = null;

        /// <summary>
        /// Display name.
        /// </summary>
        public string? DisplayName { get; set; } = null;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Endpoint summary; derived from the settings when empty.
        /// </summary>
        public string? Endpoint { get; set; } = null;

        /// <summary>
        /// Environment tag.
        /// </summary>
        public string? Environment { get; set; } = null;

        /// <summary>
        /// Operator notes included in compiled instructions.
        /// </summary>
        public string? Notes { get; set; } = null;

        /// <summary>
        /// Private or Tenant. Default Tenant on create.
        /// </summary>
        public VisibilityEnum? Visibility { get; set; } = null;

        /// <summary>
        /// Highest access mode any binding may use.
        /// </summary>
        public AccessModeEnum? DefaultAccessModeCeiling { get; set; } = null;

        /// <summary>
        /// Remove the ceiling (update only).
        /// </summary>
        public bool ClearAccessModeCeiling { get; set; } = false;

        /// <summary>
        /// Whether the resource is active.
        /// </summary>
        public bool? Active { get; set; } = null;

        /// <summary>
        /// Settings by key. Strings, numbers, and booleans are accepted; arrays become string lists.
        /// </summary>
        public Dictionary<string, JsonElement>? Settings { get; set; } = null;

        /// <summary>
        /// Secret slot bindings (slotName, secretId, optional secretFieldName).
        /// </summary>
        public List<ResourceSecretBinding>? SecretBindings { get; set; } = null;

        #endregion
    }
}
