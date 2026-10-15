namespace Hugo.Sdk.Models
{
    using System;
    using Hugo.Sdk.Enums;

    /// <summary>
    /// An authentication session (a token issued to a user or credential).
    /// </summary>
    public class AuthSession
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
        /// User Identifier.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Credential Identifier.
        /// </summary>
        public string? CredentialId { get; set; } = null;

        /// <summary>
        /// Kind of principal.
        /// </summary>
        public PrincipalTypeEnum PrincipalType { get; set; } = PrincipalTypeEnum.User;

        /// <summary>
        /// Display name of the principal: the user's email, the credential's name, or the administrator.
        /// </summary>
        public string? PrincipalName { get; set; } = null;

        /// <summary>
        /// Scheme used to obtain the session.
        /// </summary>
        public AuthSchemeEnum? AuthScheme { get; set; } = null;

        /// <summary>
        /// Source IP address.
        /// </summary>
        public string? SourceIp { get; set; } = null;

        /// <summary>
        /// User agent.
        /// </summary>
        public string? UserAgent { get; set; } = null;

        /// <summary>
        /// Expires UTC.
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;

        /// <summary>
        /// Last used UTC.
        /// </summary>
        public DateTime? LastUsedUtc { get; set; } = null;

        /// <summary>
        /// Revocation timestamp, UTC; null while valid.
        /// </summary>
        public DateTime? RevokedUtc { get; set; } = null;

        /// <summary>
        /// Revocation reason.
        /// </summary>
        public string? RevocationReason { get; set; } = null;

        /// <summary>
        /// Last re-authentication on this session, UTC.
        /// </summary>
        public DateTime? ReauthenticatedUtc { get; set; } = null;

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
