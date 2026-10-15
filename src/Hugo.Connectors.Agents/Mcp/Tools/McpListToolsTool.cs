namespace Hugo.Connectors.Agents.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Agents.Mcp.Payloads;

    /// <summary>
    /// <c>mcp_list_tools</c>: list the external MCP server's tools (after the resource's include and exclude filters) with
    /// descriptions, input schemas, and the effect Hugo derives from each tool's annotations, plus the server's own
    /// instructions fenced as untrusted third-party content. Always Read. The list is cached per resource and refreshed
    /// after <c>refreshIntervalMinutes</c>, or on demand with <c>refresh</c>. Thread-safe.
    /// </summary>
    public class McpListToolsTool : McpToolBase
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public static readonly string ToolName = "mcp_list_tools";

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string InputSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{"
            + "\"filter\":{\"type\":\"string\",\"description\":\"Case-insensitive text matched against tool names, titles, and descriptions. Omit to list every allowed tool.\"},"
            + "\"includeSchemas\":{\"type\":\"boolean\",\"description\":\"Include each tool's input schema. Default true; set false for a compact overview of a large server.\"},"
            + "\"refresh\":{\"type\":\"boolean\",\"description\":\"Fetch the list from the server now instead of using the cached list. Default false.\"}"
            + "}}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="options">Connector options.</param>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        public McpListToolsTool(McpConnectorOptions options) : base(BuildDescriptor(), options)
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, McpResourceOptions resource, McpRemoteSession session, ToolArguments arguments, TimeSpan limit, CancellationToken token)
        {
            string? filter = arguments.GetString("filter");
            bool includeSchemas = arguments.GetBool("includeSchemas", true);
            bool refresh = arguments.GetBool("refresh", false);

            McpToolCatalog catalog = await GetCatalogAsync(context, resource, session, refresh, limit, token).ConfigureAwait(false);
            List<McpRemoteTool> allowed = catalog.Tools.Where(t => resource.Filter.IsAllowed(t.Name)).ToList();
            List<McpRemoteTool> matched = String.IsNullOrWhiteSpace(filter) ? allowed : allowed.Where(t => Matches(t, filter.Trim())).ToList();
            List<McpRemoteTool> shown = matched.Take(context.Limits.MaxRows).ToList();

            McpToolListPayload payload = new McpToolListPayload
            {
                Server = session.Server.Display(),
                Endpoint = resource.EndpointName(),
                ProtocolVersion = String.IsNullOrEmpty(session.Server.ProtocolVersion) ? null : session.Server.ProtocolVersion,
                Instructions = String.IsNullOrWhiteSpace(session.Server.Instructions) ? null : TextClip.Clip(session.Server.Instructions, Options.MaxInstructionsChars),
                Discovered = catalog.Tools.Count,
                Allowed = allowed.Count,
                Matched = matched.Count,
                Shown = shown.Count,
                Incomplete = catalog.Incomplete,
                FetchedUtc = catalog.FetchedUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
            };

            StringBuilder sb = new StringBuilder();
            sb.Append("MCP server: ").Append(payload.Server).Append(" at ").Append(payload.Endpoint);
            if (payload.ProtocolVersion != null) sb.Append(" (MCP ").Append(payload.ProtocolVersion).Append(')');
            sb.Append('\n');
            sb.Append("Tools: ").Append(shown.Count.ToString(CultureInfo.InvariantCulture)).Append(" shown");
            if (!String.IsNullOrWhiteSpace(filter)) sb.Append(" of ").Append(matched.Count.ToString(CultureInfo.InvariantCulture)).Append(" matching \"").Append(TextClip.OneLine(filter)).Append('"');
            sb.Append("; ").Append(allowed.Count.ToString(CultureInfo.InvariantCulture)).Append(" allowed on this resource");
            int hidden = catalog.Tools.Count - allowed.Count;
            if (hidden > 0) sb.Append(", ").Append(hidden.ToString(CultureInfo.InvariantCulture)).Append(" hidden by includeTools or excludeTools");
            if (catalog.Incomplete) sb.Append("; the server's list was longer than the page cap, so it is incomplete");
            sb.Append(". Listed at ").Append(payload.FetchedUtc).Append(".\n");
            if (shown.Count < matched.Count) sb.Append("Only the first ").Append(shown.Count.ToString(CultureInfo.InvariantCulture)).Append(" are shown; narrow the list with filter.\n");
            sb.Append("Call one with mcp_call_tool {\"name\": \"<tool>\", \"arguments\": {...}}. Effect: R = Read, W = Write, D = Destructive (W and D may need approval).\n");

            if (payload.Instructions != null)
            {
                sb.Append('\n').Append("Server instructions:\n");
                sb.Append(UntrustedContent.Fence("instructions from MCP server " + payload.Server, payload.Instructions));
            }

            sb.Append('\n').Append("Tool names, descriptions, and schemas below come from the remote server.\n");
            foreach (McpRemoteTool tool in shown)
            {
                McpToolPayload item = new McpToolPayload
                {
                    Name = tool.Name,
                    Title = tool.Title,
                    Description = TextClip.Clip(UntrustedContent.Neutralize(tool.Description), Options.MaxDescriptionChars),
                    Effect = tool.Effect.ToString(),
                    ReadOnlyHint = tool.ReadOnlyHint,
                    DestructiveHint = tool.DestructiveHint,
                    IdempotentHint = tool.IdempotentHint,
                    OpenWorldHint = tool.OpenWorldHint
                };

                sb.Append("- ").Append(UntrustedContent.Neutralize(tool.Name)).Append(" [").Append(McpEffectClassifier.Letter(tool.Effect)).Append(']');
                if (!String.IsNullOrWhiteSpace(tool.Title)) sb.Append(' ').Append(TextClip.OneLine(UntrustedContent.Neutralize(tool.Title)));
                sb.Append('\n');
                if (item.Description.Length > 0) sb.Append("  ").Append(TextClip.OneLine(item.Description)).Append('\n');
                sb.Append("  hints: ").Append(Hints(tool)).Append('\n');

                if (tool.InputSchemaJson != null)
                {
                    if (!includeSchemas)
                    {
                        item.SchemaOmitted = true;
                    }
                    else if (tool.InputSchemaJson.Length > Options.MaxSchemaChars)
                    {
                        item.SchemaOmitted = true;
                        sb.Append("  input schema: omitted, ").Append(tool.InputSchemaJson.Length.ToString(CultureInfo.InvariantCulture)).Append(" chars is over the limit\n");
                    }
                    else
                    {
                        item.InputSchema = ParseSchema(tool.InputSchemaJson);
                        sb.Append("  input schema: ").Append(UntrustedContent.Neutralize(tool.InputSchemaJson)).Append('\n');
                    }
                }
                payload.Tools.Add(item);
            }
            if (!shown.Any()) sb.Append(allowed.Any() ? "(no tool matches the filter)\n" : "(no tools are available on this resource)\n");

            ToolResult result = new ToolResult();
            result.Text = OutputLimiter.Head(sb.ToString(), context.Limits.MaxBytes, context.Limits.MaxLines, result);
            result.StructuredJson = AgentsJson.Write(payload);
            result.Metrics["tools"] = shown.Count;
            result.Metrics["discovered"] = catalog.Tools.Count;
            return result;
        }

        #endregion

        #region Private-Methods

        private static ToolDescriptor BuildDescriptor()
        {
            return new ToolDescriptor
            {
                Name = ToolName,
                Title = "List external MCP tools",
                Description =
                    "List the tools of this external MCP server: name, description, input schema, and the effect Hugo assigns from the tool's annotations "
                    + "(readOnlyHint is Read, destructiveHint is Destructive, anything else is Write). Also returns the server's own instructions, fenced as untrusted "
                    + "third-party content. Call this first, then call a tool with mcp_call_tool using the exact name and an arguments object that matches its schema. "
                    + "Use filter to search a large server, includeSchemas=false for a compact overview, refresh=true after the server changed.",
                InputSchemaJson = InputSchema,
                BaseEffect = EffectEnum.Read,
                OutputDescription = "Text listing plus structured JSON {server, endpoint, protocolVersion, instructions, discovered, allowed, matched, shown, incomplete, fetchedUtc, tools[{name, title, description, effect, readOnlyHint, destructiveHint, idempotentHint, openWorldHint, inputSchema, schemaOmitted}]}."
            };
        }

        private static bool Matches(McpRemoteTool tool, string filter)
        {
            return tool.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || (tool.Title ?? String.Empty).Contains(filter, StringComparison.OrdinalIgnoreCase)
                || tool.Description.Contains(filter, StringComparison.OrdinalIgnoreCase);
        }

        private static string Hints(McpRemoteTool tool)
        {
            List<string> parts = new List<string>();
            if (tool.ReadOnlyHint != null) parts.Add("readOnlyHint=" + (tool.ReadOnlyHint.Value ? "true" : "false"));
            if (tool.DestructiveHint != null) parts.Add("destructiveHint=" + (tool.DestructiveHint.Value ? "true" : "false"));
            if (tool.IdempotentHint != null) parts.Add("idempotentHint=" + (tool.IdempotentHint.Value ? "true" : "false"));
            if (tool.OpenWorldHint != null) parts.Add("openWorldHint=" + (tool.OpenWorldHint.Value ? "true" : "false"));
            string text = parts.Any() ? String.Join(", ", parts) : "none";
            return text + "; effect " + tool.Effect.ToString();
        }

        private static JsonElement? ParseSchema(string json)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    return doc.RootElement.Clone();
                }
            }
            catch (JsonException)
            {
                return null;
            }
        }

        #endregion
    }
}
