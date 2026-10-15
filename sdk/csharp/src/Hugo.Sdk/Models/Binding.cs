namespace Hugo.Sdk.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A binding of a resource to an agent profile with an access mode (preview).
    /// </summary>
    public class Binding
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Tenant Identifier.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Agent profile Identifier.
        /// </summary>
        public string? AgentProfileId { get; set; } = null;

        /// <summary>
        /// Resource Identifier.
        /// </summary>
        public string ResourceId { get; set; } = String.Empty;

        /// <summary>
        /// Alias.
        /// </summary>
        public string Alias { get; set; } = String.Empty;

        /// <summary>
        /// ReadOnly, ReadWriteWithApproval, or ReadWrite.
        /// </summary>
        public string? AccessMode { get; set; } = null;

        /// <summary>
        /// Require approval for destructive.
        /// </summary>
        public bool RequireApprovalForDestructive { get; set; } = false;

        /// <summary>
        /// Inherit, Append, or Replace.
        /// </summary>
        public string? InstructionMode { get; set; } = null;

        /// <summary>
        /// Instruction text.
        /// </summary>
        public string? InstructionText { get; set; } = null;

        /// <summary>
        /// Max output bytes.
        /// </summary>
        public int? MaxOutputBytes { get; set; } = null;

        /// <summary>
        /// Max rows.
        /// </summary>
        public int? MaxRows { get; set; } = null;

        /// <summary>
        /// Enabled.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Allow knowledge updates.
        /// </summary>
        public bool AllowKnowledgeUpdates { get; set; } = false;

        /// <summary>
        /// Tool rules.
        /// </summary>
        public List<BindingToolRule> ToolRules { get; set; } = new List<BindingToolRule>();

        /// <summary>
        /// Deny patterns.
        /// </summary>
        public List<string> DenyPatterns { get; set; } = new List<string>();

        /// <summary>
        /// Resource name.
        /// </summary>
        public string? ResourceName { get; set; } = null;

        /// <summary>
        /// Connector type.
        /// </summary>
        public string? ConnectorType { get; set; } = null;

        /// <summary>
        /// Creation timestamp, UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update timestamp, UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
