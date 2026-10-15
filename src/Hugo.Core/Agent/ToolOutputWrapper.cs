namespace Hugo.Core.Agent
{
    using System;
    using System.Text;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Wraps tool output as data before it reaches the model (P6.9):
    /// <c>&lt;tool_output resource="..." tool="..." truncated="..."&gt;...&lt;/tool_output&gt;</c>. Attribute values are
    /// escaped, and any closing tag inside the output is neutralized so tool output cannot end the data block
    /// early and pose as instructions. Thread-safe (stateless).
    /// </summary>
    public static class ToolOutputWrapper
    {
        #region Public-Methods

        /// <summary>
        /// Wrap a tool result.
        /// </summary>
        /// <param name="resource">Resource alias, or null.</param>
        /// <param name="tool">Tool name.</param>
        /// <param name="result">Result.</param>
        /// <returns>Wrapped text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when tool or result is null.</exception>
        public static string Wrap(string? resource, string tool, ToolResult result)
        {
            ArgumentNullException.ThrowIfNull(tool);
            ArgumentNullException.ThrowIfNull(result);
            StringBuilder sb = new StringBuilder();
            sb.Append("<tool_output resource=\"").Append(Attribute(resource ?? String.Empty)).Append('"');
            sb.Append(" tool=\"").Append(Attribute(tool)).Append('"');
            sb.Append(" truncated=\"").Append(result.Truncated ? "true" : "false").Append('"');
            if (result.IsError)
            {
                sb.Append(" error=\"").Append(Attribute(String.IsNullOrEmpty(result.ErrorCode) ? "error" : result.ErrorCode)).Append('"');
            }
            sb.Append(">\n");
            sb.Append(Neutralize(result.Text));
            sb.Append("\n</tool_output>");
            return sb.ToString();
        }

        /// <summary>
        /// Neutralize tool_output tags inside text.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>Safe text.</returns>
        public static string Neutralize(string? text)
        {
            if (String.IsNullOrEmpty(text)) return String.Empty;
            return text
                .Replace("</tool_output", "&lt;/tool_output", StringComparison.OrdinalIgnoreCase)
                .Replace("<tool_output", "&lt;tool_output", StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region Private-Methods

        private static string Attribute(string value)
        {
            return value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\n", " ").Replace("\r", " ");
        }

        #endregion
    }
}
