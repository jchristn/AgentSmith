namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>
    /// A per-tool rule on a binding.
    /// </summary>
    public class BindingToolRule
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Binding Identifier.
        /// </summary>
        public string? BindingId { get; set; } = null;

        /// <summary>
        /// Tenant Identifier.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Tool name.
        /// </summary>
        public string ToolName { get; set; } = String.Empty;

        /// <summary>
        /// Allow, Deny, or RequireApproval.
        /// </summary>
        public string Rule { get; set; } = String.Empty;

        #endregion
    }
}
