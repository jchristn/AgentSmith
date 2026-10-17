namespace Hugo.Core.Responses
{
    using System;

    /// <summary>Standalone chat StandaloneChatMessage contract. Contains no credential or tool details.</summary>
    public class StandaloneChatMessage
    {
        /// <summary>Stable message identifier.</summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>This visitor's feedback on assistant messages, otherwise null.</summary>
        public StandaloneChatFeedback? Feedback { get; set; } = null;

        /// <summary>user or assistant only.</summary>
        public string Role { get; set; } = String.Empty;

        /// <summary>Safe message text without thinking or tool records.</summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>Timestamp UTC.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Input usage.</summary>
        public long InputTokens { get; set; } = 0;

        /// <summary>Output usage.</summary>
        public long OutputTokens { get; set; } = 0;

    }
}
