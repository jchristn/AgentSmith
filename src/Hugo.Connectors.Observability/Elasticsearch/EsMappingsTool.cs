namespace Hugo.Connectors.Observability
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>es_mappings</c>: list the fields and types of allowed indexes as flattened paths. Read. Thread-safe.
    /// </summary>
    public class EsMappingsTool : EsToolBase
    {
        #region Public-Members

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string InputSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"index\"],\"properties\":{"
            + "\"index\":{\"type\":\"string\",\"description\":\"Index name or pattern inside the resource's allowed index patterns. Use one index or a narrow pattern.\"},"
            + "\"field\":{\"type\":\"string\",\"description\":\"Only fields whose path contains this text (case-insensitive).\"}"
            + "}}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public EsMappingsTool() : base(new ToolDescriptor
        {
            Name = "es_mappings",
            Title = "Elasticsearch field mappings",
            Description = "List the fields of an index as flattened paths with their types (for example request.status: integer, message.keyword: keyword). "
                + "Check it before writing queries: term and aggregations need keyword or numeric fields; match works on text.",
            InputSchemaJson = InputSchema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "Per index, one 'path: type' line per field plus structured JSON {indices[{index, fields[{path, type}]}], omitted}."
        })
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Flatten a mapping into <c>path: type</c> entries, depth first and sorted by name.
        /// </summary>
        /// <param name="root">Root mapping.</param>
        /// <returns>Entries.</returns>
        public static List<KeyValuePair<string, string>> Flatten(EsFieldMapping? root)
        {
            List<KeyValuePair<string, string>> fields = new List<KeyValuePair<string, string>>();
            if (root != null) Walk(root.Properties, String.Empty, fields);
            return fields;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token)
        {
            ElasticsearchSettings settings = ReadSettings(context);
            string index = EsIndexGuard.Require(arguments.GetString("index"), settings.AllowedIndexPatterns);
            string? filter = arguments.GetString("field");
            ObservabilityTransport transport = Transport(context.Resource, settings);
            List<KeyValuePair<string, string?>> q = new List<KeyValuePair<string, string?>> { new KeyValuePair<string, string?>("expand_wildcards", "open") };
            string json = await transport.GetAsync(index + "/_mapping", q, token).ConfigureAwait(false);
            Dictionary<string, EsIndexMapping> mappings = ObservabilityJson.DeserializeUpstream<Dictionary<string, EsIndexMapping>>(json, transport.Provider);

            int cap = Math.Max(1, context.Limits.MaxRows);
            int used = 0;
            int omitted = 0;
            StringBuilder sb = new StringBuilder();
            List<Dictionary<string, object>> structured = new List<Dictionary<string, object>>();
            foreach (KeyValuePair<string, EsIndexMapping> entry in mappings.OrderBy(m => m.Key, StringComparer.Ordinal))
            {
                if (!EsIndexGuard.IsIndexAllowed(entry.Key, settings.AllowedIndexPatterns)) continue;
                List<KeyValuePair<string, string>> fields = Flatten(entry.Value.Mappings)
                    .Where(f => String.IsNullOrEmpty(filter) || f.Key.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                sb.Append(entry.Key).Append(" (").Append(fields.Count.ToString(CultureInfo.InvariantCulture)).Append(" field(s)):\n");
                List<Dictionary<string, string>> shown = new List<Dictionary<string, string>>();
                foreach (KeyValuePair<string, string> f in fields)
                {
                    if (used >= cap)
                    {
                        omitted++;
                        continue;
                    }
                    used++;
                    sb.Append("  ").Append(f.Key).Append(": ").Append(f.Value).Append('\n');
                    shown.Add(new Dictionary<string, string> { { "path", f.Key }, { "type", f.Value } });
                }
                structured.Add(new Dictionary<string, object> { { "index", entry.Key }, { "fields", shown } });
            }
            if (omitted > 0) sb.Append("[").Append(omitted.ToString(CultureInfo.InvariantCulture)).Append(" more field(s) not shown; use the field filter]\n");
            if (structured.Count == 0) sb.Append("No allowed index matched.\n");
            string structuredJson = ObservabilityJson.Serialize(new Dictionary<string, object> { { "indices", structured }, { "omitted", omitted } });
            ToolResult result = Finish(sb.ToString(), structuredJson, context.Limits, omitted > 0);
            result.Metrics["fields"] = used;
            return result;
        }

        #endregion

        #region Private-Methods

        private static void Walk(Dictionary<string, EsFieldMapping>? properties, string prefix, List<KeyValuePair<string, string>> fields)
        {
            if (properties == null) return;
            foreach (KeyValuePair<string, EsFieldMapping> p in properties.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                string path = prefix.Length == 0 ? p.Key : prefix + "." + p.Key;
                string type = p.Value.Type ?? (p.Value.Properties != null ? "object" : "unknown");
                fields.Add(new KeyValuePair<string, string>(path, type));
                if (p.Value.Fields != null)
                {
                    foreach (KeyValuePair<string, EsFieldMapping> mf in p.Value.Fields.OrderBy(k => k.Key, StringComparer.Ordinal))
                        fields.Add(new KeyValuePair<string, string>(path + "." + mf.Key, mf.Value.Type ?? "unknown"));
                }
                Walk(p.Value.Properties, path, fields);
            }
        }

        #endregion
    }
}
