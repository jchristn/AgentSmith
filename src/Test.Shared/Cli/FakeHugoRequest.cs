namespace Test.Shared.Cli
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A request the fake Hugo server received.
    /// </summary>
    public class FakeHugoRequest
    {
        /// <summary>
        /// HTTP method.
        /// </summary>
        public string Method { get; set; } = String.Empty;

        /// <summary>
        /// Raw path as sent (percent-encoding kept).
        /// </summary>
        public string RawPath { get; set; } = String.Empty;

        /// <summary>
        /// Headers (case-insensitive names).
        /// </summary>
        public Dictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Body text.
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// JSON-RPC method of an MCP POST, or null.
        /// </summary>
        public string? RpcMethod { get; set; } = null;

        /// <summary>
        /// HTTP status answered.
        /// </summary>
        public int Status { get; set; } = 0;

        /// <summary>
        /// A header value, or null.
        /// </summary>
        /// <param name="name">Header name.</param>
        /// <returns>Value or null.</returns>
        public string? Header(string name)
        {
            return Headers.TryGetValue(name, out string? value) ? value : null;
        }
    }
}
