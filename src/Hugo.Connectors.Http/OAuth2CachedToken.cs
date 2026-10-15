namespace Hugo.Connectors.Http
{
    using System;

    /// <summary>
    /// An access token held by <see cref="OAuth2TokenCache"/>. Immutable and thread-safe.
    /// </summary>
    public class OAuth2CachedToken
    {
        /// <summary>
        /// Access token. Secret: never log or return it.
        /// </summary>
        public string AccessToken { get; }

        /// <summary>
        /// Token type used in the Authorization header (normally <c>Bearer</c>).
        /// </summary>
        public string TokenType { get; }

        /// <summary>
        /// UTC time the token expires.
        /// </summary>
        public DateTime ExpiresUtc { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="accessToken">Access token.</param>
        /// <param name="tokenType">Token type. Null or empty means Bearer.</param>
        /// <param name="expiresUtc">Expiry in UTC.</param>
        /// <exception cref="ArgumentNullException">Thrown when accessToken is null or empty.</exception>
        public OAuth2CachedToken(string accessToken, string? tokenType, DateTime expiresUtc)
        {
            if (String.IsNullOrEmpty(accessToken)) throw new ArgumentNullException(nameof(accessToken));
            AccessToken = accessToken;
            TokenType = String.IsNullOrWhiteSpace(tokenType) || String.Equals(tokenType, "bearer", StringComparison.OrdinalIgnoreCase) ? "Bearer" : tokenType.Trim();
            ExpiresUtc = expiresUtc;
        }
    }
}
