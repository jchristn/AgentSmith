namespace Hugo.Core.Models
{
    using System;
    using Hugo.Core.Enums;
    using Hugo.Core.Helpers;

    /// <summary>
    /// What a caller talks to (plan section 10.1): a persona, limits, endpoints, exposure flags, and bindings to resources.
    /// Not thread-safe.
    /// </summary>
    public class AgentProfile
    {
        #region Public-Members

        /// <summary>Atomic document attachment-set version, starting at zero. Read-only in management requests.</summary>
        public long DocumentSetVersion { get; set; } = 0;

        /// <summary>
        /// Identifier (prefix agp_).
        /// </summary>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                _Id = String.IsNullOrWhiteSpace(value) ? IdGenerator.NewAgentProfileId() : value;
            }
        }

        /// <summary>
        /// Owning tenant identifier.
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
        /// Owning user, or null.
        /// </summary>
        public string? OwnerUserId { get; set; } = null;

        /// <summary>
        /// Private (owner and administrators only) or Tenant. Default Tenant.
        /// </summary>
        public VisibilityEnum Visibility { get; set; } = VisibilityEnum.Tenant;

        /// <summary>
        /// URL-safe name, unique per tenant (for example Hugo). Letters, digits, hyphens, and underscores, 1 to 64
        /// characters.
        /// </summary>
        public string Name
        {
            get
            {
                return _Name;
            }
            set
            {
                _Name = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Display name, or null to use the name.
        /// </summary>
        public string? DisplayName { get; set; } = null;

        /// <summary>
        /// Name the persona calls itself. Default Agent Smith.
        /// </summary>
        public string PersonaName
        {
            get
            {
                return _PersonaName;
            }
            set
            {
                _PersonaName = value ?? "Agent Smith";
            }
        }

        /// <summary>
        /// How the persona addresses the operator. Default Mr. Anderson.
        /// </summary>
        public string UserAddress
        {
            get
            {
                return _UserAddress;
            }
            set
            {
                _UserAddress = value ?? "Mr. Anderson";
            }
        }

        /// <summary>
        /// Description, or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Prompt template key of the persona. Default persona.agent-smith.
        /// </summary>
        public string PersonaTemplateKey
        {
            get
            {
                return _PersonaTemplateKey;
            }
            set
            {
                _PersonaTemplateKey = value ?? "persona.agent-smith";
            }
        }

        /// <summary>
        /// Inference model endpoint, or null (MCP raw-tool mode needs none).
        /// </summary>
        public string? InferenceEndpointId { get; set; } = null;

        /// <summary>
        /// Fallback inference endpoint, or null.
        /// </summary>
        public string? FallbackInferenceEndpointId { get; set; } = null;

        /// <summary>
        /// Embedding endpoint for tool retrieval, or null.
        /// </summary>
        public string? EmbeddingEndpointId { get; set; } = null;

        /// <summary>
        /// Maximum model iterations per turn. Default 12, minimum 1, maximum 100.
        /// </summary>
        public int MaxIterations
        {
            get
            {
                return _MaxIterations;
            }
            set
            {
                _MaxIterations = Math.Clamp(value, 1, 100);
            }
        }

        /// <summary>
        /// Maximum tool calls per turn. Default 24, minimum 1, maximum 500.
        /// </summary>
        public int MaxToolCallsPerTurn
        {
            get
            {
                return _MaxToolCallsPerTurn;
            }
            set
            {
                _MaxToolCallsPerTurn = Math.Clamp(value, 1, 500);
            }
        }

        /// <summary>
        /// Tool timeout in seconds. Default 120, minimum 1, maximum 3600.
        /// </summary>
        public int ToolTimeoutSeconds
        {
            get
            {
                return _ToolTimeoutSeconds;
            }
            set
            {
                _ToolTimeoutSeconds = Math.Clamp(value, 1, 3600);
            }
        }

        /// <summary>
        /// Sampling temperature, or null for the endpoint default.
        /// </summary>
        public double? Temperature { get; set; } = null;

        /// <summary>
        /// Whether the profile is reachable over A2A. Default true.
        /// </summary>
        public bool ExposeA2A { get; set; } = true;

        /// <summary>
        /// Whether the profile is reachable over MCP. Default true.
        /// </summary>
        public bool ExposeMcp { get; set; } = true;

        /// <summary>
        /// Whether the profile is reachable over the OpenAI-compatible surface. Default true.
        /// </summary>
        public bool ExposeOpenAI { get; set; } = true;

        /// <summary>
        /// Whether management tools are offered to admin-capable callers. Default false.
        /// </summary>
        public bool ExposeManagementTools { get; set; } = false;

        /// <summary>
        /// Profile-level free text appended to the compiled instructions, or null.
        /// </summary>
        public string? InstructionsAppend { get; set; } = null;

        /// <summary>
        /// Connector guidance detail in compiled instructions. Default <see cref="InstructionDetailEnum.Auto"/>.
        /// </summary>
        public InstructionDetailEnum InstructionDetail { get; set; } = InstructionDetailEnum.Auto;

        /// <summary>
        /// Whether this is the tenant's default profile.
        /// </summary>
        public bool IsDefault { get; set; } = false;

        /// <summary>
        /// Number of resources bound to the profile. Filled by the profile list and read routes; not stored. Null when
        /// not computed.
        /// </summary>
        public int? BindingCount { get; set; } = null;

        /// <summary>
        /// Whether the profile is active.
        /// </summary>
        public bool Active { get; set; } = true;

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

        /// <summary>Standalone heading, or null to use display name. Maximum 120 characters.</summary>
        public string? ChatTitle { get; set; } = null;

        /// <summary>Standalone logo HTTP(S) URL or absolute local path. Null uses the Hugo logo.</summary>
        public string? ChatLogoUrl { get; set; } = null;

        /// <summary>Browser page title, or null to use the standalone heading. Maximum 200 characters.</summary>
        public string? ChatPageTitle { get; set; } = null;

        /// <summary>Whether this profile's standalone chat link accepts visitors. Default false.</summary>
        public bool ChatEnabled { get; set; } = false;

        /// <summary>Visitor access policy. Default Authenticated.</summary>
        public ChatAccessModeEnum ChatAccessMode { get; set; } = ChatAccessModeEnum.Authenticated;

        /// <summary>Opaque server-generated standalone URL identifier.</summary>
        public string? ChatLinkId { get; set; } = null;

        /// <summary>Publishing credential used for public visitors; never sent to visitors.</summary>
        public string? ChatCredentialId { get; set; } = null;

        /// <summary>Revocation generation for visitor capabilities. Not a user-editable setting.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public int ChatRevision { get; set; } = 1;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.NewAgentProfileId();
        private string _TenantId = String.Empty;
        private string _Name = String.Empty;
        private string _PersonaName = "Agent Smith";
        private string _UserAddress = "Mr. Anderson";
        private string _PersonaTemplateKey = "persona.agent-smith";
        private int _MaxIterations = 12;
        private int _MaxToolCallsPerTurn = 24;
        private int _ToolTimeoutSeconds = 120;
        private DateTime _CreatedUtc = DateTime.UtcNow;
        private DateTime _LastUpdateUtc = DateTime.UtcNow;

        #endregion
    }
}
