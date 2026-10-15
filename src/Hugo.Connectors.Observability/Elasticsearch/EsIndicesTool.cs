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
    /// <c>es_indices</c>: list allowed indexes with health, document counts, and sizes. Read. Thread-safe.
    /// </summary>
    public class EsIndicesTool : EsToolBase
    {
        #region Public-Members

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string InputSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{"
            + "\"pattern\":{\"type\":\"string\",\"description\":\"Index pattern to list, for example logs-*. Default *. Only indexes inside the resource's allowed patterns are shown.\"}"
            + "}}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public EsIndicesTool() : base(new ToolDescriptor
        {
            Name = "es_indices",
            Title = "List Elasticsearch indexes",
            Description = "List the indexes you may query, with health, status, document count, size, and shard counts, sorted by name. Use it to find index names and patterns before searching.",
            InputSchemaJson = InputSchema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "Table of index, health, status, docs, bytes, pri, rep plus structured JSON {count, omitted, indices[]}."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token)
        {
            ElasticsearchSettings settings = ReadSettings(context);
            string pattern = EsIndexGuard.RequireSyntax(String.IsNullOrWhiteSpace(arguments.GetString("pattern")) ? "*" : arguments.GetString("pattern")!.Trim());
            ObservabilityTransport transport = Transport(context.Resource, settings);
            List<KeyValuePair<string, string?>> q = new List<KeyValuePair<string, string?>>
            {
                new KeyValuePair<string, string?>("format", "json"),
                new KeyValuePair<string, string?>("bytes", "b"),
                new KeyValuePair<string, string?>("h", "health,status,index,docs.count,store.size,pri,rep")
            };
            string json = await transport.GetAsync("_cat/indices/" + pattern, q, token).ConfigureAwait(false);
            List<EsCatIndex> all = ObservabilityJson.DeserializeUpstream<List<EsCatIndex>>(json, transport.Provider);
            List<EsCatIndex> allowed = all
                .Where(i => EsIndexGuard.IsIndexAllowed(i.Index, settings.AllowedIndexPatterns))
                .OrderBy(i => i.Index, StringComparer.Ordinal)
                .ToList();
            int cap = Math.Max(1, context.Limits.MaxRows);
            List<EsCatIndex> shown = allowed.Take(cap).ToList();
            int omitted = allowed.Count - shown.Count;

            StringBuilder sb = new StringBuilder();
            sb.Append(allowed.Count.ToString(CultureInfo.InvariantCulture)).Append(" index(es)");
            if (omitted > 0) sb.Append(", showing ").Append(shown.Count.ToString(CultureInfo.InvariantCulture));
            sb.Append('\n');
            if (shown.Count > 0)
            {
                List<IList<string?>> rows = shown.Select(i => (IList<string?>)new List<string?> { i.Index, i.Health, i.Status, i.DocsCount, i.StoreSize, i.Pri, i.Rep }).ToList();
                sb.Append(TextTable.Render(new List<string> { "index", "health", "status", "docs", "bytes", "pri", "rep" }, rows));
            }
            string structured = ObservabilityJson.Serialize(new Dictionary<string, object>
            {
                { "count", allowed.Count },
                { "omitted", omitted },
                { "indices", shown }
            });
            ToolResult result = Finish(sb.ToString(), structured, context.Limits, omitted > 0);
            result.Metrics["indices"] = allowed.Count;
            return result;
        }

        #endregion
    }
}
