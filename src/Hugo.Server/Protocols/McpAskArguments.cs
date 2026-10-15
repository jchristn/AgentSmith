namespace Hugo.Server.Protocols
{
    /// <summary>
    /// Arguments of ask_agent_smith.
    /// </summary>
    public class McpAskArguments
    {
        /// <summary>
        /// The task or question.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Conversation to continue, or null to start one.
        /// </summary>
        public string? ConversationId { get; set; } = null;
    }
}
