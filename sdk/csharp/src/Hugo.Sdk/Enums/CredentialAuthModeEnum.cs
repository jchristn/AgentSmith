namespace Hugo.Sdk.Enums
{
    /// <summary>
    /// How a credential may authenticate.
    /// </summary>
    public enum CredentialAuthModeEnum
    {
        /// <summary>
        /// Access key and secret key headers on every request.
        /// </summary>
        DirectHeader,

        /// <summary>
        /// Signed requests only.
        /// </summary>
        SignedRequest,

        /// <summary>
        /// Exchange the keys for a session token.
        /// </summary>
        SessionExchange,

        /// <summary>
        /// Any of the above.
        /// </summary>
        Hybrid
    }
}
