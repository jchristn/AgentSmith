namespace Hugo.Core.Instructions
{
    using System;
    using System.Collections.Generic;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// The full guidance for one bound resource of a profile (<see cref="InstructionCompiler.DescribeResourceAsync"/>):
    /// what hugo_describe_resource and the MCP resource hugo://resources/{alias} return.
    /// </summary>
    public class ResourceInstructions
    {
        #region Public-Members

        /// <summary>
        /// Binding alias (the value of the resource argument).
        /// </summary>
        public string Alias
        {
            get
            {
                return _Alias;
            }
            set
            {
                _Alias = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Resource identifier.
        /// </summary>
        public string ResourceId
        {
            get
            {
                return _ResourceId;
            }
            set
            {
                _ResourceId = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Connector type (for example sqlite).
        /// </summary>
        public string ConnectorType
        {
            get
            {
                return _ConnectorType;
            }
            set
            {
                _ConnectorType = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Connector display name (for example SQLite).
        /// </summary>
        public string ConnectorDisplayName
        {
            get
            {
                return _ConnectorDisplayName;
            }
            set
            {
                _ConnectorDisplayName = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Tool family (for example sql).
        /// </summary>
        public string Family
        {
            get
            {
                return _Family;
            }
            set
            {
                _Family = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Effective access mode (the binding's mode capped by the resource ceiling).
        /// </summary>
        public AccessModeEnum AccessMode { get; set; } = AccessModeEnum.ReadOnly;

        /// <summary>
        /// Whether Destructive calls need approval under ReadWrite.
        /// </summary>
        public bool RequireApprovalForDestructive { get; set; } = true;

        /// <summary>
        /// Environment label, or null.
        /// </summary>
        public string? Environment { get; set; } = null;

        /// <summary>
        /// Endpoint summary, or null.
        /// </summary>
        public string? Endpoint { get; set; } = null;

        /// <summary>
        /// Resource description, or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Operator notes, or null.
        /// </summary>
        public string? Notes { get; set; } = null;

        /// <summary>
        /// Root knowledge (bounded by Instructions.MaxKnowledgeCharsPerResource), or null.
        /// </summary>
        public string? Knowledge { get; set; } = null;

        /// <summary>
        /// The connector's full instructions with the binding's Append or Replace applied.
        /// </summary>
        public string Instructions
        {
            get
            {
                return _Instructions;
            }
            set
            {
                _Instructions = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Tools usable on this resource by the caller.
        /// </summary>
        public List<string> Tools
        {
            get
            {
                return _Tools;
            }
            set
            {
                _Tools = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Binding output byte limit, or null for the connector default.
        /// </summary>
        public int? MaxOutputBytes { get; set; } = null;

        /// <summary>
        /// Binding row limit, or null for the connector default.
        /// </summary>
        public int? MaxRows { get; set; } = null;

        /// <summary>
        /// Rendering warnings.
        /// </summary>
        public List<string> Warnings
        {
            get
            {
                return _Warnings;
            }
            set
            {
                _Warnings = value ?? new List<string>();
            }
        }

        #endregion

        #region Private-Members

        private string _Alias = String.Empty;
        private string _ResourceId = String.Empty;
        private string _ConnectorType = String.Empty;
        private string _ConnectorDisplayName = String.Empty;
        private string _Family = String.Empty;
        private string _Instructions = String.Empty;
        private List<string> _Tools = new List<string>();
        private List<string> _Warnings = new List<string>();

        #endregion
    }
}
