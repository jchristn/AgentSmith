namespace Hugo.Connectors.Abstractions
{
    using System;

    /// <summary>
    /// Static description of a connector tool. Treat as immutable after construction.
    /// </summary>
    public class ToolDescriptor
    {
        #region Public-Members

        /// <summary>
        /// Tool name, <c>family_operation</c> in snake case (for example <c>sql_query</c>). Required.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
        public string Name
        {
            get
            {
                return _Name;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Name));
                _Name = value;
            }
        }

        /// <summary>
        /// Short human title (for example <c>Run SQL query</c>).
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Description written for an LLM: what the tool does, when to use it, limits.
        /// </summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>
        /// JSON Schema (2020-12 subset) of the tool arguments, as a JSON object string, excluding the <c>resource</c>
        /// argument that Hugo adds. Default is an empty object schema.
        /// </summary>
        public string InputSchemaJson
        {
            get
            {
                return _InputSchemaJson;
            }
            set
            {
                _InputSchemaJson = String.IsNullOrWhiteSpace(value) ? "{\"type\":\"object\",\"properties\":{}}" : value;
            }
        }

        /// <summary>
        /// Base effect: the effect of the least severe call this tool can make. A tool whose calls differ in effect
        /// (for example <c>sql_query</c>) classifies each call in <see cref="IConnectorTool.Classify"/>. Default Read.
        /// </summary>
        public EffectEnum BaseEffect { get; set; } = EffectEnum.Read;

        /// <summary>
        /// Whether the effect is decided per call. When false, every call has <see cref="BaseEffect"/>. Default false.
        /// </summary>
        public bool ClassifiedPerCall { get; set; } = false;

        /// <summary>
        /// Short description of the result shape, for documentation.
        /// </summary>
        public string OutputDescription { get; set; } = String.Empty;

        #endregion

        #region Private-Members

        private string _Name = "tool";
        private string _InputSchemaJson = "{\"type\":\"object\",\"properties\":{}}";

        #endregion
    }
}
