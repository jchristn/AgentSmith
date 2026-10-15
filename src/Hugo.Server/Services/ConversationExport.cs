namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Models;

    /// <summary>
    /// JSON export of a conversation (<c>GET /v1.0/api/conversations/{id}/export?format=json</c>). Not thread-safe.
    /// </summary>
    public class ConversationExport
    {
        /// <summary>
        /// Export format version.
        /// </summary>
        public int FormatVersion { get; set; } = 1;

        /// <summary>
        /// Export time (UTC).
        /// </summary>
        public DateTime ExportedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Conversation.
        /// </summary>
        public Conversation Conversation { get; set; } = new Conversation();

        /// <summary>
        /// Messages in order, compacted ones included.
        /// </summary>
        public List<ConversationMessage> Messages { get; set; } = new List<ConversationMessage>();
    }
}
