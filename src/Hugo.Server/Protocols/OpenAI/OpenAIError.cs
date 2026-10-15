namespace Hugo.Server.Protocols.OpenAI
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Error detail in the OpenAI error shape.
    /// </summary>
    public class OpenAIError
    {
        /// <summary>
        /// Message.
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; } = String.Empty;

        /// <summary>
        /// Type: invalid_request_error, authentication_error, permission_error, not_found_error, conflict_error, or
        /// server_error.
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = "invalid_request_error";

        /// <summary>
        /// Offending parameter, or null.
        /// </summary>
        [JsonPropertyName("param")]
        public string? Param { get; set; } = null;

        /// <summary>
        /// Stable code, for example tools_not_supported.
        /// </summary>
        [JsonPropertyName("code")]
        public string? Code { get; set; } = null;
    }
}
