namespace Hugo.Core.Responses
{
    using System;
    using Hugo.Core.Enums;

    /// <summary>
    /// Issued session token.
    /// </summary>
    public class TokenResponse
    {
        /// <summary>
        /// Opaque token for Authorization: Bearer or x-token.
        /// </summary>
        public string Token { get; set; } = String.Empty;

        /// <summary>
        /// Expiry (UTC).
        /// </summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Tenant the token is bound to.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Session identifier.
        /// </summary>
        public string SessionId { get; set; } = String.Empty;

        /// <summary>
        /// Principal type.
        /// </summary>
        public PrincipalTypeEnum PrincipalType { get; set; } = PrincipalTypeEnum.User;

        /// <summary>
        /// Whether the user must change the password before using other routes (POST /v1.0/api/users/me/password).
        /// </summary>
        public bool MustChangePassword { get; set; } = false;
    }
}
