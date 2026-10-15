namespace Hugo.Server.Protocols
{
    using System;
    using Hugo.Core.Enums;

    /// <summary>
    /// A built-in MCP tool (meta, delegation, or management): name, description, input schema, and the permission it
    /// requires.
    /// </summary>
    public class McpToolDefinition
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Short title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Description shown to the model.
        /// </summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>
        /// JSON schema of the arguments.
        /// </summary>
        public string InputSchemaJson { get; set; } = "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}";

        /// <summary>
        /// Kind.
        /// </summary>
        public McpToolKindEnum Kind { get; set; } = McpToolKindEnum.Meta;

        /// <summary>
        /// Resource type checked before the tool runs.
        /// </summary>
        public ResourceTypeEnum ResourceType { get; set; } = ResourceTypeEnum.AgentProfile;

        /// <summary>
        /// Operation checked before the tool runs.
        /// </summary>
        public OperationTypeEnum Operation { get; set; } = OperationTypeEnum.Execute;

        /// <summary>
        /// Whether the tool only reads (the readOnlyHint annotation).
        /// </summary>
        public bool ReadOnly { get; set; } = true;

        #endregion

        #region Public-Methods

        /// <summary>
        /// The registry mapping of this tool.
        /// </summary>
        /// <returns>Mapping.</returns>
        public McpToolAuthorization Authorization()
        {
            McpToolAuthorization entry = new McpToolAuthorization();
            entry.Name = Name;
            entry.Kind = Kind;
            entry.ResourceType = ResourceType;
            entry.Operation = Operation;
            entry.Summary = Title;
            return entry;
        }

        #endregion
    }
}
