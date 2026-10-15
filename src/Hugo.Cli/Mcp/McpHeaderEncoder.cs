namespace Hugo.Cli.Mcp
{
    using System;
    using System.Globalization;
    using System.Text;
    using System.Text.Json;

    /// <summary>
    /// Encodes values for the <c>Mcp-Name</c> and <c>Mcp-Param-{Name}</c> headers of the stateless revision
    /// (2026-07-28). Printable ASCII without leading or trailing whitespace is sent as is; anything else is sent as
    /// <c>=?base64?{UTF-8 bytes in Base64}?=</c>. Thread-safe (stateless).
    /// </summary>
    public static class McpHeaderEncoder
    {
        #region Private-Members

        private const string _Prefix = "=?base64?";
        private const string _Suffix = "?=";
        private const long _MaxSafeInteger = 9007199254740991;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Encode a header value.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>Header-safe text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when value is null.</exception>
        public static string Encode(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (IsPlainSafe(value) && !LooksEncoded(value)) return value;
            return _Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(value)) + _Suffix;
        }

        /// <summary>
        /// Format a tool argument for an <c>Mcp-Param-{Name}</c> header: strings are encoded, integers within the
        /// JavaScript safe range are written in decimal, and booleans as <c>true</c> or <c>false</c>. Other values get
        /// no header.
        /// </summary>
        /// <param name="value">Argument value.</param>
        /// <param name="type">Declared schema type, or null.</param>
        /// <returns>Header text, or null when the value cannot be mirrored.</returns>
        public static string? FormatArgument(JsonElement value, string? type)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    if (type != null && type != "string") return null;
                    return Encode(value.GetString() ?? String.Empty);
                case JsonValueKind.Number:
                    if (type != null && type != "integer") return null;
                    if (!value.TryGetInt64(out long number) || number > _MaxSafeInteger || number < -_MaxSafeInteger) return null;
                    return number.ToString(CultureInfo.InvariantCulture);
                case JsonValueKind.True:
                    return type == null || type == "boolean" ? "true" : null;
                case JsonValueKind.False:
                    return type == null || type == "boolean" ? "false" : null;
                default:
                    return null;
            }
        }

        #endregion

        #region Private-Methods

        private static bool IsPlainSafe(string value)
        {
            if (value.Length == 0) return true;
            if (Char.IsWhiteSpace(value[0]) || Char.IsWhiteSpace(value[value.Length - 1])) return false;
            foreach (char c in value)
            {
                if (c < 0x20 || c > 0x7E) return false;
            }
            return true;
        }

        private static bool LooksEncoded(string value)
        {
            return value.Length >= _Prefix.Length + _Suffix.Length
                && value.StartsWith(_Prefix, StringComparison.Ordinal)
                && value.EndsWith(_Suffix, StringComparison.Ordinal);
        }

        #endregion
    }
}
