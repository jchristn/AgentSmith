namespace Hugo.Connectors.Storage
{
    using System;
    using System.Text;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Validates object keys and listing prefixes and enforces the resource's prefix restriction. Keys with dot segments,
    /// backslashes, a leading slash, or control characters are refused, because HTTP clients may normalize dot segments
    /// and escape the restriction. Thread-safe.
    /// </summary>
    public static class StorageKeyPolicy
    {
        #region Public-Members

        /// <summary>
        /// Maximum key length in UTF-8 bytes (the S3 and GCS limit).
        /// </summary>
        public const int MaximumKeyBytes = 1024;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validate an object key and enforce the prefix restriction.
        /// </summary>
        /// <param name="key">Object key.</param>
        /// <param name="prefixRestriction">Prefix restriction; empty means none.</param>
        /// <param name="argumentName">Argument name used in messages.</param>
        /// <returns>The key, unchanged.</returns>
        /// <exception cref="ConnectorException">Code <c>invalid_argument</c> when the key is malformed, or
        /// <c>permission_denied</c> when it is outside the prefix restriction.</exception>
        public static string ValidateKey(string? key, string? prefixRestriction, string argumentName = "key")
        {
            if (String.IsNullOrEmpty(key)) throw new ConnectorException("invalid_argument", "Argument '" + argumentName + "' is required.");
            string? problem = Describe(key, false);
            if (problem != null) throw new ConnectorException("invalid_argument", "Argument '" + argumentName + "' is not a valid object key: " + problem + ".");
            EnforceRestriction(key, prefixRestriction, argumentName);
            return key;
        }

        /// <summary>
        /// Resolve a listing prefix. An empty prefix becomes the prefix restriction; a non-empty prefix must start with it.
        /// </summary>
        /// <param name="prefix">Requested prefix; null or empty means the whole allowed space.</param>
        /// <param name="prefixRestriction">Prefix restriction; empty means none.</param>
        /// <returns>The effective prefix; empty means the whole bucket.</returns>
        /// <exception cref="ConnectorException">Code <c>invalid_argument</c> when the prefix is malformed, or
        /// <c>permission_denied</c> when it is outside the prefix restriction.</exception>
        public static string ResolvePrefix(string? prefix, string? prefixRestriction)
        {
            string restriction = prefixRestriction ?? String.Empty;
            if (String.IsNullOrEmpty(prefix)) return restriction;
            string? problem = Describe(prefix, true);
            if (problem != null) throw new ConnectorException("invalid_argument", "Argument 'prefix' is not a valid key prefix: " + problem + ".");
            EnforceRestriction(prefix, restriction, "prefix");
            return prefix;
        }

        /// <summary>
        /// Whether a key or prefix is inside the prefix restriction.
        /// </summary>
        /// <param name="key">Key or prefix.</param>
        /// <param name="prefixRestriction">Prefix restriction; empty means none.</param>
        /// <returns>True when allowed.</returns>
        public static bool IsWithinRestriction(string? key, string? prefixRestriction)
        {
            if (String.IsNullOrEmpty(prefixRestriction)) return true;
            if (key == null) return false;
            return key.StartsWith(prefixRestriction, StringComparison.Ordinal);
        }

        /// <summary>
        /// Describe why a key or prefix is malformed.
        /// </summary>
        /// <param name="value">Key or prefix.</param>
        /// <param name="isPrefix">True when validating a prefix (which may be partial).</param>
        /// <returns>A short reason, or null when the value is well formed.</returns>
        public static string? Describe(string value, bool isPrefix)
        {
            if (value == null) return "value is missing";
            if (Encoding.UTF8.GetByteCount(value) > MaximumKeyBytes) return "longer than " + MaximumKeyBytes + " UTF-8 bytes";
            if (value.StartsWith('/')) return "must not start with '/'";
            if (value.IndexOf('\\') >= 0) return "must not contain '\\'";
            foreach (char c in value)
            {
                if (c < 0x20 || c == 0x7F) return "must not contain control characters";
            }

            string[] segments = value.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];
                bool last = i == segments.Length - 1;
                if (segment == ".." || segment == ".")
                {
                    if (isPrefix && last && segment == ".") continue;
                    return "must not contain '.' or '..' path segments";
                }
            }
            return null;
        }

        #endregion

        #region Private-Methods

        private static void EnforceRestriction(string value, string? prefixRestriction, string argumentName)
        {
            if (IsWithinRestriction(value, prefixRestriction)) return;
            throw new ConnectorException(
                "permission_denied",
                "Argument '" + argumentName + "' ('" + value + "') is outside this resource's prefix restriction; every key must start with '" + prefixRestriction + "'.");
        }

        #endregion
    }
}
