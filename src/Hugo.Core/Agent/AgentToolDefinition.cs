namespace Hugo.Core.Agent
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// A tool visible to one agent turn, as returned by <see cref="IAgentToolCatalog"/>. Built from the same per-caller
    /// catalog MCP uses (D-017): family tools carry a <c>resource</c> argument whose schema enum lists the bindings
    /// the profile can use.
    /// </summary>
    public class AgentToolDefinition
    {
        #region Public-Members

        /// <summary>
        /// Tool name (for example <c>sql_query</c>, <c>hugo_list_resources</c>).
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
        /// Description written for the LLM. Also embedded for tool retrieval.
        /// </summary>
        public string Description
        {
            get
            {
                return _Description;
            }
            set
            {
                _Description = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Full JSON Schema of the arguments as a JSON object string, including the <c>resource</c> enum. Default is an
        /// empty object schema.
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
        /// Whether this is a Hugo meta tool (<c>hugo_*</c>). Meta tools are always offered, even when tool retrieval
        /// selects a subset. Default false.
        /// </summary>
        public bool IsMetaTool { get; set; } = false;

        /// <summary>
        /// The effect every call of this tool has, when the catalog knows it in advance (for example a tool whose
        /// <see cref="ToolDescriptor.BaseEffect"/> is Read and which is not classified per call). Null when the effect
        /// is decided per call by the pipeline. The runtime only runs calls in parallel when this is
        /// <see cref="EffectEnum.Read"/>; the pipeline still classifies and decides every call.
        /// </summary>
        public EffectEnum? KnownEffect { get; set; } = null;

        /// <summary>
        /// Connector family (for example <c>sql</c>), for events and metrics. Null for meta tools.
        /// </summary>
        public string? Family { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Name = "tool";
        private string _Description = String.Empty;
        private string _InputSchemaJson = "{\"type\":\"object\",\"properties\":{}}";

        #endregion
    }
}
