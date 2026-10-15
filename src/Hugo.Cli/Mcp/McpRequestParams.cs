namespace Hugo.Cli.Mcp
{
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The request parameters the relay reads to set HTTP headers: the routing name (<c>tools/call</c> and
    /// <c>prompts/get</c> use <c>name</c>, <c>resources/read</c> uses <c>uri</c>), tool arguments, the requested
    /// protocol version of <c>initialize</c>, and the stateless <c>_meta</c>. Everything else is ignored.
    /// </summary>
    public class McpRequestParams
    {
        /// <summary>
        /// Tool or prompt name, or null.
        /// </summary>
        [JsonPropertyName("name")]
        [JsonConverter(typeof(LenientStringConverter))]
        public string? Name { get; set; } = null;

        /// <summary>
        /// Resource URI, or null.
        /// </summary>
        [JsonPropertyName("uri")]
        [JsonConverter(typeof(LenientStringConverter))]
        public string? Uri { get; set; } = null;

        /// <summary>
        /// Protocol version the client asks for in <c>initialize</c>, or null.
        /// </summary>
        [JsonPropertyName("protocolVersion")]
        [JsonConverter(typeof(LenientStringConverter))]
        public string? ProtocolVersion { get; set; } = null;

        /// <summary>
        /// Tool arguments of <c>tools/call</c> as opaque values, or null.
        /// </summary>
        [JsonPropertyName("arguments")]
        public Dictionary<string, JsonElement>? Arguments { get; set; } = null;

        /// <summary>
        /// Request metadata, or null.
        /// </summary>
        [JsonPropertyName("_meta")]
        public McpRequestMeta? Meta { get; set; } = null;
    }
}
