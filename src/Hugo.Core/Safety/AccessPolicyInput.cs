namespace Hugo.Core.Safety
{
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Enums;

    /// <summary>
    /// Inputs to the access policy for one tool call.
    /// </summary>
    public class AccessPolicyInput
    {
        /// <summary>
        /// Classification produced by the tool.
        /// </summary>
        public EffectClassification Classification { get; set; } = new EffectClassification();

        /// <summary>
        /// Binding access mode.
        /// </summary>
        public AccessModeEnum AccessMode { get; set; } = AccessModeEnum.ReadOnly;

        /// <summary>
        /// Resource ceiling: the most permissive access mode any binding of the resource may have. Null means no ceiling.
        /// </summary>
        public AccessModeEnum? ResourceCeiling { get; set; } = null;

        /// <summary>
        /// Whether Destructive calls require approval under <see cref="AccessModeEnum.ReadWrite"/>. Default true.
        /// </summary>
        public bool RequireApprovalForDestructive { get; set; } = true;

        /// <summary>
        /// Binding tool rule for this tool, or null when none is configured.
        /// </summary>
        public ToolRuleEnum? ToolRule { get; set; } = null;

        /// <summary>
        /// Whether the tenant is in lockdown (every non-Read call is denied).
        /// </summary>
        public bool TenantLockdown { get; set; } = false;
    }
}
