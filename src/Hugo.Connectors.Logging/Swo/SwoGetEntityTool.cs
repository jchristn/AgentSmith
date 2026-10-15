namespace Hugo.Connectors.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>swo_get_entity</c>: one SolarWinds Observability entity with tags and attributes. Read. Thread-safe.
    /// </summary>
    public class SwoGetEntityTool : LoggingToolBase
    {
        #region Public-Members

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string InputSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"id\"],\"properties\":{"
            + "\"id\":{\"type\":\"string\",\"description\":\"Entity ID from swo_list_entities.\"}"
            + "}}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="options">Connector options.</param>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        public SwoGetEntityTool(LoggingConnectorOptions options)
            : base(new ToolDescriptor
            {
                Name = "swo_get_entity",
                Title = "Get SolarWinds Observability entity",
                Description = "Get one entity by ID: type, name, health state, maintenance flag, created, updated, and last-seen times, tags, and type-specific attributes.",
                InputSchemaJson = InputSchema,
                BaseEffect = EffectEnum.Read,
                OutputDescription = "Key-value text plus the entity as structured JSON."
            }, options)
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token)
        {
            string id = arguments.GetRequiredString("id");
            SwoClient client = SwoClient.FromResource(context.Resource);
            SwoEntity e = await client.GetEntityAsync(id, token).ConfigureAwait(false);

            StringBuilder sb = new StringBuilder();
            sb.Append("id: ").Append(e.Id).Append('\n');
            sb.Append("type: ").Append(e.Type).Append('\n');
            sb.Append("name: ").Append(e.Name).Append('\n');
            if (!String.IsNullOrEmpty(e.DisplayName) && !String.Equals(e.DisplayName, e.Name, StringComparison.Ordinal)) sb.Append("displayName: ").Append(e.DisplayName).Append('\n');
            sb.Append("health: ").Append(e.HealthState == null ? "unknown" : e.HealthState.State).Append('\n');
            sb.Append("inMaintenance: ").Append(e.InMaintenance ? "yes" : "no").Append('\n');
            sb.Append("created: ").Append(ListToolHelper.FormatTime(e.CreatedTime)).Append('\n');
            sb.Append("updated: ").Append(ListToolHelper.FormatTime(e.UpdatedTime)).Append('\n');
            sb.Append("lastSeen: ").Append(ListToolHelper.FormatTime(e.LastSeenTime)).Append('\n');
            if (e.Tags != null && e.Tags.Any())
            {
                sb.Append("tags:\n");
                foreach (KeyValuePair<string, string> tag in e.Tags.OrderBy(t => t.Key, StringComparer.Ordinal)) sb.Append("  ").Append(tag.Key).Append(": ").Append(tag.Value).Append('\n');
            }
            if (e.Attributes != null && e.Attributes.Any())
            {
                sb.Append("attributes:\n");
                foreach (KeyValuePair<string, object?> attr in e.Attributes.OrderBy(a => a.Key, StringComparer.Ordinal))
                    sb.Append("  ").Append(attr.Key).Append(": ").Append(attr.Value == null ? "null" : attr.Value.ToString()).Append('\n');
            }

            ToolResult result = new ToolResult();
            result.Text = OutputLimiter.Head(sb.ToString(), context.Limits.MaxBytes, context.Limits.MaxLines, result);
            result.StructuredJson = LoggingJson.Serialize(e);
            return result;
        }

        #endregion
    }
}
