namespace Test.Shared.Agent.StubLlm
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// An OpenAI tool call inside an assistant message, as received by the stub.
    /// </summary>
    public class StubToolCallWire
    {
        /// <summary>
        /// Call ID.
        /// </summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; } = null;

        /// <summary>
        /// Type (<c>function</c>).
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = null;

        /// <summary>
        /// Function name and arguments.
        /// </summary>
        [JsonPropertyName("function")]
        public StubFunctionCallWire? Function { get; set; } = null;
    }
}
