namespace Hugo.Connectors.Logging
{
    using System;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Opaque paging cursor handed to the calling LLM. It carries the SolarWinds Observability position (a skip token or tail timestamp and event ID)
    /// plus the original window and filters, so a follow-up call continues the same search. Encoded as base64url JSON.
    /// </summary>
    public class LogCursor
    {
        #region Public-Members

        /// <summary>
        /// Connector type that issued the cursor (<c>solarwinds-observability</c>).
        /// </summary>
        [JsonPropertyName("p")]
        public string Provider { get; set; } = String.Empty;

        /// <summary>
        /// Tool kind: <c>search</c> or <c>tail</c>.
        /// </summary>
        [JsonPropertyName("k")]
        public string Kind { get; set; } = String.Empty;

        /// <summary>
        /// SolarWinds Observability skip token for the next page.
        /// </summary>
        [JsonPropertyName("skip")]
        public string? SkipToken { get; set; } = null;

        /// <summary>
        /// Window start of the original search, UTC.
        /// </summary>
        [JsonPropertyName("start")]
        public DateTime? Start { get; set; } = null;

        /// <summary>
        /// Window end of the original search, UTC.
        /// </summary>
        [JsonPropertyName("end")]
        public DateTime? End { get; set; } = null;

        /// <summary>
        /// Query of the original call.
        /// </summary>
        [JsonPropertyName("q")]
        public string? Query { get; set; } = null;

        /// <summary>
        /// SolarWinds Observability log group filter of the original call.
        /// </summary>
        [JsonPropertyName("group")]
        public string? Group { get; set; } = null;

        /// <summary>
        /// SolarWinds Observability entity filter of the original call.
        /// </summary>
        [JsonPropertyName("entityId")]
        public string? EntityId { get; set; } = null;

        /// <summary>
        /// SolarWinds Observability direction of the original call.
        /// </summary>
        [JsonPropertyName("direction")]
        public string? Direction { get; set; } = null;

        /// <summary>
        /// SolarWinds Observability tail: time of the newest event already returned, UTC.
        /// </summary>
        [JsonPropertyName("lastTime")]
        public DateTime? LastTime { get; set; } = null;

        /// <summary>
        /// SolarWinds Observability tail: ID of the newest event already returned.
        /// </summary>
        [JsonPropertyName("lastId")]
        public string? LastId { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Encode as an opaque base64url string.
        /// </summary>
        /// <returns>Cursor text.</returns>
        public string Encode()
        {
            byte[] bytes = Encoding.UTF8.GetBytes(LoggingJson.Serialize(this));
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>
        /// Decode a cursor and check that it was issued by the expected connector and tool kind.
        /// </summary>
        /// <param name="cursor">Cursor text.</param>
        /// <param name="expectedProvider">Expected connector type.</param>
        /// <param name="expectedKind">Expected kind (<c>search</c> or <c>tail</c>).</param>
        /// <returns>Cursor.</returns>
        /// <exception cref="ConnectorException">Thrown with code <c>invalid_argument</c> when the cursor is malformed or was issued elsewhere.</exception>
        public static LogCursor Decode(string cursor, string expectedProvider, string expectedKind)
        {
            if (String.IsNullOrWhiteSpace(cursor)) throw new ConnectorException("invalid_argument", "Argument 'cursor' is empty.");
            LogCursor? decoded = null;
            try
            {
                string b64 = cursor.Trim().Replace('-', '+').Replace('_', '/');
                switch (b64.Length % 4)
                {
                    case 2: b64 += "=="; break;
                    case 3: b64 += "="; break;
                }
                string json = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                decoded = JsonSerializer.Deserialize<LogCursor>(json, LoggingJson.Options);
            }
            catch (FormatException)
            {
                decoded = null;
            }
            catch (JsonException)
            {
                decoded = null;
            }

            if (decoded == null)
                throw new ConnectorException("invalid_argument", "Argument 'cursor' is not a cursor returned by this tool. Pass the nextCursor value unchanged, or omit it.");
            if (!String.Equals(decoded.Provider, expectedProvider, StringComparison.Ordinal) || !String.Equals(decoded.Kind, expectedKind, StringComparison.Ordinal))
                throw new ConnectorException("invalid_argument", "Argument 'cursor' was issued by a different resource type or tool. Use the nextCursor from the same tool on the same resource.");
            return decoded;
        }

        #endregion
    }
}
