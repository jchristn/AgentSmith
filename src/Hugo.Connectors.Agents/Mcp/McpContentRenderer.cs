namespace Hugo.Connectors.Agents.Mcp
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using Hugo.Connectors.Agents.Mcp.Payloads;
    using Hugo.Connectors.Agents.Mcp.Wire;

    /// <summary>
    /// Renders MCP content blocks and resource contents as text for the calling model, with matching structured entries.
    /// Text is clipped and fence tags are neutralized; binary data (images, audio, blobs) is summarized and never
    /// included. Thread-safe.
    /// </summary>
    public static class McpContentRenderer
    {
        #region Public-Methods

        /// <summary>
        /// Render tool result content blocks.
        /// </summary>
        /// <param name="blocks">Blocks. Null is treated as empty.</param>
        /// <param name="into">Receives one structured entry per block.</param>
        /// <param name="maxTextChars">Most characters kept per text block.</param>
        /// <returns>Text, one block after another.</returns>
        /// <exception cref="ArgumentNullException">Thrown when into is null.</exception>
        public static string RenderBlocks(IEnumerable<McpWireContent>? blocks, List<McpContentPayload> into, int maxTextChars)
        {
            ArgumentNullException.ThrowIfNull(into);
            StringBuilder sb = new StringBuilder();
            foreach (McpWireContent block in blocks ?? new List<McpWireContent>())
            {
                if (block == null) continue;
                string type = String.IsNullOrEmpty(block.Type) ? "text" : block.Type;
                McpContentPayload p = new McpContentPayload { Type = type, MimeType = block.MimeType, Uri = block.Uri };
                if (type == "text")
                {
                    p.Text = TextClip.Clip(UntrustedContent.Neutralize(block.Text), maxTextChars);
                    sb.Append(p.Text);
                    if (!p.Text.EndsWith('\n')) sb.Append('\n');
                }
                else if (type == "image" || type == "audio")
                {
                    p.Bytes = Base64Bytes(block.Data);
                    sb.Append('[').Append(type).Append(' ').Append(block.MimeType ?? "unknown type").Append(", about ")
                        .Append((p.Bytes ?? 0).ToString(CultureInfo.InvariantCulture)).Append(" bytes, not shown]\n");
                }
                else if (type == "resource_link")
                {
                    p.Text = block.Name;
                    sb.Append("[resource link: ").Append(block.Uri ?? "(no uri)");
                    if (!String.IsNullOrEmpty(block.Name)) sb.Append(" (").Append(TextClip.OneLine(UntrustedContent.Neutralize(block.Name))).Append(')');
                    sb.Append("; read it with mcp_read_resource]\n");
                }
                else if (type == "resource" && block.Resource != null)
                {
                    p.Uri = block.Resource.Uri;
                    p.MimeType = block.Resource.MimeType;
                    AppendContents(sb, p, block.Resource, maxTextChars);
                }
                else
                {
                    sb.Append("[content of type ").Append(TextClip.OneLine(type)).Append(" not shown]\n");
                }
                into.Add(p);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Render resource contents.
        /// </summary>
        /// <param name="contents">Contents. Null is treated as empty.</param>
        /// <param name="into">Receives one structured entry per item.</param>
        /// <param name="maxTextChars">Most characters kept per text item.</param>
        /// <returns>Text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when into is null.</exception>
        public static string RenderContents(IEnumerable<McpWireResourceContents>? contents, List<McpContentPayload> into, int maxTextChars)
        {
            ArgumentNullException.ThrowIfNull(into);
            StringBuilder sb = new StringBuilder();
            foreach (McpWireResourceContents item in contents ?? new List<McpWireResourceContents>())
            {
                if (item == null) continue;
                McpContentPayload p = new McpContentPayload { Type = item.Blob != null ? "blob" : "text", Uri = item.Uri, MimeType = item.MimeType };
                AppendContents(sb, p, item, maxTextChars);
                into.Add(p);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Approximate decoded size of base64 text.
        /// </summary>
        /// <param name="base64">Base64 text, or null.</param>
        /// <returns>Bytes, or null when absent.</returns>
        public static long? Base64Bytes(string? base64)
        {
            if (base64 == null) return null;
            int padding = base64.EndsWith("==", StringComparison.Ordinal) ? 2 : base64.EndsWith('=') ? 1 : 0;
            return Math.Max(0, (long)base64.Length * 3 / 4 - padding);
        }

        #endregion

        #region Private-Methods

        private static void AppendContents(StringBuilder sb, McpContentPayload p, McpWireResourceContents item, int maxTextChars)
        {
            if (item.Blob != null)
            {
                p.Type = "blob";
                p.Bytes = Base64Bytes(item.Blob);
                sb.Append("[binary resource ").Append(item.Uri ?? "(no uri)").Append(", ").Append(item.MimeType ?? "unknown type").Append(", about ")
                    .Append((p.Bytes ?? 0).ToString(CultureInfo.InvariantCulture)).Append(" bytes, not shown]\n");
                return;
            }
            p.Text = TextClip.Clip(UntrustedContent.Neutralize(item.Text), maxTextChars);
            if (!String.IsNullOrEmpty(item.Uri)) sb.Append("[resource ").Append(item.Uri).Append(String.IsNullOrEmpty(item.MimeType) ? String.Empty : ", " + item.MimeType).Append("]\n");
            sb.Append(p.Text);
            if (!p.Text.EndsWith('\n')) sb.Append('\n');
        }

        #endregion
    }
}
