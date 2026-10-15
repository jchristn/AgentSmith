namespace Hugo.Connectors.Agents.Mcp
{
    using System;

    /// <summary>
    /// A JSON-RPC error returned by an external MCP server (for example <c>-32602</c> for invalid params or an unknown
    /// tool). The message is the remote server's text (third-party content).
    /// </summary>
    public class McpRemoteException : Exception
    {
        /// <summary>
        /// JSON-RPC error code.
        /// </summary>
        public int RpcCode { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="rpcCode">JSON-RPC error code.</param>
        /// <param name="message">Remote error message.</param>
        public McpRemoteException(int rpcCode, string message) : base(message ?? String.Empty)
        {
            RpcCode = rpcCode;
        }
    }
}
