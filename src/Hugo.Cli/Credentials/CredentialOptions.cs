namespace Hugo.Cli.Credentials
{
    /// <summary>
    /// Connection and credential values given on the command line. Null means the flag was not given.
    /// </summary>
    public class CredentialOptions
    {
        /// <summary>
        /// <c>--server</c>, or null.
        /// </summary>
        public string? Server { get; set; } = null;

        /// <summary>
        /// <c>--profile</c>, or null.
        /// </summary>
        public string? Profile { get; set; } = null;

        /// <summary>
        /// <c>--token</c>, or null.
        /// </summary>
        public string? Token { get; set; } = null;

        /// <summary>
        /// <c>--access-key</c>, or null.
        /// </summary>
        public string? AccessKey { get; set; } = null;

        /// <summary>
        /// <c>--secret-key</c>, or null.
        /// </summary>
        public string? SecretKey { get; set; } = null;

        /// <summary>
        /// <c>--tenant</c>, or null.
        /// </summary>
        public string? Tenant { get; set; } = null;

        /// <summary>
        /// <c>--credentials-file</c>, or null.
        /// </summary>
        public string? CredentialsFile { get; set; } = null;
    }
}
