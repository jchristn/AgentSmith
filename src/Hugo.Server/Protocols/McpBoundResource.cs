namespace Hugo.Server.Protocols
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// One resource bound to the caller's profile (hugo_list_resources, hugo_whoami).
    /// </summary>
    public class McpBoundResource
    {
        /// <summary>
        /// Alias: the value of each tool's resource argument.
        /// </summary>
        public string Alias { get; set; } = String.Empty;

        /// <summary>
        /// Connector type (for example sqlite).
        /// </summary>
        public string ConnectorType { get; set; } = String.Empty;

        /// <summary>
        /// Connector display name.
        /// </summary>
        public string Connector { get; set; } = String.Empty;

        /// <summary>
        /// Tool family (tools are named family_operation).
        /// </summary>
        public string Family { get; set; } = String.Empty;

        /// <summary>
        /// Environment label, or null.
        /// </summary>
        public string? Environment { get; set; } = null;

        /// <summary>
        /// Effective access mode.
        /// </summary>
        public AccessModeEnum AccessMode { get; set; } = AccessModeEnum.ReadOnly;

        /// <summary>
        /// Resource description, or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Operator notes, or null.
        /// </summary>
        public string? Notes { get; set; } = null;
    }
}
