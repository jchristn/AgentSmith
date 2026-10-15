namespace Hugo.Connectors.Observability
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// JSON helpers for the observability connectors. Thread-safe.
    /// </summary>
    public static class ObservabilityJson
    {
        #region Public-Members

        /// <summary>
        /// Serializer options: case-insensitive reads, nulls omitted on write, compact output, numbers readable from
        /// strings.
        /// </summary>
        public static JsonSerializerOptions Options { get; } = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
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
                if (value == null) throw new ConnectorException(ConnectorErrorCodes.UpstreamError, provider + " returned an empty response body.");
                return value;
            }
            catch (JsonException e)
            {
                throw new ConnectorException(ConnectorErrorCodes.UpstreamError, provider + " returned a response in an unexpected shape: " + e.Message, e);
            }
        }

        /// <summary>
        /// Deserialize a free-form element into a typed value.
        /// </summary>
        /// <typeparam name="T">Target type.</typeparam>
        /// <param name="element">Element.</param>
        /// <param name="provider">Provider name for error messages.</param>
        /// <returns>Value, or null when the element is null.</returns>
        /// <exception cref="ConnectorException">Thrown with code <c>upstream_error</c> when the element is not the expected shape.</exception>
        public static T? DeserializeElement<T>(JsonElement element, string provider) where T : class
        {
            try
            {
                return element.Deserialize<T>(Options);
            }
            catch (JsonException e)
            {
                throw new ConnectorException(ConnectorErrorCodes.UpstreamError, provider + " returned a result in an unexpected shape: " + e.Message, e);
            }
        }

        /// <summary>
        /// Extract a human-readable message from an upstream error body (Prometheus and Loki <c>{"error": "..."}</c>,
        /// Elasticsearch <c>{"error": {"reason": "..."}}</c>, or plain text).
        /// </summary>
        /// <param name="body">Body text.</param>
        /// <returns>Message, or the raw body.</returns>
        public static string ErrorMessage(string? body)
        {
            if (String.IsNullOrWhiteSpace(body)) return String.Empty;
            string trimmed = body.TrimStart();
            if (!trimmed.StartsWith('{')) return body.Trim();
            try
            {
                EsErrorEnvelope? es = JsonSerializer.Deserialize<EsErrorEnvelope>(trimmed, Options);
                if (es != null && es.Error != null)
                {
                    string reason = es.Error.Reason ?? es.Error.Type ?? String.Empty;
                    if (es.Error.RootCause != null && es.Error.RootCause.Count > 0 && !String.IsNullOrEmpty(es.Error.RootCause[0].Reason) && es.Error.RootCause[0].Reason != reason)
                        reason = reason + " (root cause: " + es.Error.RootCause[0].Reason + ")";
                    if (!String.IsNullOrEmpty(reason)) return reason;
                }
            }
            catch (JsonException)
            {
            }
            try
            {
                PromErrorBody? prom = JsonSerializer.Deserialize<PromErrorBody>(trimmed, Options);
                if (prom != null && !String.IsNullOrEmpty(prom.Error)) return String.IsNullOrEmpty(prom.ErrorType) ? prom.Error : prom.ErrorType + ": " + prom.Error;
            }
            catch (JsonException)
            {
            }
            return body.Trim();
        }

        #endregion
    }
}
