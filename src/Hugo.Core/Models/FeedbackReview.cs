namespace Hugo.Core.Models
{
    using System;

    /// <summary>Feedback and associated persisted request, response, and turn telemetry. Contains no visitor capability or reasoning. Not thread-safe.</summary>
    public class FeedbackReview
    {
        /// <summary>Feedback message identifier.</summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>Rated assistant message identifier.</summary>
        public string MessageId { get; set; } = String.Empty;

        /// <summary>Agent profile identifier.</summary>
        public string AgentProfileId { get; set; } = String.Empty;

        /// <summary>Agent display name.</summary>
        public string AgentName { get; set; } = String.Empty;

        /// <summary>Conversation identifier.</summary>
        public string ConversationId { get; set; } = String.Empty;

        /// <summary>Persisted turn identifier, when known.</summary>
        public string? TurnId { get; set; } = null;

        /// <summary>Positive or Negative, or null for comment-only feedback.</summary>
        public string? FeedbackRating { get; set; } = null;

        /// <summary>Written feedback, when supplied.</summary>
        public string? FeedbackComment { get; set; } = null;

        /// <summary>Most recent feedback timestamp UTC.</summary>
        public DateTime? FeedbackUpdatedUtc { get; set; } = null;

        /// <summary>User request associated with the rated response, or null when unavailable.</summary>
        public string? RequestText { get; set; } = null;

        /// <summary>Rated assistant response.</summary>
        public string ResponseText { get; set; } = String.Empty;

        /// <summary>Input tokens attributed to the rated message.</summary>
        public long InputTokens { get; set; } = 0;

        /// <summary>Output tokens attributed to the rated message.</summary>
        public long OutputTokens { get; set; } = 0;

        /// <summary>Persisted time to first token in milliseconds.</summary>
        public long? TimeToFirstTokenMs { get; set; } = null;

        /// <summary>Persisted turn duration in milliseconds.</summary>
        public long? DurationMs { get; set; } = null;

        /// <summary>Persisted turn finish reason.</summary>
        public string? FinishReason { get; set; } = null;

        /// <summary>Producing model endpoint identifier.</summary>
        public string? ModelEndpointId { get; set; } = null;

        /// <summary>Current model identifier of the producing endpoint, when still available.</summary>
        public string? ModelName { get; set; } = null;

        /// <summary>Conversation source interface.</summary>
        public string Source { get; set; } = String.Empty;

        /// <summary>Persisted assistant tool calls in the associated turn, or rated message when no turn exists.</summary>
        public long ToolCallCount { get; set; } = 0;

        /// <summary>Response creation timestamp UTC.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    }
}
