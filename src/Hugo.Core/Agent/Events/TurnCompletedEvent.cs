namespace Hugo.Core.Agent.Events
{
    using System;

    /// <summary>
    /// <c>turn.completed</c>: the last event of every turn, including cancelled and failed turns.
    /// </summary>
    public class TurnCompletedEvent : AgentEvent
    {
        /// <summary>
        /// Event name.
        /// </summary>
        public override string EventName
        {
            get
            {
                return "turn.completed";
            }
        }

        /// <summary>
        /// Finish reason.
        /// </summary>
        public AgentFinishReasonEnum FinishReason { get; set; } = AgentFinishReasonEnum.Completed;

        /// <summary>
        /// Final answer text.
        /// </summary>
        public string FinalText { get; set; } = String.Empty;

        /// <summary>
        /// ID of the persisted final assistant message, when one was written.
        /// </summary>
        public string? AssistantMessageId { get; set; } = null;

        /// <summary>
        /// Input tokens across the turn.
        /// </summary>
        public long TokensIn { get; set; } = 0;

        /// <summary>
        /// Output tokens across the turn.
        /// </summary>
        public long TokensOut { get; set; } = 0;

        /// <summary>
        /// Milliseconds from turn start to the first streamed token; -1 when none.
        /// </summary>
        public long TimeToFirstTokenMs { get; set; } = -1;

        /// <summary>
        /// Turn duration in milliseconds.
        /// </summary>
        public long DurationMs { get; set; } = 0;

        /// <summary>
        /// Model iterations.
        /// </summary>
        public int Iterations { get; set; } = 0;

        /// <summary>
        /// Tool calls requested.
        /// </summary>
        public int ToolCallCount { get; set; } = 0;

        /// <summary>
        /// Whether the fallback endpoint answered any iteration.
        /// </summary>
        public bool UsedFallback { get; set; } = false;

        /// <summary>
        /// Whether the history was compacted.
        /// </summary>
        public bool Compacted { get; set; } = false;
    }
}
