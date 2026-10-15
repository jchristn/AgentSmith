namespace Hugo.Server.Protocols.OpenAI
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Hugo extension on chat completion responses (clients that do not know it ignore it). The conversation identifier
    /// is also returned in the <c>x-hugo-conversation</c> response header.
    /// </summary>
    public class OpenAIHugoInfo
    {
        /// <summary>
        /// Conversation the turn ran in; send it back as <c>x-hugo-conversation</c> to continue.
        /// </summary>
        [JsonPropertyName("conversation_id")]
        public string ConversationId { get; set; } = String.Empty;

        /// <summary>
        /// Turn identifier.
        /// </summary>
        [JsonPropertyName("turn_id")]
        public string TurnId { get; set; } = String.Empty;

        /// <summary>
        /// Hugo finish reason: completed, max_iterations, tool_call_limit, approval_pending, cancelled, or error.
        /// </summary>
        [JsonPropertyName("finish_reason")]
        public string FinishReason { get; set; } = "completed";

        /// <summary>
        /// Pending approval, when the turn ended waiting for one.
        /// </summary>
        [JsonPropertyName("pending_approval_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? PendingApprovalId { get; set; } = null;
    }
}
