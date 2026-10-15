namespace Hugo.Core.Security
{
    using System;
    using Hugo.Core.Enums;

    /// <summary>
    /// Contents of an opaque session token. Carries only platform-controlled identifiers.
    /// </summary>
    public class TokenPayload
    {
        /// <summary>Non-session capability purpose. Null means a session token.</summary>
        public string? Purpose { get; set; } = null;

        /// <summary>
        /// Session identifier.
        /// </summary>
        public string SessionId { get; set; } = String.Empty;

        /// <summary>
        /// Random token identifier stored on the session row.
        /// </summary>
        public string TokenId { get; set; } = String.Empty;

        /// <summary>
        /// Principal type.
        /// </summary>
        public PrincipalTypeEnum PrincipalType { get; set; } = PrincipalTypeEnum.User;

        /// <summary>
        /// Tenant identifier, or null for system administrator sessions.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// User identifier for user sessions.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Credential identifier for credential sessions.
        /// </summary>
        public string? CredentialId { get; set; } = null;

        /// <summary>
        /// Time after which the session can no longer be refreshed (UTC); carried from the first sign-in through every
        /// refresh. Null for tokens issued before this field existed.
        /// </summary>
        public DateTime? AbsoluteExpiresUtc { get; set; } = null;

        /// <summary>
        /// Issuer.
        /// </summary>
        public string Issuer { get; set; } = String.Empty;

        /// <summary>
        /// Issued (UTC).
        /// </summary>
        public DateTime IssuedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Expires (UTC).
        /// </summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow;
    }
}
