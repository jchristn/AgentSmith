namespace Hugo.Sdk.Requests
{
    using System;

    /// <summary>
    /// A chat message: the body of POST /v1.0/api/conversations/{id}/messages and POST /v1.0/api/agent-profiles/{id}/ask.
    /// </summary>
    public class ChatMessageRequest
    {
        #region Public-Members

        /// <summary>
        /// Message text (required).
        /// </summary>
        public string Message { get; set; } = String.Empty;

        /// <summary>
        /// For ask: the conversation to continue, or null to start a new one. Ignored by conversation messages (the path names the conversation).
        /// </summary>
        public string? ConversationId { get; set; } = null;

        #endregion
    }
}
