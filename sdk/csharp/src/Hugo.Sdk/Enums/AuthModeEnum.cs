namespace Hugo.Sdk.Enums
{
    /// <summary>
    /// How the SDK authenticates each request.
    /// </summary>
    public enum AuthModeEnum
    {
        /// <summary>
        /// No authentication headers are sent.
        /// </summary>
        None,

        /// <summary>
        /// Bearer session token (Authorization header).
        /// </summary>
        Token,

        /// <summary>
        /// Credential access key and secret key sent as x-access-key and x-secret-key headers.
        /// </summary>
        AccessKey,

        /// <summary>
        /// Credential access key with an HMAC-SHA256 request signature (x-date, x-nonce, x-signature).
        /// </summary>
        SignedRequest,

        /// <summary>
        /// System administrator API key sent as x-api-key.
        /// </summary>
        AdminApiKey
    }
}
