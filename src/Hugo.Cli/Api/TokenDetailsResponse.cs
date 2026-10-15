namespace Hugo.Cli.Api
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Body of <c>GET /v1.0/token/details</c>: who the presented token belongs to.
    /// </summary>
    public class TokenDetailsResponse
    {
        /// <summary>
        /// Principal type (<c>User</c>, <c>Credential</c>, <c>Administrator</c>, ...), or null.
        /// </summary>
        [JsonConverter(typeof(LenientStringConverter))]
        public string? PrincipalType { get; set; } = null;

        /// <summary>
        /// Principal ID, or null.
        /// </summary>
        public string? PrincipalId { get; set; } = null;

        /// <summary>
        /// Display name of the principal, or null.
        /// </summary>
        public string? PrincipalName { get; set; } = null;

        /// <summary>
        /// Tenant ID, or null.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// User ID, or null.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Credential ID, or null.
        /// </summary>
        public string? CredentialId { get; set; } = null;

        /// <summary>
        /// Session ID, or null.
        /// </summary>
        public string? SessionId { get; set; } = null;

        /// <summary>
        /// Authentication scheme of the request, or null.
        /// </summary>
        [JsonConverter(typeof(LenientStringConverter))]
        public string? AuthScheme { get; set; } = null;

        /// <summary>
        /// Whether the principal is a system administrator.
        /// </summary>
        public bool IsAdmin { get; set; } = false;

        /// <summary>
        /// Whether the principal is a tenant administrator.
        /// </summary>
        public bool IsTenantAdmin { get; set; } = false;

        /// <summary>
        /// Whether the user must change the password.
        /// </summary>
        public bool MustChangePassword { get; set; } = false;

        /// <summary>
        /// Email of the user, or null.
        /// </summary>
        public string? Email { get; set; } = null;

        /// <summary>
        /// First name of the user, or null.
        /// </summary>
        public string? FirstName { get; set; } = null;

        /// <summary>
        /// Last name of the user, or null.
        /// </summary>
        public string? LastName { get; set; } = null;

        /// <summary>
        /// When the token expires (UTC), or null.
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;
    }
}
