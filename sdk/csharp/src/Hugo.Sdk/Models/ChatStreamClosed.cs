namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>
    /// Data of the stream.closed chat event, the last event of a chat stream (read with ChatEvent.DataAs).
    /// </summary>
    public class ChatStreamClosed
    {
        #region Public-Members

        /// <summary>
        /// Conversation identifier.
        /// </summary>
        public string ConversationId { get; set; } = String.Empty;

        /// <summary>
        /// Turn identifier.
        /// </summary>
        public string? TurnId { get; set; } = null;

        /// <summary>
        /// Finish reason label.
        /// </summary>
        public string? FinishReason { get; set; } = null;

        /// <summary>
        /// Conversation title after the turn (the server titles new conversations).
        /// </summary>
        public string? Title { get; set; } = null;

        #endregion
    }
}
