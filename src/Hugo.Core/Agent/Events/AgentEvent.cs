namespace Hugo.Core.Agent.Events
{
    using System;

    /// <summary>
    /// Base of every agent turn event (D-022). <see cref="EventName"/> is the SSE event name. Events of one turn
    /// carry strictly increasing <see cref="Sequence"/> numbers starting at 1, which SSE uses as the event ID for
    /// resume.
    /// </summary>
    public abstract class AgentEvent
    {
        #region Public-Members

        /// <summary>
        /// SSE event name (for example <c>turn.started</c>).
        /// </summary>
        public abstract string EventName { get; }

        /// <summary>
        /// Turn ID.
        /// </summary>
        public string TurnId { get; set; } = String.Empty;

        /// <summary>
        /// Conversation ID.
        /// </summary>
        public string ConversationId { get; set; } = String.Empty;

        /// <summary>
        /// Sequence number within the turn, from 1.
        /// </summary>
        public long Sequence { get; set; } = 0;

        /// <summary>
        /// When the event was created (UTC).
        /// </summary>
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
