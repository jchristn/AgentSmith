namespace Hugo.Sdk.Requests
{
    using System;
    using Hugo.Sdk.Models;

    /// <summary>
    /// A tool call for invoke or dry run (preview).
    /// </summary>
    public class ToolCallRequest
    {
        #region Public-Members

        /// <summary>
        /// Agent profile identifier.
        /// </summary>
        public string? ProfileId { get; set; } = null;

        /// <summary>
        /// Agent profile name, as an alternative to ProfileId.
        /// </summary>
        public string? Profile { get; set; } = null;

        /// <summary>
        /// Binding alias (or resource name) to target.
        /// </summary>
        public string? Resource { get; set; } = null;

        /// <summary>
        /// Resource Identifier.
        /// </summary>
        public string? ResourceId { get; set; } = null;

        /// <summary>
        /// Tool name, for example ssh_exec.
        /// </summary>
        public string ToolName { get; set; } = String.Empty;

        /// <summary>
        /// Tool arguments object.
        /// </summary>
        public object? Arguments { get; set; } = null;

        #endregion
    }
}
