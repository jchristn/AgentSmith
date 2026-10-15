namespace Hugo.Sdk.Requests
{
    /// <summary>
    /// Create or update a conversation (preview).
    /// </summary>
    public class ConversationRequest
    {
        #region Public-Members

        /// <summary>
        /// Agent profile Identifier.
        /// </summary>
        public string? AgentProfileId { get; set; } = null;

        /// <summary>
        /// Title.
        /// </summary>
        public string? Title { get; set; } = null;

        #endregion
    }
}
