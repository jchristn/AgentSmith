namespace Hugo.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;

    /// <summary>
    /// User create or update request. Omitted fields are left unchanged on update.
    /// </summary>
    public class UserRequest
    {
        /// <summary>
        /// Email, unique within the tenant. Required on create.
        /// </summary>
        public string? Email { get; set; } = null;

        /// <summary>
        /// Password. Required on create; supplying it on update changes the password.
        /// </summary>
        public string? Password { get; set; } = null;

        /// <summary>
        /// First name.
        /// </summary>
        public string? FirstName { get; set; } = null;

        /// <summary>
        /// Last name.
        /// </summary>
        public string? LastName { get; set; } = null;

        /// <summary>
        /// Whether the user is a tenant administrator.
        /// </summary>
        public bool? IsTenantAdmin { get; set; } = null;

        /// <summary>
        /// Whether the user is a system administrator. Only system administrators may set it.
        /// </summary>
        public bool? IsAdmin { get; set; } = null;

        /// <summary>
        /// Whether the user is active.
        /// </summary>
        public bool? Active { get; set; } = null;

        /// <summary>
        /// Whether the user must change the password at next sign-in, or null. Defaults to true when an administrator
        /// sets another user's password.
        /// </summary>
        public bool? MustChangePassword { get; set; } = null;

        /// <summary>
        /// Preferred dashboard locale, or null to leave unchanged.
        /// </summary>
        public string? PreferredLocale { get; set; } = null;

        /// <summary>
        /// Default agent profile identifier, or null to leave unchanged.
        /// </summary>
        public string? DefaultAgentProfileId { get; set; } = null;
    }
}
