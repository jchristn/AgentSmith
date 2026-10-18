namespace Hugo.Sdk.Models
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A conversation with an agent profile (preview, verified when the Phase 7 conversation routes land).
    /// </summary>
    public class Conversation
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>Whether the server has an active turn, even if its stream is disconnected.</summary>
        public bool TurnInProgress { get; set; } = false;

        /// <summary>
        /// Tenant Identifier.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// User Identifier.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Credential Identifier.
        /// </summary>
        public string? CredentialId { get; set; } = null;

        /// <summary>
        /// Agent profile Identifier.
        /// </summary>
        public string? AgentProfileId { get; set; } = null;

        /// <summary>
        /// Title.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Dashboard, A2A, OpenAI, Mcp, or Rest.
        /// </summary>
        public string? Source { get; set; } = null;

        /// <summary>
        /// A2A context Identifier.
        /// </summary>
        [JsonPropertyName("a2aContextId")]
        public string? A2AContextId { get; set; } = null;

        /// <summary>
        /// Message count.
        /// </summary>
        public int MessageCount { get; set; } = 0;

        /// <summary>
        /// Last message UTC.
        /// </summary>
        public DateTime? LastMessageUtc { get; set; } = null;

        /// <summary>
        /// Total input tokens.
        /// </summary>
        public long TotalInputTokens { get; set; } = 0;

        /// <summary>
        /// Total output tokens.
        /// </summary>
        public long TotalOutputTokens { get; set; } = 0;

        /// <summary>
        /// External key (for example an OpenAI-compatible session key), when the conversation was started with one.
        /// </summary>
        public string? ExternalKey { get; set; } = null;

        /// <summary>
        /// Creation timestamp, UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update timestamp, UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
