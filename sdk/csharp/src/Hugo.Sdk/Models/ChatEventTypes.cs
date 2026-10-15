namespace Hugo.Sdk.Models
{
    /// <summary>
    /// Event types of a streamed chat turn (plan section 13.3 and 11.4). These are protocol names, not settings.
    /// </summary>
    public static class ChatEventTypes
    {
        #region Public-Members

        /// <summary>
        /// The turn started.
        /// </summary>
        public const string TurnStarted = "turn.started";

        /// <summary>
        /// A fragment of the assistant's answer.
        /// </summary>
        public const string AssistantDelta = "assistant.delta";

        /// <summary>
        /// A fragment of the model's reasoning (from think blocks).
        /// </summary>
        public const string AssistantThinking = "assistant.thinking";

        /// <summary>
        /// The model proposed a tool call.
        /// </summary>
        public const string ToolProposed = "tool.proposed";

        /// <summary>
        /// A tool call started executing.
        /// </summary>
        public const string ToolStarted = "tool.started";

        /// <summary>
        /// A tool call completed.
        /// </summary>
        public const string ToolCompleted = "tool.completed";

        /// <summary>
        /// A tool call needs approval; the turn waits.
        /// </summary>
        public const string ApprovalRequired = "approval.required";

        /// <summary>
        /// A pending approval was decided.
        /// </summary>
        public const string ApprovalResolved = "approval.resolved";

        /// <summary>
        /// The turn completed.
        /// </summary>
        public const string TurnCompleted = "turn.completed";

        /// <summary>
        /// The turn failed.
        /// </summary>
        public const string Error = "error";

        /// <summary>
        /// The server closes the chat stream (data: conversationId, turnId, finishReason, title). Always the last event.
        /// </summary>
        public const string StreamClosed = "stream.closed";

        #endregion
    }
}
