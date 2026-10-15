namespace Hugo.Sdk.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A configured resource: one service Hugo can reach, such as an SSH host or a database.
    /// </summary>
    public class Resource
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
        /// Owner user Identifier.
        /// </summary>
        public string? OwnerUserId { get; set; } = null;

        /// <summary>
        /// Private or Tenant.
        /// </summary>
        public string? Visibility { get; set; } = null;

        /// <summary>
        /// Name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Connector type, for example ssh or postgresql.
        /// </summary>
        public string ConnectorType { get; set; } = String.Empty;

        /// <summary>
        /// Display name.
        /// </summary>
        public string? DisplayName { get; set; } = null;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Endpoint.
        /// </summary>
        public string? Endpoint { get; set; } = null;

        /// <summary>
        /// Environment.
        /// </summary>
        public string? Environment { get; set; } = null;

        /// <summary>
        /// Notes.
        /// </summary>
        public string? Notes { get; set; } = null;

        /// <summary>
        /// ReadOnly, ReadWriteWithApproval, or ReadWrite.
        /// </summary>
        public string? DefaultAccessModeCeiling { get; set; } = null;

        /// <summary>
        /// Last test UTC.
        /// </summary>
        public DateTime? LastTestUtc { get; set; } = null;

        /// <summary>
        /// Last test result.
        /// </summary>
        public bool? LastTestResult { get; set; } = null;

        /// <summary>
        /// Last test message.
        /// </summary>
        public string? LastTestMessage { get; set; } = null;

        /// <summary>
        /// Settings.
        /// </summary>
        public List<ResourceSetting> Settings { get; set; } = new List<ResourceSetting>();

        /// <summary>
        /// Secret bindings.
        /// </summary>
        public List<ResourceSecretBinding> SecretBindings { get; set; } = new List<ResourceSecretBinding>();

        /// <summary>
        /// Whether the record is active.
        /// </summary>
        public bool Active { get; set; } = false;

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
