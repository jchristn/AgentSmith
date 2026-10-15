namespace Hugo.Connectors.Redis
{
    using System;
    using System.Text;
    using System.Text.Json.Serialization;
    using StackExchange.Redis;

    /// <summary>
    /// A Redis value prepared for display: UTF-8 text when the bytes are printable text, otherwise base64, cut to a byte
    /// cap. Immutable after creation; thread-safe for reads.
    /// </summary>
    public class RedisDisplayValue
    {
        #region Public-Members

        /// <summary>
        /// Display text (base64 when <see cref="Binary"/> is true). Null for a nil value.
        /// </summary>
        [JsonPropertyName("text")]
        public string? Text { get; set; } = null;

        /// <summary>
        /// Size of the value in bytes before truncation.
        /// </summary>
        [JsonPropertyName("bytes")]
        public long Bytes { get; set; } = 0;

        /// <summary>
        /// Whether the value was cut to the byte cap.
        /// </summary>
        [JsonPropertyName("truncated")]
        public bool Truncated { get; set; } = false;

        /// <summary>
        /// Whether <see cref="Text"/> is base64 because the value is not printable UTF-8.
        /// </summary>
        [JsonPropertyName("binary")]
        public bool Binary { get; set; } = false;

        #endregion

        #region Private-Members

        private static readonly UTF8Encoding _StrictUtf8 = new UTF8Encoding(false, true);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Prepare a value.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="maxBytes">Byte cap; minimum 1.</param>
        /// <returns>Display value.</returns>
        public static RedisDisplayValue From(RedisValue value, int maxBytes)
        {
            if (value.IsNull) return new RedisDisplayValue();
            byte[]? bytes = (byte[]?)value;
            return FromBytes(bytes ?? Array.Empty<byte>(), maxBytes, bytes == null ? 0 : bytes.LongLength);
        }

        /// <summary>
        /// Prepare raw bytes.
        /// </summary>
        /// <param name="bytes">Bytes (possibly already a prefix of the value).</param>
        /// <param name="maxBytes">Byte cap; minimum 1.</param>
        /// <param name="totalBytes">Full size of the value, which may exceed the bytes given.</param>
        /// <returns>Display value.</returns>
        /// <exception cref="ArgumentNullException">Thrown when bytes is null.</exception>
        public static RedisDisplayValue FromBytes(byte[] bytes, int maxBytes, long totalBytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            int cap = Math.Max(1, maxBytes);
            int length = Math.Min(bytes.Length, cap);
            bool truncated = totalBytes > length;
            if (truncated)
            {
                while (length > 0 && length < bytes.Length && (bytes[length] & 0xC0) == 0x80) length--;
            }

            RedisDisplayValue v = new RedisDisplayValue { Bytes = Math.Max(totalBytes, bytes.LongLength), Truncated = truncated };
            string? text = TryText(bytes, length);
            if (text != null)
            {
                v.Text = text;
            }
            else
            {
                v.Binary = true;
                v.Text = Convert.ToBase64String(bytes, 0, Math.Min(bytes.Length, cap));
            }
            return v;
        }

        /// <summary>
        /// One-line form for tables and listings: quoted text with escapes, <c>(nil)</c>, or <c>base64:...</c>, with a
        /// truncation note.
        /// </summary>
        /// <returns>Text.</returns>
        public string ToInline()
        {
            if (Text == null) return "(nil)";
            string body = Binary ? "base64:" + Text : "\"" + Escape(Text) + "\"";
            return Truncated ? body + " [truncated, " + Bytes + " bytes]" : body;
        }

        #endregion

        #region Private-Methods

        private static string? TryText(byte[] bytes, int length)
        {
            string text;
            try
            {
                text = _StrictUtf8.GetString(bytes, 0, length);
            }
            catch (DecoderFallbackException)
            {
                return null;
            }
            foreach (char c in text)
            {
                if (c < 0x20 && c != '\n' && c != '\r' && c != '\t') return null;
                if (c == 0x7F) return null;
            }
            return text;
        }

        private static string Escape(string text)
        {
            return text.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\t", "\\t", StringComparison.Ordinal);
        }

        #endregion
    }
}
