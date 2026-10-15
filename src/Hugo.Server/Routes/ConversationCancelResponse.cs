namespace Hugo.Server.Routes
{
    using System;

    /// <summary>
    /// Response of <c>POST /v1.0/api/conversations/{id}/cancel</c>.
    /// </summary>
    public class ConversationCancelResponse
    {
        /// <summary>
        /// Conversation identifier.
        /// </summary>
        public string ConversationId { get; set; } = String.Empty;

        /// <summary>
        /// Whether a running turn was signalled to stop (false when none was running).
        /// </summary>
        public bool Cancelled { get; set; } = false;
    }
}
