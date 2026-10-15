namespace Hugo.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;

    /// <summary>
    /// Authenticated principal details.
    /// </summary>
    public class TokenDetailsResponse
    {
        /// <summary>
        /// Principal type.
        /// </summary>
        public PrincipalTypeEnum PrincipalType { get; set; } = PrincipalTypeEnum.User;

        /// <summary>
        /// Principal identifier.
        /// </summary>
        public string? PrincipalId { get; set; } = null;

        /// <summary>
        /// Principal display name.
        /// </summary>
        public string? PrincipalName { get; set; } = null;

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// User identifier.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Credential identifier.
        /// </summary>
        public string? CredentialId { get; set; } = null;

        /// <summary>
        /// Whether the user must change the password before using other routes.
        /// </summary>
        public bool MustChangePassword { get; set; } = false;

        /// <summary>
        /// Last re-authentication of the session (UTC), or null.
        /// </summary>
        public DateTime? ReauthenticatedUtc { get; set; } = null;

        /// <summary>
        /// Session identifier.
        /// </summary>
        public string? SessionId { get; set; } = null;

        /// <summary>
        /// Authentication scheme.
        /// </summary>
        public AuthSchemeEnum? AuthScheme { get; set; } = null;

        /// <summary>
        /// Whether the principal is a system administrator.
        /// </summary>
        public bool IsAdmin { get; set; } = false;

        /// <summary>
        /// Whether the principal is a tenant administrator.
        /// </summary>
        public bool IsTenantAdmin { get; set; } = false;

        /// <summary>
        /// User email.
        /// </summary>
        public string? Email { get; set; } = null;

        /// <summary>
        /// User first name.
        /// </summary>
        public string? FirstName { get; set; } = null;

        /// <summary>
        /// User last name.
        /// </summary>
        public string? LastName { get; set; } = null;

        /// <summary>
        /// Token expiry (UTC).
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;
    }
}
