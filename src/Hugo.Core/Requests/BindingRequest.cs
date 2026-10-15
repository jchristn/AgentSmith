namespace Hugo.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;

    /// <summary>
    /// Create or update a binding. Null fields keep their value; ToolRules and DenyPatterns, when present, replace the stored
    /// ones. MaxOutputBytes or MaxRows of 0 removes the binding-level limit.
    /// </summary>
    public class BindingRequest
    {
        #region Public-Members

        /// <summary>
        /// Resource to bind. Required on create; cannot change on update.
        /// </summary>
        public string? ResourceId { get; set; } = null;

        /// <summary>
        /// Alias, unique per profile. Defaults to the resource name.
        /// </summary>
        public string? Alias { get; set; } = null;

        /// <summary>
        /// Access mode. Default ReadOnly.
        /// </summary>
        public AccessModeEnum? AccessMode { get; set; } = null;

        /// <summary>
        /// Whether Destructive calls need approval under ReadWrite. Default true.
        /// </summary>
        public bool? RequireApprovalForDestructive { get; set; } = null;

        /// <summary>
        /// Inherit, Append, or Replace.
        /// </summary>
        public InstructionModeEnum? InstructionMode { get; set; } = null;

        /// <summary>
        /// Binding instruction text.
        /// </summary>
        public string? InstructionText { get; set; } = null;

        /// <summary>
        /// Output byte cap (1024 to 16777216), or 0 to remove.
        /// </summary>
        public int? MaxOutputBytes { get; set; } = null;

        /// <summary>
        /// Row cap (1 to 100000), or 0 to remove.
        /// </summary>
        public int? MaxRows { get; set; } = null;

        /// <summary>
        /// Whether the binding is usable.
        /// </summary>
        public bool? Enabled { get; set; } = null;

        /// <summary>
        /// Whether knowledge-updating tools may write through this binding.
        /// </summary>
        public bool? AllowKnowledgeUpdates { get; set; } = null;

        /// <summary>
        /// Tool rules (toolName, rule).
        /// </summary>
        public List<BindingToolRule>? ToolRules { get; set; } = null;

        /// <summary>
        /// Command deny patterns (regular expressions).
        /// </summary>
        public List<string>? DenyPatterns { get; set; } = null;

        #endregion
    }
}
