namespace Hugo.Cli.Api
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Body of <c>POST /v1.0/token</c> and <c>POST /v1.0/token/refresh</c>.
    /// </summary>
    public class TokenResponse
    {
        /// <summary>
        /// The session token (sent as <c>Authorization: Bearer</c>). Empty when the server sent none.
        /// </summary>
        public string Token { get; set; } = String.Empty;

        /// <summary>
        /// When the token expires (UTC), or null when the server did not say.
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;

        /// <summary>
        /// Tenant of the principal, or null.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Session ID, or null.
        /// </summary>
        public string? SessionId { get; set; } = null;

        /// <summary>
        /// Principal type (<c>User</c>, <c>Credential</c>, ...), or null.
        /// </summary>
        [JsonConverter(typeof(LenientStringConverter))]
        public string? PrincipalType { get; set; } = null;

        /// <summary>
        /// Whether the user still has a seeded or reset password and is asked to change it. Advisory: no route is blocked (D-048).
        /// </summary>
        public bool MustChangePassword { get; set; } = false;
    }
}
