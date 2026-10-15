namespace Hugo.Connectors.Observability
{
    using System;

    /// <summary>
    /// A cached OAuth 2.0 access token and when it must be refreshed. Immutable and thread-safe.
    /// </summary>
    public class OAuth2CachedToken
    {
        #region Public-Members

        /// <summary>
        /// Access token.
        /// </summary>
        public string AccessToken { get; }

        /// <summary>
        /// UTC time after which the token is refreshed (the server's expiry minus the refresh margin).
        /// </summary>
        public DateTimeOffset RefreshAfter { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="accessToken">Access token.</param>
        /// <param name="refreshAfter">Refresh time.</param>
        /// <exception cref="ArgumentNullException">Thrown when accessToken is null or empty.</exception>
        public OAuth2CachedToken(string accessToken, DateTimeOffset refreshAfter)
        {
            if (String.IsNullOrEmpty(accessToken)) throw new ArgumentNullException(nameof(accessToken));
            AccessToken = accessToken;
            RefreshAfter = refreshAfter;
        }

        #endregion
    }
}
