namespace Hugo.Cli.Credentials
{
    /// <summary>
    /// Where the credentials of a command came from. Sources are tried in this order: flags, environment, login cache.
    /// </summary>
    public enum CredentialSourceEnum
    {
        /// <summary>
        /// No credentials were found.
        /// </summary>
        None = 0,

        /// <summary>
        /// Command line flags (<c>--token</c>, or <c>--access-key</c> with <c>--secret-key</c>).
        /// </summary>
        Flags = 1,

        /// <summary>
        /// Environment variables (<c>HUGO_TOKEN</c>, or <c>HUGO_ACCESS_KEY</c> with <c>HUGO_SECRET_KEY</c>).
        /// </summary>
        Environment = 2,

        /// <summary>
        /// The login cache written by <c>hugo login</c>.
        /// </summary>
        Cache = 3
    }
}
