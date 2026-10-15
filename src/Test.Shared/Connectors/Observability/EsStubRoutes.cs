namespace Test.Shared.Connectors.Observability
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Recorded Elasticsearch and OpenSearch REST response shapes for <see cref="ObservabilityStub"/>. The alias
    /// <c>logs-alias</c> points to <c>logs-2026.10</c> and <c>secret-index</c>, and <c>secret-alias</c> only to
    /// <c>secret-index</c>; a search sent straight to <c>logs-alias</c> returns hits from <c>secret-index</c>, to
    /// exercise hit filtering.
    /// </summary>
    public class EsStubRoutes
    {
        #region Public-Members

        /// <summary>
        /// Whether <c>GET /</c> reports the OpenSearch distribution. Default true.
        /// </summary>
        public bool OpenSearch { get; set; } = true;

        /// <summary>
        /// Total matches reported by searches. Default 50.
        /// </summary>
        public int Total { get; set; } = 50;

        /// <summary>
        /// Whether <c>_resolve/index</c> expands wildcards to aliases too, as Elasticsearch does. Default false, so the
        /// other cases see <c>logs-*</c> resolve to the two log indexes only.
        /// </summary>
        public bool WildcardMatchesAliases { get; set; } = false;

        #endregion

        #region Private-Members

        private static readonly Regex _Size = new Regex("\"size\":(\\d+)", RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Handle a request.
        /// </summary>
        /// <param name="r">Request.</param>
        /// <returns>Reply.</returns>
        public StubReply Handle(StubRequest r)
        {
            string path = r.Path;
            if (path == "/")
            {
                string distribution = OpenSearch ? "\"distribution\":\"opensearch\"," : String.Empty;
                return StubReply.Json("{\"name\":\"node-1\",\"cluster_name\":\"stub-cluster\",\"version\":{" + distribution + "\"number\":\"2.19.3\"},\"tagline\":\"The OpenSearch Project\"}");
            }
            if (path == "/_cluster/health")
                return StubReply.Json("{\"cluster_name\":\"stub-cluster\",\"status\":\"yellow\",\"timed_out\":false,\"number_of_nodes\":1,\"number_of_data_nodes\":1,\"active_primary_shards\":5,\"active_shards\":5,\"relocating_shards\":0,\"initializing_shards\":0,\"unassigned_shards\":2,\"delayed_unassigned_shards\":0,\"number_of_pending_tasks\":0,\"active_shards_percent_as_number\":71.4}");
            if (path.StartsWith("/_cat/indices", StringComparison.Ordinal))
                return StubReply.Json("[{\"health\":\"green\",\"status\":\"open\",\"index\":\"secret-index\",\"docs.count\":\"3\",\"store.size\":\"4255\",\"pri\":\"1\",\"rep\":\"0\"},"
                    + "{\"health\":\"green\",\"status\":\"open\",\"index\":\".kibana\",\"docs.count\":\"1\",\"store.size\":\"208\",\"pri\":\"1\",\"rep\":\"0\"},"
                    + "{\"health\":\"yellow\",\"status\":\"open\",\"index\":\"logs-2026.10\",\"docs.count\":\"50\",\"store.size\":\"10261\",\"pri\":\"1\",\"rep\":\"1\"},"
                    + "{\"health\":\"green\",\"status\":\"open\",\"index\":\"logs-2026.09\",\"docs.count\":\"7\",\"store.size\":\"5000\",\"pri\":\"1\",\"rep\":\"0\"}]");

            if (path.StartsWith("/_resolve/index/", StringComparison.Ordinal))
                return Resolve(path.Substring("/_resolve/index/".Length));

            string[] parts = path.TrimStart('/').Split('/');
            if (parts.Length != 2) return StubReply.Status(404, "{\"error\":{\"type\":\"no_handler\",\"reason\":\"no handler\"},\"status\":404}");
            string index = parts[0];
            if (index.Contains("missing", StringComparison.Ordinal))
                return StubReply.Status(404, "{\"error\":{\"root_cause\":[{\"type\":\"index_not_found_exception\",\"reason\":\"no such index [" + index + "]\"}],\"type\":\"index_not_found_exception\",\"reason\":\"no such index [" + index + "]\"},\"status\":404}");
            switch (parts[1])
            {
                case "_search":
                    return Search(index, r.Body);
                case "_count":
                    return StubReply.Json("{\"count\":" + (r.Body.Contains("\"term\"", StringComparison.Ordinal) ? "5" : Total.ToString(CultureInfo.InvariantCulture)) + ",\"_shards\":{\"total\":1,\"successful\":1,\"skipped\":0,\"failed\":0}}");
                case "_mapping":
                    return StubReply.Json("{\"" + index.Replace("*", "2026.10", StringComparison.Ordinal) + "\":{\"mappings\":{\"properties\":{"
                        + "\"@timestamp\":{\"type\":\"date\"},"
                        + "\"http\":{\"properties\":{\"method\":{\"type\":\"keyword\"},\"path\":{\"type\":\"keyword\"}}},"
                        + "\"level\":{\"type\":\"keyword\"},"
                        + "\"message\":{\"type\":\"text\",\"fields\":{\"keyword\":{\"type\":\"keyword\",\"ignore_above\":256}}},"
                        + "\"status\":{\"type\":\"integer\"}}}},"
                        + "\"secret-index\":{\"mappings\":{\"properties\":{\"classified\":{\"type\":\"text\"}}}}}");
                default:
                    return StubReply.Status(404, "{\"error\":{\"type\":\"no_handler\",\"reason\":\"no handler\"},\"status\":404}");
            }
        }

        #endregion

        #region Private-Methods

        private StubReply Resolve(string expression)
        {
            string[] catalog = new[] { "logs-2026.09", "logs-2026.10", "secret-index" };
            Dictionary<string, string[]> aliases = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                { "logs-alias", new[] { "logs-2026.10", "secret-index" } },
                { "secret-alias", new[] { "secret-index" } }
            };
            SortedSet<string> indices = new SortedSet<string>(StringComparer.Ordinal);
            SortedSet<string> matchedAliases = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string part in expression.Split(','))
            {
                if (part.Contains("missing", StringComparison.Ordinal))
                    return StubReply.Status(404, "{\"error\":{\"root_cause\":[{\"type\":\"index_not_found_exception\",\"reason\":\"no such index [" + part + "]\"}],\"type\":\"index_not_found_exception\",\"reason\":\"no such index [" + part + "]\"},\"status\":404}");
                if (part.Contains('*', StringComparison.Ordinal))
                {
                    foreach (string name in catalog.Where(n => Hugo.Connectors.Observability.EsIndexGuard.GlobMatch(n, part))) indices.Add(name);
                    if (WildcardMatchesAliases)
                        foreach (string name in aliases.Keys.Where(n => Hugo.Connectors.Observability.EsIndexGuard.GlobMatch(n, part))) matchedAliases.Add(name);
                }
                else if (aliases.ContainsKey(part))
                {
                    matchedAliases.Add(part);
                }
                else
                {
                    indices.Add(part);
                }
            }
            StringBuilder sb = new StringBuilder("{\"indices\":[");
            sb.Append(String.Join(",", indices.Select(i => "{\"name\":\"" + i + "\",\"attributes\":[\"open\"]}")));
            sb.Append("],\"aliases\":[");
            sb.Append(String.Join(",", matchedAliases.Select(a => "{\"name\":\"" + a + "\",\"indices\":[" + String.Join(",", aliases[a].Select(i => "\"" + i + "\"")) + "]}")));
            sb.Append("],\"data_streams\":[]}");
            return StubReply.Json(sb.ToString());
        }

        private StubReply Search(string index, string body)
        {
            if (body.Contains("\"bogus_query\"", StringComparison.Ordinal))
                return StubReply.Status(400, "{\"error\":{\"root_cause\":[{\"type\":\"parsing_exception\",\"reason\":\"unknown query [bogus_query]\"}],\"type\":\"parsing_exception\",\"reason\":\"unknown query [bogus_query]\"},\"status\":400}");
            Match m = _Size.Match(body);
            int size = m.Success ? Int32.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 10;
            int n = Math.Min(size, Total);
            string hitIndex = index == "logs-alias" ? "secret-index" : index.Replace("*", "2026.10", StringComparison.Ordinal);
            StringBuilder sb = new StringBuilder("{\"took\":3,\"timed_out\":false,\"_shards\":{\"total\":1,\"successful\":1,\"skipped\":0,\"failed\":0},\"hits\":{\"total\":{\"value\":");
            sb.Append(Total.ToString(CultureInfo.InvariantCulture)).Append(",\"relation\":\"eq\"},\"max_score\":1.0,\"hits\":[");
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"_index\":\"").Append(hitIndex).Append("\",\"_id\":\"log-").Append(i.ToString("D2", CultureInfo.InvariantCulture)).Append("\",\"_score\":1.0,\"_source\":{\"@timestamp\":\"2026-10-14T08:")
                    .Append((i % 60).ToString("D2", CultureInfo.InvariantCulture)).Append(":00Z\",\"level\":\"").Append(i % 10 == 0 ? "error" : "info").Append("\",\"message\":\"request ").Append(i.ToString(CultureInfo.InvariantCulture)).Append("\"}}");
            }
            sb.Append("]}");
            if (body.Contains("\"aggs\"", StringComparison.Ordinal))
                sb.Append(",\"aggregations\":{\"by_level\":{\"doc_count_error_upper_bound\":0,\"sum_other_doc_count\":0,\"buckets\":[{\"key\":\"info\",\"doc_count\":45},{\"key\":\"error\",\"doc_count\":5}]}}");
            sb.Append('}');
            return StubReply.Json(sb.ToString());
        }

        #endregion
    }
}
