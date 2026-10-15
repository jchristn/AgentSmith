namespace Hugo.Core.Requests
{
    using System;

    /// <summary>
    /// A user message sent to a conversation (<c>POST /v1.0/api/conversations/{id}/messages</c>) or to a profile
    /// (<c>POST /v1.0/api/agent-profiles/{id}/ask</c>).
    /// </summary>
    public class ChatMessageRequest
    {
        /// <summary>
        /// The message. Required; at most 100000 characters.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Conversation to continue (ask route only). Null starts a new conversation.
        /// </summary>
        public string? ConversationId { get; set; } = null;
    }
}
