namespace Hugo.Cli.Mcp
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The <c>error</c> member of a JSON-RPC error response.
    /// </summary>
    public class JsonRpcErrorBody
    {
        /// <summary>
        /// Error code. The relay uses -32700 (parse error) and -32603 (internal error, for transport failures).
        /// </summary>
        [JsonPropertyName("code")]
        public int Code { get; set; } = -32603;

        /// <summary>
        /// Message.
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; } = String.Empty;

        /// <summary>
        /// Additional data, or null.
        /// </summary>
        [JsonPropertyName("data")]
        public JsonRpcErrorData? Data { get; set; } = null;
    }
}
