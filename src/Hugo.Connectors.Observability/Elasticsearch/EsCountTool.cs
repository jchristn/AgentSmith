namespace Hugo.Connectors.Observability
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>es_count</c>: count documents matching a query in allowed indexes. Read. Thread-safe.
    /// </summary>
    public class EsCountTool : EsToolBase
    {
        #region Public-Members

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string InputSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"index\"],\"properties\":{"
            + "\"index\":{\"type\":\"string\",\"description\":\"Index name, pattern, or comma-separated list inside the resource's allowed index patterns.\"},"
            + "\"query\":{\"type\":\"object\",\"description\":\"Query DSL query object. Default match_all.\"}"
            + "}}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public EsCountTool() : base(new ToolDescriptor
        {
            Name = "es_count",
            Title = "Count Elasticsearch documents",
            Description = "Count documents that match a Query DSL query in allowed indexes, without returning them. Use it to size a problem before searching.",
            InputSchemaJson = InputSchema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "One line with the count plus structured JSON {index, count}."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token)
        {
            ElasticsearchSettings settings = ReadSettings(context);
            string index = EsIndexGuard.Require(arguments.GetString("index"), settings.AllowedIndexPatterns);
            string? query = RawJson(arguments, "query", "{", "a Query DSL object");
            string body = query == null ? "{}" : "{\"query\":" + query + "}";
            ObservabilityTransport transport = Transport(context.Resource, settings);
            List<KeyValuePair<string, string?>> q = new List<KeyValuePair<string, string?>> { new KeyValuePair<string, string?>("expand_wildcards", "open") };
            string json = await transport.PostJsonAsync(index + "/_count", q, body, token).ConfigureAwait(false);
            EsCountResponse response = ObservabilityJson.DeserializeUpstream<EsCountResponse>(json, transport.Provider);
            string text = response.Count.ToString(CultureInfo.InvariantCulture) + " document(s) match in " + index + ".\n";
            string structured = ObservabilityJson.Serialize(new Dictionary<string, object> { { "index", index }, { "count", response.Count } });
            ToolResult result = Finish(text, structured, context.Limits, false);
            result.Metrics["count"] = response.Count;
            return result;
        }

        #endregion
    }
}
