namespace Test.Shared.Agent.StubLlm
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// An OpenAI tool definition as received by the stub (only the name is read).
    /// </summary>
    public class StubToolWire
    {
        /// <summary>
        /// Type (<c>function</c>).
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = null;

        /// <summary>
        /// Function definition.
        /// </summary>
        [JsonPropertyName("function")]
        public StubFunctionCallWire? Function { get; set; } = null;
    }
}
