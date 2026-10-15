namespace Hugo.Connectors.Storage
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// Content type and encoding helpers for object reads and writes. Thread-safe.
    /// </summary>
    public static class StorageContent
    {
        #region Private-Members

        private static readonly UTF8Encoding _StrictUtf8 = new UTF8Encoding(false, true);

        private static readonly Dictionary<string, string> _ExtensionTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".txt", "text/plain; charset=utf-8" },
            { ".log", "text/plain; charset=utf-8" },
            { ".md", "text/markdown; charset=utf-8" },
            { ".csv", "text/csv; charset=utf-8" },
            { ".tsv", "text/tab-separated-values; charset=utf-8" },
            { ".html", "text/html; charset=utf-8" },
            { ".htm", "text/html; charset=utf-8" },
            { ".css", "text/css; charset=utf-8" },
            { ".js", "text/javascript; charset=utf-8" },
            { ".json", "application/json" },
            { ".ndjson", "application/x-ndjson" },
            { ".jsonl", "application/x-ndjson" },
            { ".xml", "application/xml" },
            { ".yaml", "application/yaml" },
            { ".yml", "application/yaml" },
            { ".toml", "application/toml" },
            { ".ini", "text/plain; charset=utf-8" },
            { ".conf", "text/plain; charset=utf-8" },
            { ".sh", "text/x-shellscript; charset=utf-8" },
            { ".sql", "application/sql" },
            { ".png", "image/png" },
            { ".jpg", "image/jpeg" },
            { ".jpeg", "image/jpeg" },
            { ".gif", "image/gif" },
            { ".svg", "image/svg+xml" },
            { ".webp", "image/webp" },
            { ".pdf", "application/pdf" },
            { ".zip", "application/zip" },
            { ".gz", "application/gzip" },
            { ".tar", "application/x-tar" },
            { ".parquet", "application/vnd.apache.parquet" }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a content type denotes text (any <c>text/*</c>, JSON, XML, YAML, JavaScript, CSV, SQL, TOML, NDJSON,
        /// form data, or a <c>+json</c> or <c>+xml</c> suffix).
        /// </summary>
        /// <param name="contentType">Content type; null or empty returns false.</param>
        /// <returns>True when textual.</returns>
        public static bool IsTextualContentType(string? contentType)
        {
            if (String.IsNullOrWhiteSpace(contentType)) return false;
            string type = contentType.Split(';')[0].Trim().ToLowerInvariant();
            if (type.StartsWith("text/", StringComparison.Ordinal)) return true;
            if (type.EndsWith("+json", StringComparison.Ordinal) || type.EndsWith("+xml", StringComparison.Ordinal)) return true;
            switch (type)
            {
                case "application/json":
                case "application/x-ndjson":
                case "application/xml":
                case "application/yaml":
                case "application/x-yaml":
                case "application/javascript":
                case "application/x-javascript":
                case "application/sql":
                case "application/toml":
                case "application/csv":
                case "application/x-www-form-urlencoded":
                case "application/x-sh":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Guess a content type from a key's extension.
        /// </summary>
        /// <param name="key">Object key.</param>
        /// <param name="fallback">Value returned when the extension is unknown.</param>
        /// <returns>Content type.</returns>
        public static string GuessContentType(string? key, string fallback)
        {
            if (String.IsNullOrEmpty(key)) return fallback;
            int slash = key.LastIndexOf('/');
            int dot = key.LastIndexOf('.');
            if (dot <= slash + 1) return fallback;
            string ext = key.Substring(dot);
            return _ExtensionTypes.TryGetValue(ext, out string? type) ? type : fallback;
        }

        /// <summary>
        /// Try to decode bytes as strict UTF-8 text. Fails on invalid sequences and on NUL bytes. When
        /// <paramref name="allowIncompleteTail"/> is true, up to three trailing bytes of an incomplete multi-byte
        /// sequence (from a truncated read) are dropped instead of failing.
        /// </summary>
        /// <param name="data">Bytes.</param>
        /// <param name="count">Number of bytes to consider, from the start.</param>
        /// <param name="allowIncompleteTail">Whether a cut multi-byte sequence at the end is tolerated.</param>
        /// <param name="text">Decoded text, or empty on failure.</param>
        /// <param name="usedBytes">Bytes actually decoded.</param>
        /// <returns>True when the bytes are valid UTF-8 text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when data is null.</exception>
        public static bool TryDecodeUtf8(byte[] data, int count, bool allowIncompleteTail, out string text, out int usedBytes)
        {
            ArgumentNullException.ThrowIfNull(data);
            if (count < 0 || count > data.Length) count = data.Length;
            text = String.Empty;
            usedBytes = 0;

            int length = count;
            if (allowIncompleteTail) length = CompleteUtf8Length(data, count);

            for (int i = 0; i < length; i++)
            {
                if (data[i] == 0) return false;
            }

            try
            {
                text = _StrictUtf8.GetString(data, 0, length);
                usedBytes = length;
                return true;
            }
            catch (DecoderFallbackException)
            {
                text = String.Empty;
                return false;
            }
        }

        /// <summary>
        /// Length of the longest prefix of the buffer that does not end inside a multi-byte UTF-8 sequence.
        /// </summary>
        /// <param name="data">Bytes.</param>
        /// <param name="count">Number of bytes to consider.</param>
        /// <returns>Length.</returns>
        /// <exception cref="ArgumentNullException">Thrown when data is null.</exception>
        public static int CompleteUtf8Length(byte[] data, int count)
        {
            ArgumentNullException.ThrowIfNull(data);
            if (count <= 0) return 0;
            int start = count - 1;
            int back = 0;
            while (start >= 0 && back < 3 && (data[start] & 0xC0) == 0x80)
            {
                start--;
                back++;
            }
            if (start < 0) return count;
            byte lead = data[start];
            int needed;
            if ((lead & 0x80) == 0) needed = 1;
            else if ((lead & 0xE0) == 0xC0) needed = 2;
            else if ((lead & 0xF0) == 0xE0) needed = 3;
            else if ((lead & 0xF8) == 0xF0) needed = 4;
            else return count;
            int available = count - start;
            return available < needed ? start : count;
        }

        /// <summary>
        /// Format a byte count for people (for example <c>1.5 MiB</c>).
        /// </summary>
        /// <param name="bytes">Bytes.</param>
        /// <returns>Text.</returns>
        public static string FormatBytes(long bytes)
        {
            string[] units = new string[] { "B", "KiB", "MiB", "GiB", "TiB", "PiB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return unit == 0
                ? bytes + " B"
                : value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " " + units[unit];
        }

        #endregion
    }
}
