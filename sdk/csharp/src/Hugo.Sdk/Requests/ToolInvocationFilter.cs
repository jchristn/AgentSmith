namespace Hugo.Sdk.Requests
{
    using System;
    using Hugo.Sdk.Models;

    /// <summary>
    /// Tool invocation list filter (preview).
    /// </summary>
    public class ToolInvocationFilter
    {
        #region Public-Members

        /// <summary>
        /// Agent profile Identifier.
        /// </summary>
        public string? AgentProfileId { get; set; } = null;

        /// <summary>
        /// Resource Identifier.
        /// </summary>
        public string? ResourceId { get; set; } = null;

        /// <summary>
        /// Tool name.
        /// </summary>
        public string? ToolName { get; set; } = null;

        /// <summary>
        /// Decision.
        /// </summary>
        public string? Decision { get; set; } = null;

        /// <summary>
        /// Status.
        /// </summary>
        public string? Status { get; set; } = null;

        /// <summary>
        /// Surface.
        /// </summary>
        public string? Surface { get; set; } = null;

        /// <summary>
        /// Conversation Identifier.
        /// </summary>
        public string? ConversationId { get; set; } = null;

        /// <summary>
        /// Principal Identifier.
        /// </summary>
        public string? PrincipalId { get; set; } = null;

        /// <summary>
        /// Since.
        /// </summary>
        public DateTime? Since { get; set; } = null;

        #endregion
    }
}
