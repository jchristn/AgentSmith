namespace Test.Shared.Connectors.Ssh
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Typed view of a tool input schema, used to check that schemas parse and are documented.
    /// </summary>
    public class SshSchemaShape
    {
        /// <summary>
        /// JSON schema type; must be <c>object</c>.
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = null;

        /// <summary>
        /// Properties by name.
        /// </summary>
        [JsonPropertyName("properties")]
        public Dictionary<string, SshSchemaProperty>? Properties { get; set; } = null;

        /// <summary>
        /// Required property names.
        /// </summary>
        [JsonPropertyName("required")]
        public List<string>? Required { get; set; } = null;

        /// <summary>
        /// Whether unknown properties are allowed.
        /// </summary>
        [JsonPropertyName("additionalProperties")]
        public bool? AdditionalProperties { get; set; } = null;
    }
}
