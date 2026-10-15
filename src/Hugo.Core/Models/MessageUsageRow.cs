namespace Hugo.Core.Models
{
    using System;

    /// <summary>
    /// Token accounting of one assistant or summary message, read for analytics (P6.17). Not thread-safe.
    /// </summary>
    public class MessageUsageRow
    {
        /// <summary>
        /// Creation time (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Model endpoint that produced the message, or null.
        /// </summary>
        public string? ModelEndpointId { get; set; } = null;

        /// <summary>
        /// Input tokens.
        /// </summary>
        public long InputTokens { get; set; } = 0;

        /// <summary>
        /// Output tokens.
        /// </summary>
        public long OutputTokens { get; set; } = 0;

        /// <summary>
        /// Turn time to first token in milliseconds (final assistant messages only).
        /// </summary>
        public long? TimeToFirstTokenMs { get; set; } = null;

        /// <summary>
        /// Turn duration in milliseconds (final assistant messages only).
        /// </summary>
        public long? DurationMs { get; set; } = null;

        /// <summary>
        /// Finish reason; set only on the final assistant message of a turn, so a non-null value marks a turn.
        /// </summary>
        public string? FinishReason { get; set; } = null;
    }
}
