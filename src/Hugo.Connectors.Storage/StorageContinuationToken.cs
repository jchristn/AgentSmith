namespace Hugo.Connectors.Storage
{
    using System;
    using System.Text;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Opaque continuation tokens for <c>storage_list</c>. A token carries the last key (or folder prefix) returned, and
    /// the next page resumes after it in lexicographic order. Thread-safe.
    /// </summary>
    public static class StorageContinuationToken
    {
        #region Private-Members

        private const string Marker = "hugo-storage-v1:";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Encode a token from the last returned key.
        /// </summary>
        /// <param name="lastKey">Last returned key or folder prefix.</param>
        /// <returns>Token.</returns>
        /// <exception cref="ArgumentNullException">Thrown when lastKey is null or empty.</exception>
        public static string Encode(string lastKey)
        {
            if (String.IsNullOrEmpty(lastKey)) throw new ArgumentNullException(nameof(lastKey));
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(Marker + lastKey)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>
        /// Decode a token and check that it belongs to the listing prefix.
        /// </summary>
        /// <param name="token">Token; null or empty means the first page.</param>
        /// <param name="prefix">Effective listing prefix.</param>
        /// <returns>The last returned key, or null for the first page.</returns>
        /// <exception cref="ConnectorException">Code <c>invalid_argument</c> when the token is malformed or was issued for
        /// another prefix.</exception>
        public static string? Decode(string? token, string prefix)
        {
            if (String.IsNullOrEmpty(token)) return null;
            string text;
            try
            {
                string b64 = token.Replace('-', '+').Replace('_', '/');
                while (b64.Length % 4 != 0) b64 += "=";
                text = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            }
            catch (FormatException)
            {
                throw new ConnectorException("invalid_argument", "Argument 'continuationToken' is not a token returned by storage_list.");
            }

            if (!text.StartsWith(Marker, StringComparison.Ordinal) || text.Length == Marker.Length)
                throw new ConnectorException("invalid_argument", "Argument 'continuationToken' is not a token returned by storage_list.");
            string key = text.Substring(Marker.Length);
            if (!key.StartsWith(prefix ?? String.Empty, StringComparison.Ordinal))
                throw new ConnectorException("invalid_argument", "Argument 'continuationToken' was issued for a different prefix; repeat the call with the same prefix and delimiter.");
            return key;
        }

        #endregion
    }
}
