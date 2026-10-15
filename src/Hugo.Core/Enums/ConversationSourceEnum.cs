namespace Hugo.Core.Enums
{
    /// <summary>
    /// Surface that started a conversation (plan 7.5). Every surface that runs Hugo's own loop maps onto a conversation,
    /// so the history of each is visible in one place.
    /// </summary>
    public enum ConversationSourceEnum
    {
        /// <summary>
        /// Dashboard chat (or the REST conversation routes).
        /// </summary>
        Dashboard = 0,

        /// <summary>
        /// An A2A context.
        /// </summary>
        A2A = 1,

        /// <summary>
        /// An OpenAI-compatible chat completions session.
        /// </summary>
        OpenAI = 2,

        /// <summary>
        /// MCP <c>ask_agent_smith</c>.
        /// </summary>
        Mcp = 3,

        /// <summary>
        /// REST delegation (<c>POST /v1.0/api/agent-profiles/{id}/ask</c>).
        /// </summary>
        Rest = 4
    }
}
