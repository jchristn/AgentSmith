namespace Hugo.Server.Protocols
{
    using System;
    using Hugo.Core.Enums;

    /// <summary>
    /// The permission an MCP tool requires (P7.9): its resource type and operation, the same pair its REST twin uses.
    /// </summary>
    public class McpToolAuthorization
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
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
        /// Tool kind.
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
        /// One-line summary.
        /// </summary>
        public string Summary { get; set; } = String.Empty;

        #endregion

        #region Private-Members

        private string _Name = "unnamed";

        #endregion
    }
}
