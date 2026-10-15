namespace Hugo.Core.Enums
{
    /// <summary>
    /// Surface a tool call arrived on.
    /// </summary>
    public enum InvocationSurfaceEnum
    {
        /// <summary>
        /// Dashboard chat (Hugo's own loop).
        /// </summary>
        Chat = 0,

        /// <summary>
        /// MCP tools/call or ask_agent_smith.
        /// </summary>
        Mcp = 1,

        /// <summary>
        /// A2A task (Hugo's own loop).
        /// </summary>
        A2A = 2,

        /// <summary>
        /// OpenAI-compatible chat completions (Hugo's own loop).
        /// </summary>
        OpenAI = 3,

        /// <summary>
        /// REST direct invoke or dry run.
        /// </summary>
        Rest = 4
    }
}
