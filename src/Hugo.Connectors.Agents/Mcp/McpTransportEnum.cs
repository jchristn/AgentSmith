namespace Hugo.Connectors.Agents.Mcp
{
    /// <summary>
    /// Transport used to reach an external MCP server.
    /// </summary>
    public enum McpTransportEnum
    {
        /// <summary>
        /// MCP Streamable HTTP (one endpoint for POST and GET, with <c>Mcp-Session-Id</c>).
        /// </summary>
        StreamableHttp = 0,

        /// <summary>
        /// A child process speaking MCP over standard input and output. Allowed only when the resource and the server
        /// both allow it.
        /// </summary>
        Stdio = 1
    }
}
