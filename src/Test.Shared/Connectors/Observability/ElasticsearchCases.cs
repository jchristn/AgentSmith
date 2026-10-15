namespace Test.Shared.Connectors.Observability
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Observability;
    using Touchstone.Core;

    /// <summary>
    /// Elasticsearch and OpenSearch connector cases against <see cref="EsStubRoutes"/>.
    /// </summary>
    public static class ElasticsearchCases
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public static readonly string SuiteId = "connectors.observability.elasticsearch";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the cases.
        /// </summary>
        /// <returns>Cases.</returns>
        public static List<TestCaseDescriptor> Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            Add(cases, "test-connection-flavor", "TestConnectionAsync detects the distribution and warns on a flavor mismatch", TestConnectionFlavor);
            Add(cases, "search-basic", "es_search sends a bounded body and renders hits", SearchBasic);
            Add(cases, "search-size-caps", "size is capped by maxHits and the row limit; from + size over 10000 is rejected", SearchSizeCaps);
            Add(cases, "search-arguments", "Unknown arguments and wrongly typed JSON arguments are rejected before any request", SearchArguments);
            Add(cases, "search-passthrough", "query, sort, _source, aggs, search_after, and track_total_hits pass through", SearchPassthrough);
            Add(cases, "search-allow-list", "Indexes outside the allow list are refused before any request", SearchAllowList);
            Add(cases, "search-alias-filter", "Hits from disallowed indexes behind an alias are removed and reported", SearchAliasFilter);
            Add(cases, "search-errors", "Query parse errors and missing indexes map to invalid_argument and not_found", SearchErrors);
            Add(cases, "search-output-limits", "Long sources are shortened and output limits truncate", SearchOutputLimits);
            Add(cases, "count", "es_count sends the query and reports the count", Count);
            Add(cases, "cluster-health", "es_cluster_health reports status and shards", ClusterHealth);
            Add(cases, "indices", "es_indices hides indexes outside the allow list and hidden indexes", Indices);
            Add(cases, "mappings", "es_mappings flattens fields, multi-fields, and objects, and hides disallowed indexes", Mappings);
            return cases;
        }

        #endregion

        #region Private-Methods

        private static void Add(List<TestCaseDescriptor> cases, string id, string name, Func<CancellationToken, Task> body)
        {
            cases.Add(new TestCaseDescriptor(SuiteId, id, name, body));
        }

        private static ObservabilityStub Stub(EsStubRoutes? routes = null)
        {
            ObservabilityStub stub = new ObservabilityStub();
            EsStubRoutes r = routes ?? new EsStubRoutes();
            stub.Handler = r.Handle;
            return stub;
        }

        private static Task<ToolResult> Run(ObservabilityStub stub, string tool, string json, CancellationToken token, Dictionary<string, string>? extra = null, OutputLimits? limits = null)
        {
            Dictionary<string, string> settings = new Dictionary<string, string> { { "flavor", "OpenSearch" }, { "allowedIndexPatterns", "logs-*" } };
            if (extra != null) foreach (KeyValuePair<string, string> kv in extra) settings[kv.Key] = kv.Value;
            ConnectorResourceContext r = ObsTestHelper.Resource("elasticsearch", ObsTestHelper.Settings(stub, settings));
            return ObsTestHelper.RunAsync(new ElasticsearchConnector(), tool, json, ObsTestHelper.Context(r, 30, limits), token);
        }

        private static async Task TestConnectionFlavor(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                ElasticsearchConnector c = new ElasticsearchConnector();
                ConnectorHealthResult os = await c.TestConnectionAsync(ObsTestHelper.Resource("elasticsearch", ObsTestHelper.Settings(stub, new Dictionary<string, string> { { "flavor", "OpenSearch" } })), token).ConfigureAwait(false);
                ObsTestHelper.Assert(os.Success && os.Details["distribution"] == "OpenSearch" && !os.Details.ContainsKey("flavorWarning") && os.Details["status"] == "yellow", "opensearch: " + os.Message);
                ConnectorHealthResult mismatch = await c.TestConnectionAsync(ObsTestHelper.Resource("elasticsearch", ObsTestHelper.Settings(stub)), token).ConfigureAwait(false);
                ObsTestHelper.Assert(mismatch.Success && mismatch.Details.ContainsKey("flavorWarning"), "mismatch warning");
            }
            using (ObservabilityStub stub = Stub(new EsStubRoutes { OpenSearch = false }))
            {
                ConnectorHealthResult es = await new ElasticsearchConnector().TestConnectionAsync(ObsTestHelper.Resource("elasticsearch", ObsTestHelper.Settings(stub)), token).ConfigureAwait(false);
                ObsTestHelper.Assert(es.Success && es.Details["distribution"] == "Elasticsearch" && !es.Details.ContainsKey("flavorWarning"), "elasticsearch detected");
                ObsTestHelper.Assert(es.Message.StartsWith("Connected to Elasticsearch 2.19.3", StringComparison.Ordinal), es.Message);
            }
        }

        private static async Task SearchBasic(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                ToolResult r = await Run(stub, "es_search", "{\"index\":\"logs-2026.10\"}", token).ConfigureAwait(false);
                ObsTestHelper.Ok(r, "search");
                StubRequest req = stub.Last("/logs-2026.10/_search")!;
                ObsTestHelper.Assert(req.Method == "POST" && req.Body == "{\"query\":{\"match_all\":{}},\"size\":10}", "body: " + req.Body);
                ObsTestHelper.Assert(req.Query["expand_wildcards"] == "open", "expand_wildcards open");
                ObsTestHelper.Assert(r.Text.Contains("50 match(es) in logs-2026.10, returned 10, took 3 ms", StringComparison.Ordinal), "summary: " + r.Text.Split('\n')[0]);
                ObsTestHelper.Assert(r.Text.Contains("[logs-2026.10/log-00 score=1] {\"@timestamp\":\"2026-10-14T08:00:00Z\"", StringComparison.Ordinal), "hit line");
                ObsTestHelper.Assert(ObsTestHelper.ArrayLength(r, "hits") == 10 && ObsTestHelper.Long(r, "total") == 50, "structured");
            }
        }

        private static async Task SearchSizeCaps(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                ToolResult r = await Run(stub, "es_search", "{\"index\":\"logs-*\",\"size\":500}", token, new Dictionary<string, string> { { "maxHits", "5" } }).ConfigureAwait(false);
                ObsTestHelper.Ok(r, "capped");
                ObsTestHelper.Assert(stub.Last("/logs-*/_search")!.Body.Contains("\"size\":5", StringComparison.Ordinal), "size 5 sent");
                ObsTestHelper.Assert(r.Truncated && r.Text.Contains("Note: size lowered from 500 to 5", StringComparison.Ordinal), "reported");
                ToolResult rows = await Run(stub, "es_search", "{\"index\":\"logs-*\",\"size\":50}", token, null, new OutputLimits { MaxRows = 3 }).ConfigureAwait(false);
                ObsTestHelper.Assert(ObsTestHelper.ArrayLength(rows, "hits") == 3, "row limit caps size");
                ToolResult zero = await Run(stub, "es_search", "{\"index\":\"logs-*\",\"size\":0}", token).ConfigureAwait(false);
                ObsTestHelper.Assert(ObsTestHelper.ArrayLength(zero, "hits") == 0 && stub.Last("/logs-*/_search")!.Body.Contains("\"size\":0", StringComparison.Ordinal), "size 0");
                ToolResult deep = await Run(stub, "es_search", "{\"index\":\"logs-*\",\"from\":9995,\"size\":10}", token).ConfigureAwait(false);
                ObsTestHelper.Error(deep, "invalid_argument", "from+size over 10000");
                ToolResult from = await Run(stub, "es_search", "{\"index\":\"logs-*\",\"from\":20,\"size\":10}", token).ConfigureAwait(false);
                ObsTestHelper.Assert(!from.IsError && stub.Last("/logs-*/_search")!.Body.Contains("\"from\":20", StringComparison.Ordinal), "from sent");
            }
        }

        private static async Task SearchArguments(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                string[] bad =
                {
                    "{\"index\":\"logs-1\",\"script_fields\":{}}",
                    "{\"index\":\"logs-1\",\"query\":\"error\"}",
                    "{\"index\":\"logs-1\",\"aggs\":[1]}",
                    "{\"index\":\"logs-1\",\"search_after\":{}}",
                    "{\"index\":\"logs-1\",\"track_total_hits\":\"yes\"}",
                    "{\"query\":{}}"
                };
                foreach (string json in bad)
                {
                    ToolResult r = await Run(stub, "es_search", json, token).ConfigureAwait(false);
                    ObsTestHelper.Error(r, "invalid_argument", json);
                }
                ObsTestHelper.Assert(stub.Requests.IsEmpty, "no request sent");
            }
        }

        private static async Task SearchPassthrough(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                string args = "{\"index\":\"logs-2026.10\",\"size\":2,\"query\":{\"term\":{\"level\":\"error\"}},\"sort\":[{\"@timestamp\":\"desc\"}],\"_source\":[\"message\"],"
                    + "\"aggs\":{\"by_level\":{\"terms\":{\"field\":\"level\"}}},\"search_after\":[1760428800000],\"track_total_hits\":true}";
                ToolResult r = await Run(stub, "es_search", args, token).ConfigureAwait(false);
                ObsTestHelper.Ok(r, "passthrough");
                string body = stub.Last("/logs-2026.10/_search")!.Body;
                ObsTestHelper.Assert(body == "{\"query\":{\"term\":{\"level\":\"error\"}},\"size\":2,\"sort\":[{\"@timestamp\":\"desc\"}],\"_source\":[\"message\"],\"aggs\":{\"by_level\":{\"terms\":{\"field\":\"level\"}}},\"search_after\":[1760428800000],\"track_total_hits\":true}", "body: " + body);
                ObsTestHelper.Assert(r.Text.Contains("Aggregations: {\"by_level\"", StringComparison.Ordinal) && (r.StructuredJson ?? String.Empty).Contains("\"aggregations\":{\"by_level\"", StringComparison.Ordinal), "aggregations rendered");
                ToolResult source = await Run(stub, "es_search", "{\"index\":\"logs-1\",\"_source\":false}", token).ConfigureAwait(false);
                ObsTestHelper.Assert(!source.IsError && stub.Last("/logs-1/_search")!.Body.Contains("\"_source\":false", StringComparison.Ordinal), "_source false");
            }
        }

        private static async Task SearchAllowList(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                foreach (string index in new[] { "secret-index", "*", "logs-*,secret-index", "_all", "remote:logs-1", ".kibana", "lo*" })
                {
                    ToolResult r = await Run(stub, "es_search", "{\"index\":\"" + index + "\"}", token).ConfigureAwait(false);
                    ObsTestHelper.Error(r, "permission_denied", index);
                }
                foreach (string index in new[] { "-logs-1", "<logs-{now/d}>", "LOGS-1", "logs 1", "logs-1,,logs-2" })
                {
                    ToolResult r = await Run(stub, "es_search", "{\"index\":\"" + index + "\"}", token).ConfigureAwait(false);
                    ObsTestHelper.Error(r, "invalid_argument", index);
                }
                ToolResult count = await Run(stub, "es_count", "{\"index\":\"secret-index\"}", token).ConfigureAwait(false);
                ObsTestHelper.Error(count, "permission_denied", "count");
                ToolResult map = await Run(stub, "es_mappings", "{\"index\":\"secret-*\"}", token).ConfigureAwait(false);
                ObsTestHelper.Error(map, "permission_denied", "mappings");
                ObsTestHelper.Assert(stub.Requests.IsEmpty, "no request sent");
            }
        }

        private static async Task SearchAliasFilter(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                ToolResult r = await Run(stub, "es_search", "{\"index\":\"logs-alias\",\"size\":4}", token).ConfigureAwait(false);
                ObsTestHelper.Ok(r, "alias");
                ObsTestHelper.Assert(ObsTestHelper.ArrayLength(r, "hits") == 0 && ObsTestHelper.Long(r, "filtered") == 4, "hits removed");
                ObsTestHelper.Assert(r.Truncated && r.Text.Contains("4 hit(s) from indexes outside the allowed patterns were removed", StringComparison.Ordinal), "reported");
                ObsTestHelper.Assert(!r.Text.Contains("secret-index", StringComparison.Ordinal), "no secret index content");
            }
        }

        private static async Task SearchErrors(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                ToolResult parse = await Run(stub, "es_search", "{\"index\":\"logs-1\",\"query\":{\"bogus_query\":{}}}", token).ConfigureAwait(false);
                ObsTestHelper.Error(parse, "invalid_argument", "parse");
                ObsTestHelper.Assert(parse.Text.Contains("unknown query [bogus_query]", StringComparison.Ordinal) && parse.Text.Contains("OpenSearch", StringComparison.Ordinal), "reason: " + parse.Text);
                ToolResult missing = await Run(stub, "es_search", "{\"index\":\"logs-missing\"}", token).ConfigureAwait(false);
                ObsTestHelper.Error(missing, "not_found", "missing");
                ObsTestHelper.Assert(missing.Text.Contains("no such index [logs-missing]", StringComparison.Ordinal), "reason");
            }
        }

        private static async Task SearchOutputLimits(CancellationToken token)
        {
            using (ObservabilityStub stub = new ObservabilityStub())
            {
                string big = new string('y', 5000);
                stub.Handler = r => StubReply.Json("{\"took\":1,\"timed_out\":true,\"hits\":{\"total\":3,\"hits\":[{\"_index\":\"logs-1\",\"_id\":\"a\",\"_score\":null,\"_source\":{\"m\":\"" + big + "\"},\"sort\":[5]}]}}");
                ToolResult r = await Run(stub, "es_search", "{\"index\":\"logs-1\"}", token).ConfigureAwait(false);
                ObsTestHelper.Ok(r, "big source");
                ObsTestHelper.Assert(r.Text.Contains("TIMED OUT", StringComparison.Ordinal) && r.Text.Contains("3 match(es)", StringComparison.Ordinal), "numeric total and timed out");
                ObsTestHelper.Assert(r.Text.Contains("sort=[5]", StringComparison.Ordinal) && r.Text.Contains("...", StringComparison.Ordinal) && !r.Text.Contains(new string('y', 2100), StringComparison.Ordinal), "source shortened");
                ToolResult small = await Run(stub, "es_search", "{\"index\":\"logs-1\"}", token, null, new OutputLimits { MaxBytes = 1024 }).ConfigureAwait(false);
                ObsTestHelper.Assert(small.Truncated && small.Text.Length < 1200, "byte limit");
            }
        }

        private static async Task Count(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                ToolResult all = await Run(stub, "es_count", "{\"index\":\"logs-*\"}", token).ConfigureAwait(false);
                ObsTestHelper.Ok(all, "count");
                ObsTestHelper.Assert(ObsTestHelper.Long(all, "count") == 50 && stub.Last("/logs-*/_count")!.Body == "{}", "count all");
                ToolResult term = await Run(stub, "es_count", "{\"index\":\"logs-*\",\"query\":{\"term\":{\"level\":\"error\"}}}", token).ConfigureAwait(false);
                ObsTestHelper.Assert(term.Text.StartsWith("5 document(s) match in logs-*", StringComparison.Ordinal) && stub.Last("/logs-*/_count")!.Body == "{\"query\":{\"term\":{\"level\":\"error\"}}}", "count query");
            }
        }

        private static async Task ClusterHealth(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                ToolResult r = await Run(stub, "es_cluster_health", "{}", token).ConfigureAwait(false);
                ObsTestHelper.Ok(r, "health");
                ObsTestHelper.Assert(r.Text.Contains("status: yellow", StringComparison.Ordinal) && r.Text.Contains("unassigned 2", StringComparison.Ordinal) && r.Text.Contains("71.4%", StringComparison.Ordinal), r.Text);
                ObsTestHelper.Assert(r.Metrics["unassignedShards"] == 2, "metric");
            }
        }

        private static async Task Indices(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                ToolResult r = await Run(stub, "es_indices", "{}", token).ConfigureAwait(false);
                ObsTestHelper.Ok(r, "indices");
                ObsTestHelper.Assert(r.Text.StartsWith("2 index(es)", StringComparison.Ordinal) && !r.Text.Contains("secret-index", StringComparison.Ordinal) && !r.Text.Contains(".kibana", StringComparison.Ordinal), r.Text);
                ObsTestHelper.Assert(r.Text.IndexOf("logs-2026.09", StringComparison.Ordinal) < r.Text.IndexOf("logs-2026.10", StringComparison.Ordinal), "sorted");
                StubRequest req = stub.Last("/_cat/indices/*")!;
                ObsTestHelper.Assert(req.Query["format"] == "json" && req.Query["bytes"] == "b", "cat params");
                ToolResult open = await Run(stub, "es_indices", "{}", token, new Dictionary<string, string> { { "allowedIndexPatterns", "" } }).ConfigureAwait(false);
                ObsTestHelper.Assert(open.Text.StartsWith("3 index(es)", StringComparison.Ordinal) && !open.Text.Contains(".kibana", StringComparison.Ordinal), "empty allow list hides dot indexes");
                ToolResult dot = await Run(stub, "es_indices", "{}", token, new Dictionary<string, string> { { "allowedIndexPatterns", ".kib*" } }).ConfigureAwait(false);
                ObsTestHelper.Assert(dot.Text.StartsWith("1 index(es)", StringComparison.Ordinal) && dot.Text.Contains(".kibana", StringComparison.Ordinal), "dot pattern allows hidden");
                ToolResult bad = await Run(stub, "es_indices", "{\"pattern\":\"a:b\"}", token).ConfigureAwait(false);
                ObsTestHelper.Error(bad, "permission_denied", "remote pattern");
            }
        }

        private static async Task Mappings(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                ToolResult r = await Run(stub, "es_mappings", "{\"index\":\"logs-2026.10\"}", token).ConfigureAwait(false);
                ObsTestHelper.Ok(r, "mappings");
                foreach (string line in new[] { "logs-2026.10 (8 field(s)):", "  @timestamp: date", "  http: object", "  http.method: keyword", "  message: text", "  message.keyword: keyword", "  status: integer" })
                    ObsTestHelper.Assert(r.Text.Contains(line + "\n", StringComparison.Ordinal), "line '" + line + "' in " + r.Text);
                ObsTestHelper.Assert(!r.Text.Contains("secret-index", StringComparison.Ordinal) && !r.Text.Contains("classified", StringComparison.Ordinal), "disallowed index hidden");
                ToolResult filtered = await Run(stub, "es_mappings", "{\"index\":\"logs-2026.10\",\"field\":\"HTTP\"}", token).ConfigureAwait(false);
                ObsTestHelper.Assert(filtered.Text.Contains("(3 field(s))", StringComparison.Ordinal), "field filter: " + filtered.Text);
                ToolResult capped = await Run(stub, "es_mappings", "{\"index\":\"logs-2026.10\"}", token, null, new OutputLimits { MaxRows = 2 }).ConfigureAwait(false);
                ObsTestHelper.Assert(capped.Truncated && capped.Text.Contains("[6 more field(s) not shown", StringComparison.Ordinal), "row cap");
            }
        }

        #endregion
    }
}
