namespace Hugo.Core.Security
{
    using System;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;

    /// <summary>
    /// AWS-style request signing for credential authentication. The canonical string is
    /// METHOD\nPATH\nQUERY\nTENANT\nDATE\nNONCE\nSHA256(BODY) and the signature is lowercase hex HMAC-SHA256 with
    /// the secret key. Thread-safe.
    /// </summary>
    public static class RequestSigner
    {
        /// <summary>
        /// Build the canonical string.
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Path without query.</param>
        /// <param name="query">Raw query string without the leading question mark, or empty.</param>
        /// <param name="tenantId">Tenant identifier, or empty.</param>
        /// <param name="date">x-date value (ISO 8601 UTC).</param>
        /// <param name="nonce">x-nonce value.</param>
        /// <param name="body">Request body bytes, or null.</param>
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
        /// <param name="secretKey">Secret key.</param>
        /// <param name="canonical">Canonical string.</param>
        /// <returns>Lowercase hex signature.</returns>
        /// <exception cref="ArgumentNullException">Thrown when secretKey is null or empty.</exception>
        public static string Sign(string secretKey, string canonical)
        {
            if (String.IsNullOrEmpty(secretKey)) throw new ArgumentNullException(nameof(secretKey));
            return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secretKey), Encoding.UTF8.GetBytes(canonical ?? String.Empty))).ToLowerInvariant();
        }

        /// <summary>
        /// Whether a date header is within the allowed skew of now.
        /// </summary>
        /// <param name="date">x-date value.</param>
        /// <param name="skewSeconds">Allowed skew in seconds.</param>
        /// <returns>True when acceptable.</returns>
        public static bool DateWithinSkew(string? date, int skewSeconds)
        {
            if (String.IsNullOrEmpty(date)) return false;
            if (!DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed)) return false;
            return Math.Abs((DateTime.UtcNow - parsed).TotalSeconds) <= skewSeconds;
        }
    }
}
