namespace Test.Shared.Integration.Mcp
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A JSON-RPC error as the tests read it.
    /// </summary>
    public class McpTestError
    {
        /// <summary>
        /// Code.
        /// </summary>
        [JsonPropertyName("code")]
        public int Code { get; set; } = 0;

        /// <summary>
        /// Message.
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; } = String.Empty;
    }
}
