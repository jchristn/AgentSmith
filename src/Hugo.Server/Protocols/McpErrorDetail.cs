namespace Hugo.Server.Protocols
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// JSON-RPC error object.
    /// </summary>
    public class McpErrorDetail
    {
        /// <summary>
        /// JSON-RPC error code.
        /// </summary>
        [JsonPropertyName("code")]
        public int Code { get; set; } = -32603;

        /// <summary>
        /// Message. Hugo's messages start with a stable code ("ProfileLocked: ...").
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; } = String.Empty;
    }
}
