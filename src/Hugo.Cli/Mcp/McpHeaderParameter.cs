namespace Hugo.Cli.Mcp
{
    using System;

    /// <summary>
    /// A top-level tool parameter annotated with <c>x-mcp-header</c>, mirrored into an <c>Mcp-Param-{Name}</c>
    /// header on <c>tools/call</c> under the stateless revision.
    /// </summary>
    public class McpHeaderParameter
    {
        /// <summary>
        /// Name of the argument in <c>params.arguments</c>.
        /// </summary>
        public string PropertyName { get; set; } = String.Empty;

        /// <summary>
        /// Header name portion (the header is <c>Mcp-Param-{HeaderName}</c>).
        /// </summary>
        public string HeaderName { get; set; } = String.Empty;

        /// <summary>
        /// Declared schema type, or null.
        /// </summary>
        public string? Type { get; set; } = null;
    }
}
