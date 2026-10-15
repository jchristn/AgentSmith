namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>
    /// Data of a turn.completed chat event (read with ChatEvent.DataAs).
    /// </summary>
    public class ChatTurnCompleted
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
        /// Completed, MaxIterations, ToolCallLimit, ApprovalPending, Cancelled, or Error.
        /// </summary>
        public string FinishReason { get; set; } = String.Empty;

        /// <summary>
        /// Final assistant text.
        /// </summary>
        public string FinalText { get; set; } = String.Empty;

        /// <summary>
        /// Identifier of the stored assistant message.
        /// </summary>
        public string? AssistantMessageId { get; set; } = null;

        /// <summary>
        /// Input tokens.
        /// </summary>
        public long TokensIn { get; set; } = 0;

        /// <summary>
        /// Output tokens.
        /// </summary>
        public long TokensOut { get; set; } = 0;

        /// <summary>
        /// Time to first token, milliseconds (-1 when unknown).
        /// </summary>
        public long TimeToFirstTokenMs { get; set; } = 0;

        /// <summary>
        /// Turn duration, milliseconds.
        /// </summary>
        public long DurationMs { get; set; } = 0;

        /// <summary>
        /// Loop iterations.
        /// </summary>
        public int Iterations { get; set; } = 0;

        /// <summary>
        /// Tool calls made.
        /// </summary>
        public int ToolCallCount { get; set; } = 0;

        /// <summary>
        /// Whether the fallback inference endpoint answered.
        /// </summary>
        public bool UsedFallback { get; set; } = false;

        /// <summary>
        /// Whether the history was compacted.
        /// </summary>
        public bool Compacted { get; set; } = false;

        #endregion
    }
}
