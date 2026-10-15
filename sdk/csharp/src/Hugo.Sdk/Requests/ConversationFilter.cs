namespace Hugo.Sdk.Requests
{
    using System;

    /// <summary>
    /// Conversation list filter.
    /// </summary>
    public class ConversationFilter
    {
        #region Public-Members

        /// <summary>
        /// Agent profile identifier.
        /// </summary>
        public string? AgentProfileId { get; set; } = null;

        /// <summary>
        /// Dashboard, A2A, OpenAI, Mcp, or Rest.
        /// </summary>
        public string? Source { get; set; } = null;

        #endregion
    }
}
