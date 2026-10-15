namespace Hugo.Cli.Mcp
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The <c>data</c> member of an error the relay produced itself.
    /// </summary>
    public class JsonRpcErrorData
    {
        /// <summary>
        /// HTTP status returned by the server, or 0 when no response arrived.
        /// </summary>
        [JsonPropertyName("httpStatus")]
        public int HttpStatus { get; set; } = 0;

        /// <summary>
        /// Who produced the error: always <c>hugo-cli</c>.
        /// </summary>
        [JsonPropertyName("source")]
        public string Source { get; set; } = String.Empty;
    }
}
