namespace Hugo.Sdk.Responses
{
    using System;
    using Hugo.Sdk.Enums;
    using Hugo.Sdk.Models;

    /// <summary>
    /// Description of the authenticated principal.
    /// </summary>
    public class TokenDetails
    {
        #region Public-Members

        /// <summary>
        /// Kind of principal.
        /// </summary>
        public PrincipalTypeEnum PrincipalType { get; set; } = PrincipalTypeEnum.User;

        /// <summary>
        /// Principal Identifier.
        /// </summary>
        public string? PrincipalId { get; set; } = null;

        /// <summary>
        /// Principal name.
        /// </summary>
        public string? PrincipalName { get; set; } = null;

        /// <summary>
        /// Tenant Identifier.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// User Identifier.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Credential Identifier.
        /// </summary>
        public string? CredentialId { get; set; } = null;

        /// <summary>
        /// Must change password.
        /// </summary>
        public bool MustChangePassword { get; set; } = false;

        /// <summary>
        /// Reauthenticated UTC.
        /// </summary>
        public DateTime? ReauthenticatedUtc { get; set; } = null;

        /// <summary>
        /// Session Identifier.
        /// </summary>
        public string? SessionId { get; set; } = null;

        /// <summary>
        /// Auth scheme.
        /// </summary>
        public AuthSchemeEnum? AuthScheme { get; set; } = null;

        /// <summary>
        /// Is admin.
        /// </summary>
        public bool IsAdmin { get; set; } = false;

        /// <summary>
        /// Is tenant admin.
        /// </summary>
        public bool IsTenantAdmin { get; set; } = false;

        /// <summary>
        /// Email.
        /// </summary>
        public string? Email { get; set; } = null;

        /// <summary>
        /// First name.
        /// </summary>
        public string? FirstName { get; set; } = null;

        /// <summary>
        /// Last name.
        /// </summary>
        public string? LastName { get; set; } = null;

        /// <summary>
        /// Expires UTC.
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;

        #endregion
    }
}
