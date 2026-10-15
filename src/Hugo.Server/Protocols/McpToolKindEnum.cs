namespace Hugo.Server.Protocols
{
    /// <summary>
    /// Kind of an MCP tool, which decides how it is listed and dispatched.
    /// </summary>
    public enum McpToolKindEnum
    {
        /// <summary>
        /// A connector tool (family_operation) run through the tool invocation pipeline.
        /// </summary>
        Connector,

        /// <summary>
        /// A Hugo meta tool (hugo_whoami, hugo_await_approval, and so on).
        /// </summary>
        Meta,

        /// <summary>
        /// ask_agent_smith: a task delegated to Hugo's own agent loop.
        /// </summary>
        Delegation,

        /// <summary>
        /// A management tool (hugo_admin_*), listed only with expose_management_tools and an Admin-capable principal.
        /// </summary>
        Management
    }
}
