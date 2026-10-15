namespace Test.Shared.Connectors.Ssh
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Typed view of one property in a tool input schema.
    /// </summary>
    public class SshSchemaProperty
    {
        /// <summary>
        /// JSON schema type.
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = null;

        /// <summary>
        /// Description written for the LLM.
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; } = null;
    }
}
