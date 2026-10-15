namespace Hugo.Sdk.Enums
{
    /// <summary>
    /// Authentication scheme a request or session used.
    /// </summary>
    public enum AuthSchemeEnum
    {
        /// <summary>
        /// Bearer token.
        /// </summary>
        BearerToken,

        /// <summary>
        /// X token.
        /// </summary>
        XToken,

        /// <summary>
        /// Password headers.
        /// </summary>
        PasswordHeaders,

        /// <summary>
        /// Access key secret.
        /// </summary>
        AccessKeySecret,

        /// <summary>
        /// Access key signature.
        /// </summary>
        AccessKeySignature,

        /// <summary>
        /// Admin API key.
        /// </summary>
        AdminApiKey
    }
}
