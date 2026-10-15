namespace Hugo.Core.Connectors
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The JSON Schema 2020-12 subset Hugo tool input schemas use (P3.10): type, properties, required, enum, numeric and
    /// length bounds, items, additionalProperties, and pattern, plus the Hugo extension x-hugo-primary that marks the
    /// primary command argument for binding deny patterns. Unknown keywords are ignored. Not thread-safe.
    /// </summary>
    public class ToolInputSchema
    {
        #region Public-Members

        /// <summary>
        /// Allowed types (string, integer, number, boolean, array, object, null). Empty allows any type.
        /// </summary>
        [JsonPropertyName("type")]
        [JsonConverter(typeof(SchemaTypeListConverter))]
        public List<string> Type
        {
            get
            {
                return _Type;
            }
            set
            {
                _Type = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Object properties.
        /// </summary>
        [JsonPropertyName("properties")]
        public Dictionary<string, ToolInputSchema>? Properties { get; set; } = null;

        /// <summary>
        /// Required property names.
        /// </summary>
        [JsonPropertyName("required")]
        public List<string>? Required { get; set; } = null;

        /// <summary>
        /// Allowed values, compared by their JSON text.
        /// </summary>
        [JsonPropertyName("enum")]
        public List<JsonElement>? Enum { get; set; } = null;

        /// <summary>
        /// Inclusive numeric minimum.
        /// </summary>
        [JsonPropertyName("minimum")]
        public double? Minimum { get; set; } = null;

        /// <summary>
        /// Inclusive numeric maximum.
        /// </summary>
        [JsonPropertyName("maximum")]
        public double? Maximum { get; set; } = null;

        /// <summary>
        /// Minimum string length.
        /// </summary>
        [JsonPropertyName("minLength")]
        public int? MinLength { get; set; } = null;

        /// <summary>
        /// Maximum string length.
        /// </summary>
        [JsonPropertyName("maxLength")]
        public int? MaxLength { get; set; } = null;

        /// <summary>
        /// Minimum array length.
        /// </summary>
        [JsonPropertyName("minItems")]
        public int? MinItems { get; set; } = null;

        /// <summary>
        /// Maximum array length.
        /// </summary>
        [JsonPropertyName("maxItems")]
        public int? MaxItems { get; set; } = null;

        /// <summary>
        /// Schema of array items.
        /// </summary>
        [JsonPropertyName("items")]
        public ToolInputSchema? Items { get; set; } = null;

        /// <summary>
        /// additionalProperties as written: false forbids unknown properties; true, absent, or a schema allows them.
        /// </summary>
        [JsonPropertyName("additionalProperties")]
        public JsonElement? AdditionalProperties { get; set; } = null;

        /// <summary>
        /// Regular expression a string must match.
        /// </summary>
        [JsonPropertyName("pattern")]
        public string? Pattern { get; set; } = null;

        /// <summary>
        /// Description.
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; } = null;

        /// <summary>
        /// Hugo extension: marks this property as the primary command argument of a per-call classified tool.
        /// </summary>
        [JsonPropertyName("x-hugo-primary")]
        public bool? HugoPrimary { get; set; } = null;

        #endregion

        #region Private-Members

        private List<string> _Type = new List<string>();

        #endregion
    }
}
