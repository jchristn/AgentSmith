namespace Hugo.Core.Responses
{
    using System;

    /// <summary>Standalone chat StandaloneChatTurn contract. Contains no credential or tool details.</summary>
    public class StandaloneChatTurn
    {
        /// <summary>Stable final assistant message identifier, null when no visible message was persisted.</summary>
        public string? MessageId { get; set; } = null;

        /// <summary>Conversation identifier.</summary>
        public string ConversationId { get; set; } = String.Empty;

        /// <summary>Assistant final text only.</summary>
        public string Text { get; set; } = String.Empty;

        /// <summary>Turn finish status.</summary>
        public string Status { get; set; } = String.Empty;

        /// <summary>Input usage.</summary>
        public long TokensIn { get; set; } = 0;

        /// <summary>Output usage.</summary>
        public long TokensOut { get; set; } = 0;

    }
}
