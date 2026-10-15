namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>
    /// Data of an assistant.delta chat event (read with ChatEvent.DataAs).
    /// </summary>
    public class ChatAssistantDelta
    {
        #region Public-Members

        /// <summary>
        /// Turn identifier.
        /// </summary>
        public string TurnId { get; set; } = String.Empty;

        /// <summary>
        /// Conversation identifier.
        /// </summary>
        public string ConversationId { get; set; } = String.Empty;

        /// <summary>
        /// Event sequence within the turn.
        /// </summary>
        public long Sequence { get; set; } = 0;

        /// <summary>
        /// Text fragment.
        /// </summary>
        public string Text { get; set; } = String.Empty;

        /// <summary>
        /// Loop iteration.
        /// </summary>
        public int Iteration { get; set; } = 0;

        #endregion
    }
}
