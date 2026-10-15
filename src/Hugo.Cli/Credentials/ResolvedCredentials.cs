namespace Hugo.Cli.Credentials
{
    using System;

    /// <summary>
    /// The server, profile, and credentials a command uses after applying precedence.
    /// </summary>
    public class ResolvedCredentials
    {
        /// <summary>
        /// Normalized server URL. Never empty.
        /// </summary>
        public string Server { get; set; } = ServerAddress.Default;

        /// <summary>
        /// Agent profile name, or null for the server's default resolution.
        /// </summary>
        public string? Profile { get; set; } = null;

        /// <summary>
        /// Session token, or null.
        /// </summary>
        public string? Token { get; set; } = null;

        /// <summary>
        /// Access key, or null.
        /// </summary>
        public string? AccessKey { get; set; } = null;

        /// <summary>
        /// Secret key, or null.
        /// </summary>
        public string? SecretKey { get; set; } = null;

        /// <summary>
        /// Tenant ID, or null.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Where the credentials came from.
        /// </summary>
        public CredentialSourceEnum Source { get; set; } = CredentialSourceEnum.None;

        /// <summary>
        /// The credential store consulted (renewed tokens are written back to it when <see cref="Source"/> is
        /// <see cref="CredentialSourceEnum.Cache"/>).
        /// </summary>
        public CredentialStore? Store { get; set; } = null;

        /// <summary>
        /// Whether an access key and secret key are available (a session can be created or renewed).
        /// </summary>
        public bool HasKeys
        {
            get
            {
                return !String.IsNullOrEmpty(AccessKey) && !String.IsNullOrEmpty(SecretKey);
            }
        }

        /// <summary>
        /// Whether any credential is available.
        /// </summary>
        public bool HasCredentials
        {
            get
            {
                return !String.IsNullOrEmpty(Token) || HasKeys;
            }
        }
    }
}
