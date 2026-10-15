namespace Hugo.Connectors.Logging
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// JSON helpers for the logging connectors. Thread-safe.
    /// </summary>
    public static class LoggingJson
    {
        #region Public-Members

        /// <summary>
        /// Serializer options: case-insensitive reads, nulls omitted on write, compact output.
        /// </summary>
        public static JsonSerializerOptions Options { get; } = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Serialize a value to compact JSON.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>JSON text.</returns>
        public static string Serialize(object value)
        {
            return JsonSerializer.Serialize(value, value == null ? typeof(object) : value.GetType(), Options);
        }

        /// <summary>
        /// Deserialize an upstream response body.
        /// </summary>
        /// <typeparam name="T">Target type.</typeparam>
        /// <param name="json">Body text.</param>
        /// <param name="provider">Provider name for error messages.</param>
        /// <returns>Value. Never null.</returns>
        /// <exception cref="ConnectorException">Thrown with code <c>upstream_error</c> when the body is not the expected shape.</exception>
        public static T DeserializeUpstream<T>(string json, string provider) where T : class
        {
            try
            {
                T? value = JsonSerializer.Deserialize<T>(String.IsNullOrWhiteSpace(json) ? "null" : json, Options);
                if (value == null) throw new ConnectorException("upstream_error", provider + " returned an empty response body.");
                return value;
            }
            catch (JsonException e)
            {
                throw new ConnectorException("upstream_error", provider + " returned a response in an unexpected shape: " + e.Message, e);
            }
        }

        /// <summary>
        /// Try to read an upstream error body.
        /// </summary>
        /// <param name="json">Body text.</param>
        /// <returns>Parsed body, or null when the body is not JSON.</returns>
        public static UpstreamErrorBody? TryReadError(string? json)
        {
            if (String.IsNullOrWhiteSpace(json)) return null;
            string trimmed = json.TrimStart();
            if (!trimmed.StartsWith('{')) return null;
            try
            {
                return JsonSerializer.Deserialize<UpstreamErrorBody>(trimmed, Options);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        #endregion
    }
}
