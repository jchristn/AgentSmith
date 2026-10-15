namespace Hugo.Connectors.Agents.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Agents.Mcp.Payloads;
    using Hugo.Connectors.Agents.Mcp.Wire;

    /// <summary>
    /// <c>mcp_list_resources</c>: list the resources (documents, files, records) the external MCP server offers. Always
    /// Read. Returns <c>not_supported</c> when the server does not offer resources. Thread-safe.
    /// </summary>
    public class McpListResourcesTool : McpToolBase
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public static readonly string ToolName = "mcp_list_resources";

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string InputSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{"
            + "\"filter\":{\"type\":\"string\",\"description\":\"Case-insensitive text matched against URIs, names, and descriptions. Omit to list everything.\"}"
            + "}}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="options">Connector options.</param>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        public McpListResourcesTool(McpConnectorOptions options) : base(BuildDescriptor(), options)
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, McpResourceOptions resource, McpRemoteSession session, ToolArguments arguments, TimeSpan limit, CancellationToken token)
        {
            if (!session.Server.HasResources)
                throw new ConnectorException(ConnectorErrorCodes.NotSupported, "The MCP server " + session.Server.Display() + " does not offer resources. Use mcp_list_tools instead.");
            string? filter = arguments.GetString("filter");

            List<McpWireResource> all = new List<McpWireResource>();
            string? cursor = null;
            bool incomplete = false;
            for (int page = 0; ; page++)
            {
                if (page >= Options.MaxListPages)
                {
                    incomplete = true;
                    break;
                }
                string json = await session.CallAsync("resources/list", McpRemoteSession.CursorParameters(cursor), limit, token).ConfigureAwait(false);
                McpWireListResourcesResult result = AgentsJson.Read<McpWireListResourcesResult>(json, "a resources/list result");
                all.AddRange((result.Resources ?? new List<McpWireResource>()).Where(r => r != null && !String.IsNullOrEmpty(r.Uri)));
                if (String.IsNullOrEmpty(result.NextCursor)) break;
                cursor = result.NextCursor;
            }

            List<McpWireResource> matched = String.IsNullOrWhiteSpace(filter) ? all : all.Where(r => Matches(r, filter.Trim())).ToList();
            List<McpWireResource> shown = matched.Take(context.Limits.MaxRows).ToList();
            McpResourceListPayload payload = new McpResourceListPayload
            {
                Server = session.Server.Display(),
                Discovered = all.Count,
                Shown = shown.Count,
                Incomplete = incomplete
            };

            StringBuilder sb = new StringBuilder();
            sb.Append("MCP server: ").Append(payload.Server).Append('\n');
            sb.Append("Resources: ").Append(shown.Count.ToString(CultureInfo.InvariantCulture)).Append(" shown of ").Append(matched.Count.ToString(CultureInfo.InvariantCulture));
            sb.Append(String.IsNullOrWhiteSpace(filter) ? String.Empty : " matching \"" + TextClip.OneLine(filter) + "\"");
            if (incomplete) sb.Append("; the server's list was longer than the page cap, so it is incomplete");
            sb.Append(". Read one with mcp_read_resource {\"uri\": \"...\"}. Names and descriptions come from the remote server.\n");
            foreach (McpWireResource r in shown)
            {
                McpResourcePayload item = new McpResourcePayload
                {
                    Uri = r.Uri ?? String.Empty,
                    Name = r.Name,
                    Title = r.Title,
                    Description = r.Description == null ? null : TextClip.Clip(UntrustedContent.Neutralize(r.Description), Options.MaxDescriptionChars),
                    MimeType = r.MimeType,
                    Size = r.Size
                };
                payload.Resources.Add(item);
                sb.Append("- ").Append(UntrustedContent.Neutralize(item.Uri));
                string label = TextClip.OneLine(UntrustedContent.Neutralize(r.Title ?? r.Name));
                if (label.Length > 0) sb.Append(" | ").Append(label);
                if (!String.IsNullOrEmpty(r.MimeType)) sb.Append(" | ").Append(r.MimeType);
                if (r.Size != null) sb.Append(" | ").Append(r.Size.Value.ToString(CultureInfo.InvariantCulture)).Append(" bytes");
                sb.Append('\n');
                if (!String.IsNullOrWhiteSpace(item.Description)) sb.Append("  ").Append(TextClip.OneLine(item.Description)).Append('\n');
            }
            if (!shown.Any()) sb.Append("(no resources)\n");

            ToolResult toolResult = new ToolResult();
            toolResult.Text = OutputLimiter.Head(sb.ToString(), context.Limits.MaxBytes, context.Limits.MaxLines, toolResult);
            toolResult.StructuredJson = AgentsJson.Write(payload);
            toolResult.Metrics["resources"] = shown.Count;
            return toolResult;
        }

        #endregion

        #region Private-Methods

        private static ToolDescriptor BuildDescriptor()
        {
            return new ToolDescriptor
            {
                Name = ToolName,
                Title = "List external MCP resources",
                Description =
                    "List the resources (documents, files, records) this external MCP server offers, with URI, name, MIME type, size, and description. "
                    + "Read one with mcp_read_resource. Returns not_supported when the server has no resources.",
                InputSchemaJson = InputSchema,
                BaseEffect = EffectEnum.Read,
                OutputDescription = "Text listing plus structured JSON {server, discovered, shown, incomplete, resources[{uri, name, title, description, mimeType, size}]}."
            };
        }

        private static bool Matches(McpWireResource r, string filter)
        {
            return (r.Uri ?? String.Empty).Contains(filter, StringComparison.OrdinalIgnoreCase)
                || (r.Name ?? String.Empty).Contains(filter, StringComparison.OrdinalIgnoreCase)
                || (r.Title ?? String.Empty).Contains(filter, StringComparison.OrdinalIgnoreCase)
                || (r.Description ?? String.Empty).Contains(filter, StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
