namespace Hugo.Server.Protocols
{
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Arguments of hugo_admin_update_binding.
    /// </summary>
    public class McpUpdateBindingArguments
    {
        /// <summary>
        /// Profile name or id.
        /// </summary>
        public string? Profile { get; set; } = null;

        /// <summary>
        /// Binding alias or id.
        /// </summary>
        public string? Binding { get; set; } = null;

        /// <summary>
        /// New access mode.
        /// </summary>
        public AccessModeEnum? AccessMode { get; set; } = null;

        /// <summary>
        /// New destructive approval policy, or null to keep it.
        /// </summary>
        public bool? RequireApprovalForDestructive { get; set; } = null;

        /// <summary>
        /// New enabled flag, or null to keep it.
        /// </summary>
        public bool? Enabled { get; set; } = null;
    }
}
