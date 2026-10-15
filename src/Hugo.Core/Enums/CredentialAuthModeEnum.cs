namespace Hugo.Core.Enums
{
    /// <summary>
    /// How a credential authenticates.
    /// </summary>
    public enum CredentialAuthModeEnum
    {
        /// <summary>
        /// DirectHeader.
        /// </summary>
        DirectHeader,

        /// <summary>
        /// SignedRequest.
        /// </summary>
        SignedRequest,

        /// <summary>
        /// SessionExchange.
        /// </summary>
        SessionExchange,

        /// <summary>
        /// Hybrid.
        /// </summary>
        Hybrid
    }
}
