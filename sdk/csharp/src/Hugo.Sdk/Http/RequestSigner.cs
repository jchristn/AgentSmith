namespace Hugo.Sdk.Http
{
    using System;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;

    /// <summary>
    /// Signed request support matching the server's RequestSigner. The canonical string is
    /// METHOD\nPATH\nQUERY\nTENANT\nDATE\nNONCE\nSHA256HEX(BODY) and the signature is lowercase hex HMAC-SHA256 keyed
    /// with the credential's secret key. Thread-safe.
    /// </summary>
    public static class RequestSigner
    {
        #region Public-Methods

        /// <summary>
        /// Build the canonical string.
        /// </summary>
        /// <param name="method">HTTP method (upper-cased here).</param>
        /// <param name="path">Raw path without the query string.</param>
        /// <param name="query">Raw query string exactly as sent, without the leading question mark, or empty.</param>
        /// <param name="tenantId">Tenant identifier of the credential.</param>
        /// <param name="date">x-date value, ISO 8601 UTC (yyyy-MM-ddTHH:mm:ssZ).</param>
        /// <param name="nonce">x-nonce value.</param>
        /// <param name="body">Body bytes, or null.</param>
        /// <returns>Canonical string.</returns>
        public static string Canonicalize(string method, string path, string? query, string? tenantId, string date, string nonce, byte[]? body)
        {
            string bodyHash = Convert.ToHexString(SHA256.HashData(body ?? Array.Empty<byte>())).ToLowerInvariant();
            return (method ?? String.Empty).ToUpperInvariant() + "\n" + (path ?? String.Empty) + "\n" + (query ?? String.Empty) + "\n" +
                   (tenantId ?? String.Empty) + "\n" + (date ?? String.Empty) + "\n" + (nonce ?? String.Empty) + "\n" + bodyHash;
        }

        /// <summary>
        /// Sign a canonical string.
        /// </summary>
        /// <param name="secretKey">Credential secret key.</param>
        /// <param name="canonical">Canonical string.</param>
        /// <returns>Lowercase hex signature.</returns>
        /// <exception cref="ArgumentNullException">Thrown when secretKey is null or empty.</exception>
        public static string Sign(string secretKey, string canonical)
        {
            if (String.IsNullOrEmpty(secretKey)) throw new ArgumentNullException(nameof(secretKey));
            return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secretKey), Encoding.UTF8.GetBytes(canonical ?? String.Empty))).ToLowerInvariant();
        }

        /// <summary>
        /// Format a timestamp for the x-date header.
        /// </summary>
        /// <param name="utc">Timestamp, UTC.</param>
        /// <returns>yyyy-MM-ddTHH:mm:ssZ.</returns>
        public static string FormatDate(DateTime utc)
        {
            return utc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Generate a random nonce (32 lowercase hex characters).
        /// </summary>
        /// <returns>Nonce.</returns>
        public static string NewNonce()
        {
            return Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        }

        #endregion
    }
}
