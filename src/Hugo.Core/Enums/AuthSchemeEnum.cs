namespace Hugo.Core.Enums
{
    /// <summary>
    /// Authentication schemes.
    /// </summary>
    public enum AuthSchemeEnum
    {
        /// <summary>
        /// Authorization: Bearer session token.
        /// </summary>
        BearerToken,

        /// <summary>
        /// x-token session token.
        /// </summary>
        XToken,

        /// <summary>
        /// Email and password (token route only).
        /// </summary>
        PasswordHeaders,

        /// <summary>
        /// Direct access key and secret key headers.
        /// </summary>
        AccessKeySecret,

        /// <summary>
        /// Signed request with an access key.
        /// </summary>
        AccessKeySignature,

        /// <summary>
        /// System administrator API key (x-api-key).
        /// </summary>
        AdminApiKey
    }
}
