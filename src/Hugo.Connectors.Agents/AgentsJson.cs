namespace Hugo.Connectors.Agents
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// JSON helpers shared by the agent connectors: one options instance for wire models and structured output, typed
    /// deserialization with a stable error, and compact pass-through of opaque values. Thread-safe.
    /// </summary>
    public static class AgentsJson
    {
        #region Public-Members

        /// <summary>
        /// Options: case-insensitive reads, camelCase writes, nulls omitted.
        /// </summary>
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Deserialize a remote JSON document into a wire model.
        /// </summary>
        /// <typeparam name="T">Model type.</typeparam>
        /// <param name="json">JSON text. Null or empty yields a new instance.</param>
        /// <param name="what">Description of the document for the error message.</param>
        /// <returns>The model. Never null.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>upstream_error</c> when the JSON does not match the model.</exception>
        public static T Read<T>(string? json, string what) where T : class, new()
        {
            if (String.IsNullOrWhiteSpace(json) || json.Trim() == "null") return new T();
            try
            {
                return JsonSerializer.Deserialize<T>(json, Options) ?? new T();
            }
            catch (JsonException e)
            {
                throw new ConnectorException(ConnectorErrorCodes.UpstreamError, "The remote server returned " + what + " that Hugo could not read: " + e.Message);
            }
        }

        /// <summary>
        /// Serialize a value with <see cref="Options"/>.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>JSON text.</returns>
        public static string Write(object? value)
        {
            return JsonSerializer.Serialize(value, Options);
        }

        /// <summary>
        /// Compact JSON text of an opaque value (for example an input schema), or null when absent or JSON null.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>Compact JSON, or null.</returns>
        public static string? Compact(JsonElement? value)
        {
            if (value == null) return null;
            JsonElement e = value.Value;
            if (e.ValueKind == JsonValueKind.Undefined || e.ValueKind == JsonValueKind.Null) return null;
            return JsonSerializer.Serialize(e);
        }

        /// <summary>
        /// Compact JSON text of any value already parsed by a client library (for example an A2A data part), or null.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>Compact JSON, or null.</returns>
        public static string? CompactObject(object? value)
        {
            if (value == null) return null;
            if (value is JsonElement element) return Compact(element);
            return JsonSerializer.Serialize(value, value.GetType());
        }

        /// <summary>
        /// Parse a JSON object string into an opaque value for pass-through to a remote server.
        /// </summary>
        /// <param name="json">JSON text. Null or empty means an empty object.</param>
        /// <param name="argumentName">Argument name for the error message.</param>
        /// <returns>The value (a detached element).</returns>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_argument</c> when the text is not a JSON object.</exception>
        public static JsonElement ParseObject(string? json, string argumentName)
        {
            string text = String.IsNullOrWhiteSpace(json) ? "{}" : json;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(text))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Argument '" + argumentName + "' must be a JSON object.");
                    return doc.RootElement.Clone();
                }
            }
            catch (JsonException e)
            {
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Argument '" + argumentName + "' is not valid JSON: " + e.Message);
            }
        }

        #endregion
    }
}
