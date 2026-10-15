namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>
    /// A user within a tenant.
    /// </summary>
    public class User
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Tenant Identifier.
        /// </summary>
        public string TenantId { get; set; } = String.Empty;

        /// <summary>
        /// First name.
        /// </summary>
        public string? FirstName { get; set; } = null;

        /// <summary>
        /// Last name.
        /// </summary>
        public string? LastName { get; set; } = null;

        /// <summary>
        /// Email address (unique per tenant).
        /// </summary>
        public string Email { get; set; } = String.Empty;

        /// <summary>
        /// Whether the user is a system administrator.
        /// </summary>
        public bool IsAdmin { get; set; } = false;

        /// <summary>
        /// Whether the user administers the tenant.
        /// </summary>
        public bool IsTenantAdmin { get; set; } = false;

        /// <summary>
        /// Whether the user must change their password before using other routes.
        /// </summary>
        public bool MustChangePassword { get; set; } = false;

        /// <summary>
        /// Default agent profile Identifier.
        /// </summary>
        public string? DefaultAgentProfileId { get; set; } = null;

        /// <summary>
        /// Preferred locale, for example en or de.
        /// </summary>
        public string? PreferredLocale { get; set; } = null;

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
