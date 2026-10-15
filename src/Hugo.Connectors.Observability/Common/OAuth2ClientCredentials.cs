namespace Hugo.Connectors.Observability
{
    using System;

    /// <summary>
    /// An OAuth 2.0 client ID and secret read from the <c>clientCredentials</c> slot for one call. Never log or return
    /// these values. Immutable and thread-safe.
    /// </summary>
    public class OAuth2ClientCredentials
    {
        #region Public-Members

        /// <summary>
        /// Client ID.
        /// </summary>
        public string ClientId { get; }

        /// <summary>
        /// Client secret.
        /// </summary>
        public string ClientSecret { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="clientId">Client ID.</param>
        /// <param name="clientSecret">Client secret.</param>
        /// <exception cref="ArgumentNullException">Thrown when either value is null or empty.</exception>
        public OAuth2ClientCredentials(string clientId, string clientSecret)
        {
            if (String.IsNullOrEmpty(clientId)) throw new ArgumentNullException(nameof(clientId));
            if (String.IsNullOrEmpty(clientSecret)) throw new ArgumentNullException(nameof(clientSecret));
            ClientId = clientId;
            ClientSecret = clientSecret;
        }

        #endregion
    }
}
