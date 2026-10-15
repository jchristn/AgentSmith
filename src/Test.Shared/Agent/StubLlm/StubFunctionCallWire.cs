namespace Test.Shared.Agent.StubLlm
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Function name and arguments of an OpenAI tool call.
    /// </summary>
    public class StubFunctionCallWire
    {
        /// <summary>
        /// Name.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; } = null;

        /// <summary>
        /// Arguments JSON string.
        /// </summary>
        [JsonPropertyName("arguments")]
        public string? Arguments { get; set; } = null;
    }
}
