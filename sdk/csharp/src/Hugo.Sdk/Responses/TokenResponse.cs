namespace Hugo.Sdk.Responses
{
    using System;
    using Hugo.Sdk.Enums;
    using Hugo.Sdk.Models;

    /// <summary>
    /// A session token issued by POST /v1.0/token or /v1.0/token/refresh.
    /// </summary>
    public class TokenResponse
    {
        #region Public-Members

        /// <summary>
        /// Bearer token.
        /// </summary>
        public string Token { get; set; } = String.Empty;

        /// <summary>
        /// Expiry, UTC.
        /// </summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Tenant Identifier.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Session Identifier.
        /// </summary>
        public string? SessionId { get; set; } = null;

        /// <summary>
        /// Kind of principal.
        /// </summary>
        public PrincipalTypeEnum PrincipalType { get; set; } = PrincipalTypeEnum.User;

        /// <summary>
        /// Whether the user must change their password before using other routes.
        /// </summary>
        public bool MustChangePassword { get; set; } = false;

        #endregion
    }
}
