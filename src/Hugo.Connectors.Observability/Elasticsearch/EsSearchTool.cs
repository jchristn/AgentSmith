namespace Hugo.Connectors.Observability
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>es_search</c>: run a Query DSL search with a capped size against allowed indexes. Read. Thread-safe.
    /// </summary>
    public class EsSearchTool : EsToolBase
    {
        #region Public-Members

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string InputSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"index\"],\"properties\":{"
            + "\"index\":{\"type\":\"string\",\"description\":\"Index name, pattern, or comma-separated list inside the resource's allowed index patterns, for example logs-2026.10.*.\"},"
            + "\"query\":{\"type\":\"object\",\"description\":\"Query DSL query object, for example {\\\"bool\\\":{\\\"filter\\\":[{\\\"range\\\":{\\\"@timestamp\\\":{\\\"gte\\\":\\\"now-15m\\\"}}}]}}. Default match_all.\"},"
            + "\"size\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":10000,\"description\":\"Hits to return. Default 10. Capped by the resource's maxHits. Use 0 with aggs for counts only.\"},"
            + "\"from\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":10000,\"description\":\"Offset of the first hit. from + size must not exceed 10000; use search_after for deeper paging.\"},"
            + "\"sort\":{\"description\":\"Sort clause, for example [{\\\"@timestamp\\\":\\\"desc\\\"}].\"},"
            + "\"_source\":{\"description\":\"true, false, or a list of fields to return, for example [\\\"@timestamp\\\",\\\"message\\\"]. Prefer a field list.\"},"
            + "\"aggs\":{\"type\":\"object\",\"description\":\"Aggregations, for example {\\\"by_level\\\":{\\\"terms\\\":{\\\"field\\\":\\\"level\\\"}}}.\"},"
            + "\"search_after\":{\"type\":\"array\",\"description\":\"Sort values of the last hit of the previous page, for deep paging with a sort.\"},"
            + "\"track_total_hits\":{\"description\":\"true for an exact total, or a number to count up to. Default server behavior (10000).\"}"
            + "}}";

        /// <summary>
        /// Longest rendered document source in the text output; longer sources end with <c>...</c>. Minimum 100, maximum
        /// 1048576, default 2000.
        /// </summary>
        public int MaxSourceChars
        {
            get
            {
                return _MaxSourceChars;
            }
            set
            {
                _MaxSourceChars = Math.Clamp(value, 100, 1048576);
            }
        }

        /// <summary>
        /// Hits returned when the caller omits <c>size</c>. Minimum 0, maximum 10000, default 10.
        /// </summary>
        public int DefaultSize
        {
            get
            {
                return _DefaultSize;
            }
            set
            {
                _DefaultSize = Math.Clamp(value, 0, 10000);
            }
        }

        #endregion

        #region Private-Members

        private static readonly HashSet<string> _Allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "index", "query", "size", "from", "sort", "_source", "aggs", "search_after", "track_total_hits"
        };

        private int _MaxSourceChars = 2000;
        private int _DefaultSize = 10;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public EsSearchTool() : base(new ToolDescriptor
        {
            Name = "es_search",
            Title = "Search Elasticsearch",
            Description = "Search one or more allowed indexes with a Query DSL query and return hits (index, id, score, source) and aggregations. "
                + "Always set size (default 10, capped by the resource's maxHits), filter by time with a range query, and request only the fields you need with _source. "
                + "Use aggs with size 0 for counts and breakdowns instead of reading many documents.",
            InputSchemaJson = InputSchema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "Summary line, one line per hit, and aggregations as JSON, plus structured JSON {index, total, relation, took, returned, filtered, hits[{index, id, score, source, sort}], aggregations}."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token)
        {
            string? unknown = arguments.Names().FirstOrDefault(n => !_Allowed.Contains(n));
            if (unknown != null)
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Argument '" + unknown + "' is not supported. Allowed: " + String.Join(", ", _Allowed) + ".");

            ElasticsearchSettings settings = ReadSettings(context);
            string index = EsIndexGuard.Require(arguments.GetString("index"), settings.AllowedIndexPatterns);
            string? query = RawJson(arguments, "query", "{", "a Query DSL object");
            string? sort = RawJson(arguments, "sort", "[{\"", "an array, object, or string");
            string? source = RawJson(arguments, "_source", "tf[\"{", "true, false, a field name, or a list of fields");
            string? aggs = RawJson(arguments, "aggs", "{", "an aggregations object");
            string? searchAfter = RawJson(arguments, "search_after", "[", "an array of sort values");
            string? trackTotal = RawJson(arguments, "track_total_hits", "tf0123456789", "true, false, or a number");

            int cap = Math.Max(0, Math.Min(settings.MaxHits, context.Limits.MaxRows));
            long requested = arguments.GetLong("size", Math.Min(_DefaultSize, cap), 0, 10000);
            List<string> notes = new List<string>();
            int size = (int)Math.Min(requested, cap);
            if (requested > cap) notes.Add("size lowered from " + requested.ToString(CultureInfo.InvariantCulture) + " to " + size.ToString(CultureInfo.InvariantCulture) + " (maxHits and output limits).");
            long from = arguments.GetLong("from", 0, 0, Int64.MaxValue);
            if (from + size > 10000)
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "from + size must not exceed 10000 (got " + (from + size).ToString(CultureInfo.InvariantCulture) + "). Use sort with search_after to page deeper.");

            string body = BuildBody(query, size, from, sort, source, aggs, searchAfter, trackTotal);
            ObservabilityTransport transport = Transport(context.Resource, settings);
            EsResolvedTarget target = await new EsTargetResolver().ResolveAsync(transport, index, settings.AllowedIndexPatterns, token).ConfigureAwait(false);
            if (target.Excluded.Count > 0)
                notes.Add(target.Excluded.Count.ToString(CultureInfo.InvariantCulture) + " index(es) outside the allowed patterns, reached through an alias or wildcard, were left out; hits, totals, and aggregations cover only the allowed indexes.");
            List<KeyValuePair<string, string?>> q = new List<KeyValuePair<string, string?>> { new KeyValuePair<string, string?>("expand_wildcards", "open") };
            string json = await transport.PostJsonAsync(target.Expression + "/_search", q, body, token).ConfigureAwait(false);
            EsSearchResponse response = ObservabilityJson.DeserializeUpstream<EsSearchResponse>(json, transport.Provider);

            List<EsHit> allowedHits = response.Hits.Hits.Where(h => EsIndexGuard.IsIndexAllowed(h.Index, settings.AllowedIndexPatterns)).ToList();
            int filtered = response.Hits.Hits.Count - allowedHits.Count;
            if (filtered > 0) notes.Add(filtered.ToString(CultureInfo.InvariantCulture) + " hit(s) from indexes outside the allowed patterns were removed (an alias may point to them).");

            long total = response.Hits.Total == null ? allowedHits.Count : response.Hits.Total.Value;
            string relation = response.Hits.Total == null ? "eq" : response.Hits.Total.Relation;
            StringBuilder sb = new StringBuilder();
            foreach (string note in notes) sb.Append("Note: ").Append(note).Append('\n');
            sb.Append(total.ToString(CultureInfo.InvariantCulture)).Append(relation == "gte" ? "+" : String.Empty).Append(" match(es) in ").Append(index)
                .Append(", returned ").Append(allowedHits.Count.ToString(CultureInfo.InvariantCulture))
                .Append(", took ").Append(response.Took.ToString(CultureInfo.InvariantCulture)).Append(" ms");
            if (response.TimedOut) sb.Append(", TIMED OUT on some shards (results are partial)");
            sb.Append('\n');
            foreach (EsHit hit in allowedHits)
            {
                sb.Append('[').Append(hit.Index).Append('/').Append(hit.Id);
                if (hit.Score.HasValue) sb.Append(" score=").Append(hit.Score.Value.ToString("0.###", CultureInfo.InvariantCulture));
                if (hit.Sort.HasValue) sb.Append(" sort=").Append(hit.Sort.Value.GetRawText());
                sb.Append("] ");
                string src = hit.Source.HasValue ? hit.Source.Value.GetRawText() : "{}";
                if (src.Length > _MaxSourceChars) src = src.Substring(0, _MaxSourceChars - 3) + "...";
                sb.Append(src.Replace('\n', ' ')).Append('\n');
            }
            if (response.Aggregations.HasValue)
            {
                string agg = response.Aggregations.Value.GetRawText();
                sb.Append("Aggregations: ").Append(agg).Append('\n');
            }

            string structured = Structured(index, total, relation, response, allowedHits, filtered);
            ToolResult result = Finish(sb.ToString(), structured, context.Limits, filtered > 0 || requested > cap || target.Excluded.Count > 0);
            result.Metrics["hits"] = allowedHits.Count;
            result.Metrics["total"] = total;
            result.Metrics["tookMs"] = response.Took;
            return result;
        }

        #endregion

        #region Private-Methods

        private static string BuildBody(string? query, int size, long from, string? sort, string? source, string? aggs, string? searchAfter, string? trackTotal)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (Utf8JsonWriter w = new Utf8JsonWriter(ms))
                {
                    w.WriteStartObject();
                    w.WritePropertyName("query");
                    if (query != null) w.WriteRawValue(query);
                    else w.WriteRawValue("{\"match_all\":{}}");
                    w.WriteNumber("size", size);
                    if (from > 0) w.WriteNumber("from", from);
                    if (sort != null)
                    {
                        w.WritePropertyName("sort");
                        w.WriteRawValue(sort);
                    }
                    if (source != null)
                    {
                        w.WritePropertyName("_source");
                        w.WriteRawValue(source);
                    }
                    if (aggs != null)
                    {
                        w.WritePropertyName("aggs");
                        w.WriteRawValue(aggs);
                    }
                    if (searchAfter != null)
                    {
                        w.WritePropertyName("search_after");
                        w.WriteRawValue(searchAfter);
                    }
                    if (trackTotal != null)
                    {
                        w.WritePropertyName("track_total_hits");
                        w.WriteRawValue(trackTotal);
                    }
                    w.WriteEndObject();
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static string Structured(string index, long total, string relation, EsSearchResponse response, List<EsHit> hits, int filtered)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (Utf8JsonWriter w = new Utf8JsonWriter(ms))
                {
                    w.WriteStartObject();
                    w.WriteString("index", index);
                    w.WriteNumber("total", total);
                    w.WriteString("relation", relation);
                    w.WriteNumber("took", response.Took);
                    w.WriteBoolean("timedOut", response.TimedOut);
                    w.WriteNumber("returned", hits.Count);
                    w.WriteNumber("filtered", filtered);
                    w.WriteStartArray("hits");
                    foreach (EsHit hit in hits)
                    {
                        w.WriteStartObject();
                        w.WriteString("index", hit.Index);
                        w.WriteString("id", hit.Id);
                        if (hit.Score.HasValue) w.WriteNumber("score", hit.Score.Value);
                        if (hit.Source.HasValue)
                        {
                            w.WritePropertyName("source");
                            hit.Source.Value.WriteTo(w);
                        }
                        if (hit.Sort.HasValue)
                        {
                            w.WritePropertyName("sort");
                            hit.Sort.Value.WriteTo(w);
                        }
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                    if (response.Aggregations.HasValue)
                    {
                        w.WritePropertyName("aggregations");
                        response.Aggregations.Value.WriteTo(w);
                    }
                    w.WriteEndObject();
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        #endregion
    }
}
