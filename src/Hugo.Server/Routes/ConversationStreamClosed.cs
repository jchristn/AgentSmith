namespace Hugo.Server.Routes
{
    using System;

    /// <summary>
    /// Data of the <c>stream.closed</c> event that ends a chat SSE stream, sent after <c>turn.completed</c> once the turn
    /// is recorded (and the conversation possibly titled).
    /// </summary>
    public class ConversationStreamClosed
    {
        /// <summary>
        /// Conversation identifier.
        /// </summary>
        public string ConversationId { get; set; } = String.Empty;

        /// <summary>
        /// Turn identifier.
        /// </summary>
        public string TurnId { get; set; } = String.Empty;

        /// <summary>
        /// Finish reason label.
        /// </summary>
        public string FinishReason { get; set; } = "completed";

        /// <summary>
        /// Conversation title after the turn, or null.
        /// </summary>
        public string? Title { get; set; } = null;
    }
}
