namespace Hugo.Connectors.Http
{
    using System;
    using System.Text;

    /// <summary>
    /// Decides whether a response body is text and decodes it. Thread-safe.
    /// </summary>
    public static class HttpBodyText
    {
        #region Private-Members

        private static readonly UTF8Encoding _StrictUtf8 = new UTF8Encoding(false, true);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Try to decode a body as text. Textual content types (text/*, JSON, XML, JavaScript, YAML, CSV, form data) and
        /// bodies without a content type that are valid UTF-8 without NUL bytes are text.
        /// </summary>
        /// <param name="contentType">Content type, or null.</param>
        /// <param name="body">Body bytes.</param>
        /// <param name="text">Decoded text, or empty.</param>
        /// <returns>True when the body is text.</returns>
        public static bool TryDecode(string? contentType, byte[] body, out string text)
        {
            text = String.Empty;
            byte[] bytes = body ?? Array.Empty<byte>();
            if (bytes.Length == 0) return true;
            string ct = (contentType ?? String.Empty).ToLowerInvariant();
            bool declaredText = ct.StartsWith("text/", StringComparison.Ordinal)
                || ct.Contains("json", StringComparison.Ordinal)
                || ct.Contains("xml", StringComparison.Ordinal)
                || ct.Contains("javascript", StringComparison.Ordinal)
                || ct.Contains("yaml", StringComparison.Ordinal)
                || ct.Contains("csv", StringComparison.Ordinal)
                || ct.Contains("x-www-form-urlencoded", StringComparison.Ordinal)
                || ct.Contains("charset=", StringComparison.Ordinal);
            if (Array.IndexOf(bytes, (byte)0) >= 0 && !declaredText) return false;

            try
            {
                text = _StrictUtf8.GetString(bytes);
                return true;
            }
            catch (DecoderFallbackException)
            {
                if (!declaredText) return false;
                text = Encoding.UTF8.GetString(bytes);
                return true;
            }
        }

        #endregion
    }
}
