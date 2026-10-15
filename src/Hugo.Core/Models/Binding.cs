namespace Hugo.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Enums;
    using Hugo.Core.Helpers;

    /// <summary>
    /// Attaches a resource to an agent profile under an alias with an access mode (plan sections 7.4 and 11). Not
    /// thread-safe.
    /// </summary>
    public class Binding
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix bnd_).
        /// </summary>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                _Id = String.IsNullOrWhiteSpace(value) ? IdGenerator.NewBindingId() : value;
            }
        }

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string TenantId
        {
            get
            {
                return _TenantId;
            }
            set
            {
                _TenantId = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Profile identifier.
        /// </summary>
        public string AgentProfileId
        {
            get
            {
                return _AgentProfileId;
            }
            set
            {
                _AgentProfileId = value ?? String.Empty;
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
        /// Alias the profile's tools use in the resource argument; unique per profile. Defaults to the resource
        /// name.
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
        /// Access mode. Default ReadOnly.
        /// </summary>
        public AccessModeEnum AccessMode { get; set; } = AccessModeEnum.ReadOnly;

        /// <summary>
        /// Whether Destructive calls need approval under ReadWrite. Default true (D-010).
        /// </summary>
        public bool RequireApprovalForDestructive { get; set; } = true;

        /// <summary>
        /// How InstructionText changes the connector instructions. Default Inherit.
        /// </summary>
        public InstructionModeEnum InstructionMode { get; set; } = InstructionModeEnum.Inherit;

        /// <summary>
        /// Binding instruction text for Append or Replace, or null.
        /// </summary>
        public string? InstructionText { get; set; } = null;

        /// <summary>
        /// Tighter output byte cap for this binding, or null for the connector default. Minimum 1024, maximum
        /// 16777216.
        /// </summary>
        public int? MaxOutputBytes
        {
            get
            {
                return _MaxOutputBytes;
            }
            set
            {
                _MaxOutputBytes = value.HasValue ? Math.Clamp(value.Value, 1024, 16777216) : null;
            }
        }

        /// <summary>
        /// Tighter row cap for this binding, or null for the connector default. Minimum 1, maximum 100000.
        /// </summary>
        public int? MaxRows
        {
            get
            {
                return _MaxRows;
            }
            set
            {
                _MaxRows = value.HasValue ? Math.Clamp(value.Value, 1, 100000) : null;
            }
        }

        /// <summary>
        /// Whether the binding is usable. Default true.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Whether tools that update resource knowledge may run through this binding. Default true.
        /// </summary>
        public bool AllowKnowledgeUpdates { get; set; } = true;

        /// <summary>
        /// Per-tool rules (Allow, Deny, RequireApproval).
        /// </summary>
        public List<BindingToolRule> ToolRules
        {
            get
            {
                return _ToolRules;
            }
            set
            {
                _ToolRules = value ?? new List<BindingToolRule>();
            }
        }

        /// <summary>
        /// Command deny patterns (.NET regular expressions, case-insensitive). The pipeline applies them to the
        /// primary argument of per-call classified tools (for example the command of ssh_exec or the sql of sql_query) and
        /// denies a matching call before it is classified.
        /// </summary>
        public List<string> DenyPatterns
        {
            get
            {
                return _DenyPatterns;
            }
            set
            {
                _DenyPatterns = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Resource name, populated on read; not persisted.
        /// </summary>
        public string? ResourceName { get; set; } = null;

        /// <summary>
        /// Connector type of the resource, populated on read; not persisted.
        /// </summary>
        public string? ConnectorType { get; set; } = null;

        /// <summary>
        /// Creation timestamp (UTC).
        /// </summary>
        public DateTime CreatedUtc
        {
            get
            {
                return _CreatedUtc;
            }
            set
            {
                _CreatedUtc = TimestampHelper.Normalize(value);
            }
        }

        /// <summary>
        /// Last update timestamp (UTC).
        /// </summary>
        public DateTime LastUpdateUtc
        {
            get
            {
                return _LastUpdateUtc;
            }
            set
            {
                _LastUpdateUtc = TimestampHelper.Normalize(value);
            }
        }

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.NewBindingId();
        private string _TenantId = String.Empty;
        private string _AgentProfileId = String.Empty;
        private string _ResourceId = String.Empty;
        private string _Alias = String.Empty;
        private int? _MaxOutputBytes = null;
        private int? _MaxRows = null;
        private List<BindingToolRule> _ToolRules = new List<BindingToolRule>();
        private List<string> _DenyPatterns = new List<string>();
        private DateTime _CreatedUtc = DateTime.UtcNow;
        private DateTime _LastUpdateUtc = DateTime.UtcNow;

        #endregion
    }
}
