namespace Hugo.Cli.Credentials
{
    /// <summary>
    /// Environment variables the CLI reads. Each one is used only when the matching flag is not given.
    /// </summary>
    public static class CliEnvironmentVariables
    {
        /// <summary>
        /// Server base URL, for example <c>http://127.0.0.1:8800</c>.
        /// </summary>
        public const string Server = "HUGO_SERVER";

        /// <summary>
        /// Session token.
        /// </summary>
        public const string Token = "HUGO_TOKEN";

        /// <summary>
        /// Access key (used with <see cref="SecretKey"/>).
        /// </summary>
        public const string AccessKey = "HUGO_ACCESS_KEY";

        /// <summary>
        /// Secret key (used with <see cref="AccessKey"/>).
        /// </summary>
        public const string SecretKey = "HUGO_SECRET_KEY";

        /// <summary>
        /// Tenant ID.
        /// </summary>
        public const string Tenant = "HUGO_TENANT";

        /// <summary>
        /// Agent profile name for <c>hugo mcp stdio</c>.
        /// </summary>
        public const string Profile = "HUGO_PROFILE";

        /// <summary>
        /// Path of the credential file (default <c>~/.hugo/credentials.json</c>).
        /// </summary>
        public const string CredentialsFile = "HUGO_CREDENTIALS_FILE";
    }
}
