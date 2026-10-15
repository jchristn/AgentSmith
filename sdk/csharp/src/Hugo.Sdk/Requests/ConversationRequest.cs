namespace Hugo.Sdk.Requests
{
    /// <summary>
    /// Create or update a conversation (preview, verified when the Phase 7 conversation routes land).
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
