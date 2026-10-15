namespace Hugo.Sdk.Requests
{
    using System.Collections.Generic;
    using Hugo.Sdk.Models;

    /// <summary>
    /// Create or update a binding (preview). Null properties are omitted.
    /// </summary>
    public class BindingRequest
    {
        #region Public-Members

        /// <summary>
        /// Resource Identifier.
        /// </summary>
        public string? ResourceId { get; set; } = null;

        /// <summary>
        /// Alias.
        /// </summary>
        public string? Alias { get; set; } = null;

        /// <summary>
        /// ReadOnly, ReadWriteWithApproval, or ReadWrite.
        /// </summary>
        public string? AccessMode { get; set; } = null;

        /// <summary>
        /// Require approval for destructive.
        /// </summary>
        public bool? RequireApprovalForDestructive { get; set; } = null;

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
        public bool? Enabled { get; set; } = null;

        /// <summary>
        /// Allow knowledge updates.
        /// </summary>
        public bool? AllowKnowledgeUpdates { get; set; } = null;

        /// <summary>
        /// Tool rules.
        /// </summary>
        public List<BindingToolRule>? ToolRules { get; set; } = null;

        /// <summary>
        /// Deny patterns.
        /// </summary>
        public List<string>? DenyPatterns { get; set; } = null;

        #endregion
    }
}
