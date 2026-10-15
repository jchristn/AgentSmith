namespace Test.Shared.Connectors.Observability
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Observability;
    using Touchstone.Core;

    /// <summary>
    /// Prometheus connector cases against <see cref="PromStubRoutes"/>.
    /// </summary>
    public static class PrometheusCases
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public static readonly string SuiteId = "connectors.observability.prometheus";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the cases.
        /// </summary>
        /// <returns>Cases.</returns>
        public static List<TestCaseDescriptor> Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            Add(cases, "test-connection", "TestConnectionAsync reads buildinfo", TestConnection);
            Add(cases, "query-vector", "prom_query renders a vector table, warnings, and structured series", QueryVector);
            Add(cases, "query-scalar", "prom_query renders a scalar", QueryScalar);
            Add(cases, "query-bad", "A PromQL syntax error maps to invalid_argument with the upstream message", QueryBad);
            Add(cases, "query-required", "prom_query without a query is invalid_argument before any request", QueryRequired);
            Add(cases, "max-series", "Series beyond maxSeries, the row limit, or limit are dropped and reported", MaxSeries);
            Add(cases, "range-default-step", "prom_query_range picks a step and summarizes each series", RangeDefaultStep);
            Add(cases, "range-step-raised", "A step finer than maxPointsPerSeries allows is raised and reported", RangeStepRaised);
            Add(cases, "range-too-long", "Windows beyond maxRangeHours fail before any request", RangeTooLong);
            Add(cases, "range-output-limits", "Output limits truncate the text and mark the result", RangeOutputLimits);
            Add(cases, "alerts", "prom_alerts lists and filters alerts", Alerts);
            Add(cases, "rules", "prom_rules lists rules, passes type, and filters unhealthy", Rules);
            Add(cases, "targets", "prom_targets lists down targets first and filters", Targets);
            Add(cases, "series", "prom_series sends every match[] and needs at least one", Series);
            Add(cases, "label-values", "prom_label_values lists names or values and validates the label", LabelValues);
            return cases;
        }

        #endregion

        #region Private-Methods

        private static void Add(List<TestCaseDescriptor> cases, string id, string name, Func<CancellationToken, Task> body)
        {
            cases.Add(new TestCaseDescriptor(SuiteId, id, name, body));
        }

        private static ObservabilityStub Stub(PromStubRoutes? routes = null)
        {
            ObservabilityStub stub = new ObservabilityStub();
            PromStubRoutes r = routes ?? new PromStubRoutes();
            stub.Handler = r.Handle;
            return stub;
        }

        private static ConnectorInvocationContext Ctx(ObservabilityStub stub, Dictionary<string, string>? extra = null, OutputLimits? limits = null)
        {
            return ObsTestHelper.Context(ObsTestHelper.Resource("prometheus", ObsTestHelper.Settings(stub, extra)), 30, limits);
        }

        private static Task<ToolResult> Run(ObservabilityStub stub, string tool, string json, CancellationToken token, Dictionary<string, string>? extra = null, OutputLimits? limits = null)
        {
            return ObsTestHelper.RunAsync(new PrometheusConnector(), tool, json, Ctx(stub, extra, limits), token);
        }

        private static async Task TestConnection(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                ConnectorHealthResult h = await new PrometheusConnector().TestConnectionAsync(ObsTestHelper.Resource("prometheus", ObsTestHelper.Settings(stub)), token).ConfigureAwait(false);
                ObsTestHelper.Assert(h.Success && h.Details["version"] == "3.7.0", "health: " + h.Message);
            }
        }

        private static async Task QueryVector(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                ToolResult r = await Run(stub, "prom_query", "{\"query\":\"http_requests_total\",\"time\":\"2026-10-14T08:00:00Z\"}", token).ConfigureAwait(false);
                ObsTestHelper.Ok(r, "vector");
                ObsTestHelper.Assert(r.Text.Contains("3 series", StringComparison.Ordinal), "count line");
                ObsTestHelper.Assert(r.Text.Contains("http_requests_total{instance=\"i-1\",job=\"api\"}", StringComparison.Ordinal), "label set rendering: " + r.Text);
                ObsTestHelper.Assert(r.Text.Contains("2026-10-14T08:00:00Z", StringComparison.Ordinal), "time");
                ObsTestHelper.Assert(r.Text.Contains("Prometheus warning: stub warning", StringComparison.Ordinal), "warning note");
                ObsTestHelper.Assert(ObsTestHelper.ArrayLength(r, "series") == 3, "structured series");
                ObsTestHelper.Assert(stub.Last("/api/v1/query")!.Query["time"] == "1791964800", "time sent as unix seconds: " + stub.Last("/api/v1/query")!.Query["time"]);
                ObsTestHelper.Assert(r.Metrics["series"] == 3 && r.Metrics.ContainsKey("latencyMs"), "metrics");
            }
        }

        private static async Task QueryScalar(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                ToolResult r = await Run(stub, "prom_query", "{\"query\":\"scalar(1)\"}", token).ConfigureAwait(false);
                ObsTestHelper.Ok(r, "scalar");
                ObsTestHelper.Assert(r.Text.Contains("scalar result: 1 at", StringComparison.Ordinal), "scalar text: " + r.Text);
            }
        }

        private static async Task QueryBad(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                ToolResult r = await Run(stub, "prom_query", "{\"query\":\"bad(\"}", token).ConfigureAwait(false);
                ObsTestHelper.Error(r, "invalid_argument", "bad promql");
                ObsTestHelper.Assert(r.Text.Contains("unclosed left parenthesis", StringComparison.Ordinal) && r.Text.Contains("bad_data", StringComparison.Ordinal), "upstream message: " + r.Text);
            }
        }

        private static async Task QueryRequired(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                ToolResult r = await Run(stub, "prom_query", "{\"query\":\"  \"}", token).ConfigureAwait(false);
                ObsTestHelper.Error(r, "invalid_argument", "missing query");
                ToolResult t = await Run(stub, "prom_query", "{\"query\":\"up\",\"time\":\"later\"}", token).ConfigureAwait(false);
                ObsTestHelper.Error(t, "invalid_argument", "bad time");
                ObsTestHelper.Assert(stub.Requests.IsEmpty, "no request");
            }
        }

        private static async Task MaxSeries(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub(new PromStubRoutes { SeriesCount = 10 }))
            {
                ToolResult r = await Run(stub, "prom_query", "{\"query\":\"up\"}", token, new Dictionary<string, string> { { "maxSeries", "4" } }).ConfigureAwait(false);
                ObsTestHelper.Ok(r, "capped");
                ObsTestHelper.Assert(r.Truncated && ObsTestHelper.ArrayLength(r, "series") == 4 && ObsTestHelper.Long(r, "omittedSeries") == 6, "maxSeries 4");
                ObsTestHelper.Assert(r.Text.Contains("showing the first 4", StringComparison.Ordinal), "reported");

                ToolResult rows = await Run(stub, "prom_query", "{\"query\":\"up\"}", token, null, new OutputLimits { MaxRows = 2 }).ConfigureAwait(false);
                ObsTestHelper.Assert(ObsTestHelper.ArrayLength(rows, "series") == 2, "row limit caps series");

                ToolResult limit = await Run(stub, "prom_query", "{\"query\":\"up\",\"limit\":5}", token).ConfigureAwait(false);
                ObsTestHelper.Assert(ObsTestHelper.ArrayLength(limit, "series") == 5, "limit argument");
                ToolResult over = await Run(stub, "prom_query", "{\"query\":\"up\",\"limit\":5000}", token, new Dictionary<string, string> { { "maxSeries", "3" } }).ConfigureAwait(false);
                ObsTestHelper.Assert(ObsTestHelper.ArrayLength(over, "series") == 3, "limit capped by maxSeries");
            }
        }

        private static async Task RangeDefaultStep(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                ToolResult r = await Run(stub, "prom_query_range", "{\"query\":\"rate(http_requests_total[5m])\",\"start\":\"2026-10-14T07:00:00Z\",\"end\":\"2026-10-14T08:00:00Z\"}", token).ConfigureAwait(false);
                ObsTestHelper.Ok(r, "range");
                StubRequest req = stub.Last("/api/v1/query_range")!;
                ObsTestHelper.Assert(req.Query["step"] == "62" && req.Query["start"] == "1791961200" && req.Query["end"] == "1791964800", "params: " + req.RawUrl);
                ObsTestHelper.Assert(r.Text.Contains("| series | points | min | max | avg | last |", StringComparison.Ordinal), "summary table: " + r.Text);
                ObsTestHelper.Assert(r.Text.Contains("Values (time=value, UTC):", StringComparison.Ordinal) && r.Text.Contains("07:00:00=0", StringComparison.Ordinal), "values");
                ObsTestHelper.Assert(r.Text.Contains("step 1m2s", StringComparison.Ordinal), "step shown");
                ObsTestHelper.Assert(ObsTestHelper.Long(r, "stepSeconds") == 62 && r.Metrics["stepRaised"] == 0, "structured step");
            }
        }

        private static async Task RangeStepRaised(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                ToolResult r = await Run(stub, "prom_query_range", "{\"query\":\"up\",\"start\":\"2h\",\"step\":\"1s\"}", token, new Dictionary<string, string> { { "maxPointsPerSeries", "100" } }).ConfigureAwait(false);
                ObsTestHelper.Ok(r, "raised");
                long step = Int64.Parse(stub.Last("/api/v1/query_range")!.Query["step"]!, CultureInfo.InvariantCulture);
                ObsTestHelper.Assert(step == 73 && r.Metrics["stepRaised"] == 1, "step raised to 73, got " + step);
                ObsTestHelper.Assert(r.Text.Contains("Note: step raised from 1s to 1m13s", StringComparison.Ordinal), "note: " + r.Text.Split('\n')[0]);

                ToolResult ok = await Run(stub, "prom_query_range", "{\"query\":\"up\",\"start\":\"1h\",\"step\":\"5m\"}", token).ConfigureAwait(false);
                ObsTestHelper.Assert(stub.Last("/api/v1/query_range")!.Query["step"] == "300" && ok.Metrics["stepRaised"] == 0, "5m kept");
                ToolResult bad = await Run(stub, "prom_query_range", "{\"query\":\"up\",\"step\":\"fast\"}", token).ConfigureAwait(false);
                ObsTestHelper.Error(bad, "invalid_argument", "bad step");
            }
        }

        private static async Task RangeTooLong(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                ToolResult r = await Run(stub, "prom_query_range", "{\"query\":\"up\",\"start\":\"3d\"}", token, new Dictionary<string, string> { { "maxRangeHours", "48" } }).ConfigureAwait(false);
                ObsTestHelper.Error(r, "time_range_exceeded", "3d over 48h");
                ToolResult s = await Run(stub, "prom_series", "{\"match\":[\"up\"],\"start\":\"9d\"}", token).ConfigureAwait(false);
                ObsTestHelper.Error(s, "time_range_exceeded", "series 9d over default 168h");
                ObsTestHelper.Assert(stub.Requests.IsEmpty, "no request sent");
            }
        }

        private static async Task RangeOutputLimits(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub(new PromStubRoutes { SeriesCount = 20 }))
            {
                ToolResult r = await Run(stub, "prom_query_range", "{\"query\":\"up\",\"start\":\"6h\"}", token, null, new OutputLimits { MaxLines = 10, MaxBytes = 2048 }).ConfigureAwait(false);
                ObsTestHelper.Ok(r, "limited");
                ObsTestHelper.Assert(r.Truncated && r.OriginalBytes > 2048, "truncated");
                ObsTestHelper.Assert(r.Text.Split('\n').Length <= 12, "within MaxLines");
            }
        }

        private static async Task Alerts(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                ToolResult r = await Run(stub, "prom_alerts", "{}", token).ConfigureAwait(false);
                ObsTestHelper.Ok(r, "alerts");
                ObsTestHelper.Assert(r.Text.StartsWith("2 alert(s)", StringComparison.Ordinal), "count");
                ObsTestHelper.Assert(r.Text.IndexOf("HighErrorRate", StringComparison.Ordinal) < r.Text.IndexOf("DiskFilling", StringComparison.Ordinal), "firing first");
                ObsTestHelper.Assert(r.Text.Contains("API 5xx above 5%", StringComparison.Ordinal) && r.Text.Contains("disk 85%", StringComparison.Ordinal), "summary or description");
                ToolResult firing = await Run(stub, "prom_alerts", "{\"state\":\"firing\"}", token).ConfigureAwait(false);
                ObsTestHelper.Assert(ObsTestHelper.Long(firing, "count") == 1, "state filter");
                ToolResult named = await Run(stub, "prom_alerts", "{\"name\":\"disk\"}", token).ConfigureAwait(false);
                ObsTestHelper.Assert(ObsTestHelper.Long(named, "count") == 1 && named.Text.Contains("DiskFilling", StringComparison.Ordinal), "name filter");
                ToolResult bad = await Run(stub, "prom_alerts", "{\"state\":\"resolved\"}", token).ConfigureAwait(false);
                ObsTestHelper.Error(bad, "invalid_argument", "bad state");
            }
        }

        private static async Task Rules(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                ToolResult r = await Run(stub, "prom_rules", "{}", token).ConfigureAwait(false);
                ObsTestHelper.Ok(r, "rules");
                ObsTestHelper.Assert(ObsTestHelper.Long(r, "count") == 3 && r.Text.Contains("job:requests:rate5m", StringComparison.Ordinal), "all rules");
                ToolResult alerting = await Run(stub, "prom_rules", "{\"type\":\"alert\"}", token).ConfigureAwait(false);
                ObsTestHelper.Ok(alerting, "type");
                ObsTestHelper.Assert(stub.Last("/api/v1/rules")!.Query["type"] == "alert", "type passed");
                ToolResult bad = await Run(stub, "prom_rules", "{\"unhealthyOnly\":true}", token).ConfigureAwait(false);
                ObsTestHelper.Assert(ObsTestHelper.Long(bad, "count") == 1 && bad.Text.Contains("parse error", StringComparison.Ordinal), "unhealthy only");
                ToolResult group = await Run(stub, "prom_rules", "{\"group\":\"API\",\"name\":\"high\"}", token).ConfigureAwait(false);
                ObsTestHelper.Assert(ObsTestHelper.Long(group, "count") == 1, "group and name filter");
            }
        }

        private static async Task Targets(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                ToolResult r = await Run(stub, "prom_targets", "{}", token).ConfigureAwait(false);
                ObsTestHelper.Ok(r, "targets");
                ObsTestHelper.Assert(r.Text.StartsWith("2 target(s), 1 down", StringComparison.Ordinal), "count: " + r.Text.Split('\n')[0]);
                ObsTestHelper.Assert(r.Text.IndexOf("node", StringComparison.Ordinal) < r.Text.IndexOf("| api", StringComparison.Ordinal), "down first");
                ObsTestHelper.Assert(r.Text.Contains("connection refused", StringComparison.Ordinal) && r.Text.Contains("2.6", StringComparison.Ordinal), "error and duration ms");
                ToolResult up = await Run(stub, "prom_targets", "{\"health\":\"up\"}", token).ConfigureAwait(false);
                ObsTestHelper.Assert(ObsTestHelper.Long(up, "count") == 1, "health filter");
                ToolResult job = await Run(stub, "prom_targets", "{\"job\":\"NODE\"}", token).ConfigureAwait(false);
                ObsTestHelper.Assert(ObsTestHelper.Long(job, "count") == 1, "job filter");
                ToolResult any = await Run(stub, "prom_targets", "{\"state\":\"any\"}", token).ConfigureAwait(false);
                ObsTestHelper.Assert(ObsTestHelper.Long(any, "count") == 3 && any.Text.Contains("dropped-job", StringComparison.Ordinal), "dropped included");
                ObsTestHelper.Assert(stub.Last("/api/v1/targets")!.Query["state"] == "any", "state passed");
            }
        }

        private static async Task Series(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                ToolResult r = await Run(stub, "prom_series", "{\"match\":[\"up\",\"{job=\\\"api\\\"}\"],\"start\":\"30m\"}", token).ConfigureAwait(false);
                ObsTestHelper.Ok(r, "series");
                StubRequest req = stub.Last("/api/v1/series")!;
                ObsTestHelper.Assert(req.Query.GetValues("match[]")!.Length == 2, "two match[] params");
                ObsTestHelper.Assert(r.Text.Contains("up{instance=\"i-0\",job=\"api\"}", StringComparison.Ordinal), "label set");
                ToolResult capped = await Run(stub, "prom_series", "{\"match\":[\"up\"],\"limit\":2}", token).ConfigureAwait(false);
                ObsTestHelper.Assert(capped.Truncated && ObsTestHelper.Long(capped, "count") == 2 && stub.Last("/api/v1/series")!.Query["limit"] == "3", "limit+1 sent and capped");
                ToolResult none = await Run(stub, "prom_series", "{\"match\":[]}", token).ConfigureAwait(false);
                ObsTestHelper.Error(none, "invalid_argument", "match required");
            }
        }

        private static async Task LabelValues(CancellationToken token)
        {
            using (ObservabilityStub stub = Stub())
            {
                ToolResult names = await Run(stub, "prom_label_values", "{}", token).ConfigureAwait(false);
                ObsTestHelper.Ok(names, "names");
                ObsTestHelper.Assert(names.Text.StartsWith("3 label name(s)", StringComparison.Ordinal) && names.Text.Contains("__name__", StringComparison.Ordinal), "names");
                ToolResult values = await Run(stub, "prom_label_values", "{\"label\":\"job\",\"match\":[\"up\"]}", token).ConfigureAwait(false);
                ObsTestHelper.Ok(values, "values");
                ObsTestHelper.Assert(values.Text.Contains("value(s) of job", StringComparison.Ordinal) && values.Text.Contains("worker", StringComparison.Ordinal), "values");
                ObsTestHelper.Assert(stub.Last("/api/v1/label/job/values")!.Query["match[]"] == "up", "match passed");
                ToolResult bad = await Run(stub, "prom_label_values", "{\"label\":\"../admin\"}", token).ConfigureAwait(false);
                ObsTestHelper.Error(bad, "invalid_argument", "path injection in label");
                ToolResult missing = await Run(stub, "prom_label_values", "{\"label\":\"nope\"}", token).ConfigureAwait(false);
                ObsTestHelper.Error(missing, "not_found", "404");
            }
        }

        #endregion
    }
}
