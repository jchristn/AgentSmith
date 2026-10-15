namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>
    /// A tenant: the isolation boundary for users, credentials, secrets, and Hugo configuration.
    /// </summary>
    public class Tenant
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Tenant name (unique).
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Parent tenant identifier, if any.
        /// </summary>
        public string? ParentTenantId { get; set; } = null;

        /// <summary>
        /// Region.
        /// </summary>
        public string? Region { get; set; } = null;

        /// <summary>
        /// Default agent profile identifier.
        /// </summary>
        public string? DefaultAgentProfileId { get; set; } = null;

        /// <summary>
        /// Whether tenant lockdown is on (every non-Read tool call is denied).
        /// </summary>
        public bool Lockdown { get; set; } = false;

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
