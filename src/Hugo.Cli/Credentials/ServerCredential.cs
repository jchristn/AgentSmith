namespace Hugo.Cli.Credentials
{
    using System;

    /// <summary>
    /// The cached sign-in for one server in <c>~/.hugo/credentials.json</c>. Passwords are never stored. An access key
    /// and secret key are stored only when the operator signed in with them (and did not pass <c>--no-store-key</c>),
    /// so the stdio relay can renew an expired session.
    /// </summary>
    public class ServerCredential
    {
        /// <summary>
        /// Normalized server URL this entry belongs to.
        /// </summary>
        public string Server { get; set; } = String.Empty;

        /// <summary>
        /// Session token, or null.
        /// </summary>
        public string? Token { get; set; } = null;

        /// <summary>
        /// When the session token expires (UTC), or null when unknown.
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;

        /// <summary>
        /// Tenant ID, or null.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Principal type reported at sign-in, or null.
        /// </summary>
        public string? PrincipalType { get; set; } = null;

        /// <summary>
        /// Email used to sign in, or null for access key sign-ins.
        /// </summary>
        public string? Email { get; set; } = null;

        /// <summary>
        /// Access key, or null.
        /// </summary>
        public string? AccessKey { get; set; } = null;

        /// <summary>
        /// Secret key paired with <see cref="AccessKey"/>, or null.
        /// </summary>
        public string? SecretKey { get; set; } = null;

        /// <summary>
        /// When this entry was written (UTC).
        /// </summary>
        public DateTime SavedUtc { get; set; } = DateTime.UtcNow;
    }
}
