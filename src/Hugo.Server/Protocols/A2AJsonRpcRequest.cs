namespace Hugo.Server.Protocols
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A JSON-RPC 2.0 request envelope as the A2A endpoint receives it. <see cref="Params"/> is kept as raw JSON and
    /// deserialized into the typed request of the method; <see cref="Id"/> is echoed back unchanged.
    /// </summary>
    public class A2AJsonRpcRequest
    {
        /// <summary>
        /// Protocol version (2.0).
        /// </summary>
        [JsonPropertyName("jsonrpc")]
        public string? JsonRpc { get; set; } = null;

        /// <summary>
        /// Request identifier (string or number), echoed in the response.
        /// </summary>
        [JsonPropertyName("id")]
        public JsonElement? Id { get; set; } = null;

        /// <summary>
        /// Method name (A2A 1.0 or legacy slash-style).
        /// </summary>
        [JsonPropertyName("method")]
        public string? Method { get; set; } = null;

        /// <summary>
        /// Parameters as raw JSON.
        /// </summary>
        [JsonPropertyName("params")]
        public JsonElement? Params { get; set; } = null;
    }
}
