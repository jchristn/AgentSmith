namespace Hugo.Server.Protocols
{
    using System;

    /// <summary>
    /// A tool failure reported to the MCP caller as an isError result with a stable code (never a transport error).
    /// </summary>
    public class McpToolErrorException : Exception
    {
        /// <summary>
        /// Stable error code (for example forbidden, not_found, invalid_argument).
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="code">Stable error code.</param>
        /// <param name="message">Message.</param>
        public McpToolErrorException(string code, string message) : base(message)
        {
            Code = String.IsNullOrEmpty(code) ? "error" : code;
        }
    }
}
