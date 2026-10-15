namespace Hugo.Cli.Mcp
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A JSON-RPC 2.0 message as the relay sees it: the envelope fields are typed, and <c>id</c>, <c>params</c>,
    /// <c>result</c>, and <c>error</c> are carried as opaque values the relay passes through unchanged. Unknown
    /// top-level members are kept in <see cref="Extra"/>. Not thread-safe.
    /// </summary>
    public class JsonRpcMessage
    {
        #region Public-Members

        /// <summary>
        /// Protocol version marker; <c>2.0</c> for valid messages.
        /// </summary>
        [JsonPropertyName("jsonrpc")]
        [JsonConverter(typeof(LenientStringConverter))]
        public string? JsonRpc { get; set; } = null;

        /// <summary>
        /// Request or response ID (string or number), or null for notifications.
        /// </summary>
        [JsonPropertyName("id")]
        public JsonElement? Id { get; set; } = null;

        /// <summary>
        /// Method, for requests and notifications; null for responses.
        /// </summary>
        [JsonPropertyName("method")]
        [JsonConverter(typeof(LenientStringConverter))]
        public string? Method { get; set; } = null;

        /// <summary>
        /// Parameters, or null.
        /// </summary>
        [JsonPropertyName("params")]
        public JsonElement? Params { get; set; } = null;

        /// <summary>
        /// Result of a successful response, or null.
        /// </summary>
        [JsonPropertyName("result")]
        public JsonElement? Result { get; set; } = null;

        /// <summary>
        /// Error of a failed response, or null.
        /// </summary>
        [JsonPropertyName("error")]
        public JsonElement? Error { get; set; } = null;

        /// <summary>
        /// Other top-level members, kept so a rewritten message loses nothing.
        /// </summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; set; } = null;

        /// <summary>
        /// Whether this message has a non-null ID.
        /// </summary>
        [JsonIgnore]
        public bool HasId
        {
            get
            {
                return Id != null && Id.Value.ValueKind != JsonValueKind.Null && Id.Value.ValueKind != JsonValueKind.Undefined;
            }
        }

        /// <summary>
        /// Whether this message is a request (method and ID).
        /// </summary>
        [JsonIgnore]
        public bool IsRequest
        {
            get
            {
                return !String.IsNullOrEmpty(Method) && HasId;
            }
        }

        /// <summary>
        /// Whether this message is a notification (method, no ID).
        /// </summary>
        [JsonIgnore]
        public bool IsNotification
        {
            get
            {
                return !String.IsNullOrEmpty(Method) && !HasId;
            }
        }

        /// <summary>
        /// Whether this message is a response (no method).
        /// </summary>
        [JsonIgnore]
        public bool IsResponse
        {
            get
            {
                return String.IsNullOrEmpty(Method);
            }
        }

        /// <summary>
        /// The ID as raw JSON text (for example <c>7</c> or <c>"abc"</c>), used to match responses to requests; null
        /// when there is no ID.
        /// </summary>
        [JsonIgnore]
        public string? IdKey
        {
            get
            {
                return HasId ? Id!.Value.GetRawText() : null;
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse one JSON-RPC object. Returns null when the text is not a JSON object.
        /// </summary>
        /// <param name="json">JSON text.</param>
        /// <returns>Message or null.</returns>
        public static JsonRpcMessage? TryParse(string? json)
        {
            if (String.IsNullOrWhiteSpace(json)) return null;
            string trimmed = json.TrimStart();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal)) return null;
            return CliJson.TryDeserialize<JsonRpcMessage>(trimmed);
        }

        /// <summary>
        /// Build an error response.
        /// </summary>
        /// <param name="id">ID of the request being answered, or null.</param>
        /// <param name="code">JSON-RPC error code.</param>
        /// <param name="message">Error message.</param>
        /// <param name="httpStatus">HTTP status that caused the error, or null.</param>
        /// <returns>Compact JSON text.</returns>
        public static string ErrorJson(JsonElement? id, int code, string message, int? httpStatus)
        {
            JsonRpcErrorResponse response = new JsonRpcErrorResponse();
            response.Id = id;
            response.Error.Code = code;
            response.Error.Message = message ?? String.Empty;
            if (httpStatus != null) response.Error.Data = new JsonRpcErrorData { HttpStatus = httpStatus.Value, Source = "hugo-cli" };
            return JsonSerializer.Serialize(response, CliJson.Options);
        }

        /// <summary>
        /// Serialize this message as compact JSON.
        /// </summary>
        /// <returns>Compact JSON text.</returns>
        public string ToJson()
        {
            return JsonSerializer.Serialize(this, CliJson.Options);
        }

        #endregion
    }
}
