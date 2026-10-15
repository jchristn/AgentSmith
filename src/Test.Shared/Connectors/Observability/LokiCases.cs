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
    /// Loki connector cases against <see cref="LokiStubRoutes"/>.
    /// </summary>
    public static class LokiCases
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public static readonly string SuiteId = "connectors.observability.loki";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the cases.
        /// </summary>
        /// <returns>Cases.</returns>
        public static List<TestCaseDescriptor> Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            Add(cases, "test-connection", "TestConnectionAsync lists labels with the tenant header", TestConnection);
            Add(cases, "tenant-header", "The tenant header is sent; a missing tenant maps to auth_failed", TenantHeader);
            Add(cases, "selector-guard", "Queries without a stream selector are rejected before any request", SelectorGuard);
            Add(cases, "range-lines", "loki_query_range renders a stream legend and lines newest first", RangeLines);
            Add(cases, "range-forward", "direction forward renders oldest first and is sent to Loki", RangeForward);
            Add(cases, "line-caps", "limit, maxLines, and the output line limit cap lines and report the rest", LineCaps);
            Add(cases, "range-metric", "Metric range queries render series summaries and pass the step", RangeMetric);
            Add(cases, "instant-metric", "loki_query renders an instant vector", InstantMetric);
            Add(cases, "range-too-long", "Windows beyond maxRangeHours fail with time_range_exceeded", RangeTooLong);
            Add(cases, "upstream-400", "A LogQL parse error maps to invalid_argument with Loki's message", Upstream400);
            Add(cases, "labels", "loki_labels and loki_label_values list sorted names and values", Labels);
            Add(cases, "long-lines", "Long lines are shortened", LongLines);
            Add(cases, "selector-table", "Stream selector detection table", SelectorTable);
            return cases;
        }

        #endregion

        #region Private-Methods

        private static void Add(List<TestCaseDescriptor> cases, string id, string name, Func<CancellationToken, Task> body)
        {
            cases.Add(new TestCaseDescriptor(SuiteId, id, name, body));
        }

        private static ObservabilityStub Stub(LokiStubRoutes? routes = null)
        {
            ObservabilityStub stub = new ObservabilityStub();
            LokiStubRoutes r = routes ?? new LokiStubRoutes { RequiredTenant = "tenant-a" };
            stub.Handler = r.Handle;
            return stub;
        }

        private static Dictionary<string, string> Tenant(Dictionary<string, string>? extra = null)
        {
            Dictionary<string, string> s = new Dictionary<string, string> { { "tenantHeader", "tenant-a" } };
            if (extra != null) foreach (KeyValuePair<string, string> kv in extra) s[kv.Key] = kv.Value;
            return s;
        }

        private static Task<ToolResult> Run(ObservabilityStub stub, string tool, string json, CancellationToken token, Dictionary<string, string>? settings = null, OutputLimits? limits = null)
        {
            ConnectorResourceContext r = ObsTestHelper.Resource("loki", ObsTestHelper.Settings(stub, settings ?? Tenant()));
            return ObsTestHelper.RunAsync(new LokiConnector(), tool, json, ObsTestHelper.Context(r, 30, limits), token);
        }

        private static async Task TestConnection(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                ConnectorHealthResult h = await new LokiConnector().TestConnectionAsync(ObsTestHelper.Resource("loki", ObsTestHelper.Settings(stub, Tenant())), token).ConfigureAwait(false);
                ObsTestHelper.Assert(h.Success && h.Details["labels"] == "3" && h.Details["tenant"] == "tenant-a", "health: " + h.Message);
            }
        }

        private static async Task TenantHeader(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                ToolResult ok = await Run(stub, "loki_labels", "{}", token).ConfigureAwait(false);
                ObsTestHelper.Ok(ok, "with tenant");
                ObsTestHelper.Assert(stub.Last("/loki/api/v1/labels")!.Headers["X-Scope-OrgID"] == "tenant-a", "header sent");
                ToolResult none = await Run(stub, "loki_labels", "{}", token, new Dictionary<string, string>()).ConfigureAwait(false);
                ObsTestHelper.Error(none, "auth_failed", "no tenant");
                ObsTestHelper.Assert(stub.Requests.Last().Headers["X-Scope-OrgID"] == null, "no header when tenantHeader empty");
            }
        }

        private static async Task SelectorGuard(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                foreach (string q in new[] { "\\\"error\\\"", "{}", "sum(rate([5m]))", "{app}" })
                {
                    ToolResult r = await Run(stub, "loki_query_range", "{\"query\":\"" + q + "\"}", token).ConfigureAwait(false);
                    ObsTestHelper.Error(r, "invalid_argument", "no selector: " + q);
                    ObsTestHelper.Assert(r.Text.Contains("stream selector", StringComparison.Ordinal), "guidance");
                }
                ToolResult instant = await Run(stub, "loki_query", "{\"query\":\"count_over_time([5m])\"}", token).ConfigureAwait(false);
                ObsTestHelper.Error(instant, "invalid_argument", "instant without selector");
                ToolResult values = await Run(stub, "loki_label_values", "{\"label\":\"app\",\"query\":\"app=api\"}", token).ConfigureAwait(false);
                ObsTestHelper.Error(values, "invalid_argument", "label values selector");
                ObsTestHelper.Assert(stub.Requests.IsEmpty, "no request sent");
            }
        }

        private static async Task RangeLines(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                ToolResult r = await Run(stub, "loki_query_range", "{\"query\":\"{env=\\\"testbed\\\"} |= \\\"line\\\"\",\"start\":\"15m\"}", token).ConfigureAwait(false);
                ObsTestHelper.Ok(r, "lines");
                ObsTestHelper.Assert(r.Text.StartsWith("60 line(s) from 2 stream(s), newest first", StringComparison.Ordinal), "header: " + r.Text.Split('\n')[0]);
                ObsTestHelper.Assert(r.Text.Contains("s1 = {app=\"hugo-api\",env=\"testbed\"}", StringComparison.Ordinal), "legend");
                List<string> lines = r.Text.Split('\n').Where(l => l.Length > 24 && l[4] == '-' && l[10] == 'T').ToList();
                ObsTestHelper.Assert(lines.Count == 60, "60 lines, got " + lines.Count);
                ObsTestHelper.Assert(lines[0].Contains(" s1 level=error app=hugo-api msg=line 0", StringComparison.Ordinal), "newest first: " + lines[0]);
                ObsTestHelper.Assert(lines[1].Contains(" s2 ", StringComparison.Ordinal), "interleaved by time: " + lines[1]);
                ObsTestHelper.Assert(String.CompareOrdinal(lines[0], lines[59]) > 0, "descending timestamps");
                StubRequest req = stub.Last("/loki/api/v1/query_range")!;
                ObsTestHelper.Assert(req.Query["direction"] == "backward" && req.Query["limit"] == "100" && req.Query["start"]!.Length == 19, "params: " + req.RawUrl);
                ObsTestHelper.Assert(ObsTestHelper.ArrayLength(r, "lines") == 60 && ObsTestHelper.ArrayLength(r, "streams") == 2, "structured");
            }
        }

        private static async Task RangeForward(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                ToolResult r = await Run(stub, "loki_query_range", "{\"query\":\"{app=\\\"hugo-api\\\"}\",\"direction\":\"forward\"}", token).ConfigureAwait(false);
                ObsTestHelper.Ok(r, "forward");
                List<string> lines = r.Text.Split('\n').Where(l => l.Length > 24 && l[4] == '-' && l[10] == 'T').ToList();
                ObsTestHelper.Assert(String.CompareOrdinal(lines[0], lines[lines.Count - 1]) < 0, "ascending");
                ObsTestHelper.Assert(stub.Last("/loki/api/v1/query_range")!.Query["direction"] == "forward", "direction sent");
                ToolResult bad = await Run(stub, "loki_query_range", "{\"query\":\"{app=\\\"x\\\"}\",\"direction\":\"sideways\"}", token).ConfigureAwait(false);
                ObsTestHelper.Error(bad, "invalid_argument", "bad direction");
            }
        }

        private static async Task LineCaps(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                ToolResult limit = await Run(stub, "loki_query_range", "{\"query\":\"{app=\\\"hugo-api\\\"}\",\"limit\":10}", token).ConfigureAwait(false);
                ObsTestHelper.Assert(limit.Truncated && ObsTestHelper.Long(limit, "count") == 10 && ObsTestHelper.Long(limit, "omitted") == 50, "limit 10");
                ObsTestHelper.Assert(limit.Text.Contains("[50 more line(s) not shown", StringComparison.Ordinal), "reported");
                ObsTestHelper.Assert(stub.Last("/loki/api/v1/query_range")!.Query["limit"] == "10", "limit sent");

                ToolResult maxLines = await Run(stub, "loki_query_range", "{\"query\":\"{app=\\\"hugo-api\\\"}\",\"limit\":5000}", token, Tenant(new Dictionary<string, string> { { "maxLines", "7" } })).ConfigureAwait(false);
                ObsTestHelper.Assert(ObsTestHelper.Long(maxLines, "count") == 7 && stub.Last("/loki/api/v1/query_range")!.Query["limit"] == "7", "maxLines caps");

                ToolResult outputLines = await Run(stub, "loki_query_range", "{\"query\":\"{app=\\\"hugo-api\\\"}\"}", token, null, new OutputLimits { MaxLines = 12 }).ConfigureAwait(false);
                ObsTestHelper.Assert(outputLines.Truncated && outputLines.Text.Split('\n').Length <= 14, "output line limit");
            }
        }

        private static async Task RangeMetric(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                ToolResult r = await Run(stub, "loki_query_range", "{\"query\":\"sum by (level) (count_over_time({app=\\\"hugo-api\\\"}[1m]))\",\"step\":\"1m\"}", token).ConfigureAwait(false);
                ObsTestHelper.Ok(r, "metric");
                ObsTestHelper.Assert(r.Text.Contains("{level=\"error\"}", StringComparison.Ordinal) && r.Text.Contains("| series | points |", StringComparison.Ordinal), "series summary: " + r.Text);
                ObsTestHelper.Assert(stub.Last("/loki/api/v1/query_range")!.Query["step"] == "60s", "step passed");
            }
        }

        private static async Task InstantMetric(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                ToolResult r = await Run(stub, "loki_query", "{\"query\":\"sum(count_over_time({app=\\\"hugo-api\\\"} |= \\\"error\\\" [5m]))\"}", token).ConfigureAwait(false);
                ObsTestHelper.Ok(r, "instant");
                ObsTestHelper.Assert(r.Text.Contains("1 series", StringComparison.Ordinal) && r.Text.Contains("| 6 |", StringComparison.Ordinal), "vector: " + r.Text);
                ObsTestHelper.Assert(stub.Last("/loki/api/v1/query")!.Query["time"]!.Length == 19, "time in nanoseconds");
            }
        }

        private static async Task RangeTooLong(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                ToolResult r = await Run(stub, "loki_query_range", "{\"query\":\"{app=\\\"a\\\"}\",\"start\":\"25h\"}", token).ConfigureAwait(false);
                ObsTestHelper.Error(r, "time_range_exceeded", "25h over 24h default");
                ToolResult ok = await Run(stub, "loki_query_range", "{\"query\":\"{app=\\\"a\\\"}\",\"start\":\"25h\"}", token, Tenant(new Dictionary<string, string> { { "maxRangeHours", "48" } })).ConfigureAwait(false);
                ObsTestHelper.Ok(ok, "raised limit");
            }
        }

        private static async Task Upstream400(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                ToolResult r = await Run(stub, "loki_query_range", "{\"query\":\"{app=\\\"a\\\"} |~ \\\"(\\\"\"}", token).ConfigureAwait(false);
                ObsTestHelper.Error(r, "invalid_argument", "parse error");
                ObsTestHelper.Assert(r.Text.Contains("invalid regex", StringComparison.Ordinal), "message: " + r.Text);
            }
        }

        private static async Task Labels(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                ToolResult names = await Run(stub, "loki_labels", "{\"start\":\"1h\"}", token).ConfigureAwait(false);
                ObsTestHelper.Ok(names, "labels");
                ObsTestHelper.Assert(names.Text.StartsWith("3 label name(s)", StringComparison.Ordinal), "names");
                ToolResult values = await Run(stub, "loki_label_values", "{\"label\":\"app\",\"query\":\"{env=\\\"testbed\\\"}\"}", token).ConfigureAwait(false);
                ObsTestHelper.Ok(values, "values");
                ObsTestHelper.Assert(values.Text.IndexOf("hugo-api", StringComparison.Ordinal) < values.Text.IndexOf("hugo-worker", StringComparison.Ordinal), "sorted");
                ObsTestHelper.Assert(stub.Last("/loki/api/v1/label/app/values")!.Query["query"] == "{env=\"testbed\"}", "selector sent");
                ToolResult bad = await Run(stub, "loki_label_values", "{\"label\":\"a/b\"}", token).ConfigureAwait(false);
                ObsTestHelper.Error(bad, "invalid_argument", "bad label");
                ToolResult missing = await Run(stub, "loki_label_values", "{}", token).ConfigureAwait(false);
                ObsTestHelper.Error(missing, "invalid_argument", "label required");
            }
        }

        private static async Task LongLines(CancellationToken token)
        {
            using (ObservabilityStub stub = new ObservabilityStub())
            {
                string longLine = new string('x', 5000);
                stub.Handler = r => StubReply.Json("{\"status\":\"success\",\"data\":{\"resultType\":\"streams\",\"result\":[{\"stream\":{\"app\":\"a\"},\"values\":[[\"1791964800000000000\",\"" + longLine + "\"]]}]}}");
                ToolResult r = await Run(stub, "loki_query_range", "{\"query\":\"{app=\\\"a\\\"}\"}", token).ConfigureAwait(false);
                ObsTestHelper.Ok(r, "long");
                ObsTestHelper.Assert(r.Text.Contains(new string('x', 1990) + "...", StringComparison.Ordinal) && !r.Text.Contains(new string('x', 2001), StringComparison.Ordinal), "shortened to 2000");
                ObsTestHelper.Assert(r.Text.Contains("2026-10-14T08:00:00.000Z s1", StringComparison.Ordinal), "nanosecond time rendering");
            }
        }

        private static Task SelectorTable(CancellationToken token)
        {
            Dictionary<string, bool> table = new Dictionary<string, bool>
            {
                { "{app=\"api\"}", true },
                { "{app=~\"api|web\"}", true },
                { "{app!=\"api\", env=\"prod\"}", true },
                { "{ app = \"api\" }", true },
                { "{app!~`a.*`}", true },
                { "sum(rate({app=\"api\"}[5m]))", true },
                { "count_over_time({job=\"x\"} |= \"e\" [1m])", true },
                { "{__name__=\"x\"}", true },
                { "\"error\"", false },
                { "{}", false },
                { "{app}", false },
                { "{=\"x\"}", false },
                { "rate([5m])", false },
                { "", false },
                { "{1app=\"x\"}", false }
            };
            foreach (KeyValuePair<string, bool> row in table)
                ObsTestHelper.Assert(LokiQueryGuard.HasStreamSelector(row.Key) == row.Value, "selector '" + row.Key + "' expected " + row.Value);
            return Task.CompletedTask;
        }

        #endregion
    }
}
