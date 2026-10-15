namespace Test.Shared.Connectors.Observability
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Observability;
    using Touchstone.Core;

    /// <summary>
    /// Observability connector cases against the real Prometheus, Loki, and OpenSearch of
    /// <c>docker/testbed/observability/compose.yaml</c>. They run only when <c>HUGO_TESTBED=1</c>; otherwise every case
    /// is skipped with a reason.
    /// </summary>
    public static class ObservabilityTestbedCases
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public static readonly string SuiteId = "testbed.observability";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            Add(cases, "prom-test-connection", "Prometheus test connection reports the version", PromTestConnection);
            Add(cases, "prom-query", "prom_query returns the up series", PromQuery);
            Add(cases, "prom-query-range", "prom_query_range returns points and raises a too-fine step", PromQueryRange);
            Add(cases, "prom-alerts", "prom_alerts lists the always-firing alert", PromAlerts);
            Add(cases, "prom-rules", "prom_rules lists the alerting and recording rules", PromRules);
            Add(cases, "prom-targets", "prom_targets reports the down target", PromTargets);
            Add(cases, "prom-series", "prom_series finds the up series", PromSeries);
            Add(cases, "prom-label-values", "prom_label_values lists jobs and label names", PromLabelValues);
            Add(cases, "prom-bad-query", "A PromQL syntax error maps to invalid_argument", PromBadQuery);
            Add(cases, "loki-test-connection", "Loki test connection with the tenant header", LokiTestConnection);
            Add(cases, "loki-missing-tenant", "Loki without a tenant maps to auth_failed", LokiMissingTenant);
            Add(cases, "loki-labels", "loki_labels and loki_label_values see the seeded streams", LokiLabels);
            Add(cases, "loki-query-range", "loki_query_range returns the seeded lines with caps", LokiQueryRange);
            Add(cases, "loki-metric", "Metric LogQL via loki_query and loki_query_range", LokiMetric);
            Add(cases, "loki-bad-query", "A LogQL parse error maps to invalid_argument", LokiBadQuery);
            Add(cases, "es-test-connection", "OpenSearch test connection detects the distribution", EsTestConnection);
            Add(cases, "es-cluster-health", "es_cluster_health reports the cluster", EsClusterHealth);
            Add(cases, "es-indices", "es_indices lists only allowed indexes", EsIndices);
            Add(cases, "es-mappings", "es_mappings flattens the seeded mapping", EsMappings);
            Add(cases, "es-search", "es_search with a term filter, sort, _source, and aggs", EsSearch);
            Add(cases, "es-count", "es_count counts all and filtered documents", EsCount);
            Add(cases, "es-allow-list", "The allow list refuses hugo-secret and filters hits behind the alias", EsAllowList);
            Add(cases, "es-errors", "Missing indexes and bad queries map to not_found and invalid_argument", EsErrors);
            return new TestSuiteDescriptor(SuiteId, "Observability connectors against the testbed", cases);
        }

        #endregion

        #region Private-Methods

        private static void Add(List<TestCaseDescriptor> cases, string id, string name, Func<CancellationToken, Task> body)
        {
            bool skip = !ObservabilityTestbed.Enabled;
            cases.Add(new TestCaseDescriptor(SuiteId, id, name, body, null, skip, skip ? ObservabilityTestbed.SkipReason : null!));
        }

        private static ConnectorResourceContext Prom()
        {
            return ObsTestHelper.Resource("prometheus", new Dictionary<string, string> { { "baseUrl", ObservabilityTestbed.PrometheusUrl } });
        }

        private static ConnectorResourceContext Loki(bool tenant = true)
        {
            Dictionary<string, string> s = new Dictionary<string, string> { { "baseUrl", ObservabilityTestbed.LokiUrl } };
            if (tenant) s["tenantHeader"] = ObservabilityTestbed.LokiTenant;
            return ObsTestHelper.Resource("loki", s);
        }

        private static ConnectorResourceContext Es(string allowed = "hugo-logs-*\nhugo-alias")
        {
            return ObsTestHelper.Resource("elasticsearch", new Dictionary<string, string> { { "baseUrl", ObservabilityTestbed.OpenSearchUrl }, { "flavor", "OpenSearch" }, { "allowedIndexPatterns", allowed } });
        }

        private static Task<ToolResult> Run(IConnector c, ConnectorResourceContext r, string tool, string json, CancellationToken token, OutputLimits? limits = null)
        {
            return ObsTestHelper.RunAsync(c, tool, json, ObsTestHelper.Context(r, 30, limits), token);
        }

        private static async Task PromTestConnection(CancellationToken token)
        {
            ConnectorHealthResult h = await new PrometheusConnector().TestConnectionAsync(Prom(), token).ConfigureAwait(false);
            ObsTestHelper.Assert(h.Success && h.Details["version"].StartsWith("3.", StringComparison.Ordinal), "health: " + h.Message);
        }

        private static async Task PromQuery(CancellationToken token)
        {
            ToolResult r = await Run(new PrometheusConnector(), Prom(), "prom_query", "{\"query\":\"up\"}", token).ConfigureAwait(false);
            ObsTestHelper.Ok(r, "up");
            ObsTestHelper.Assert(ObsTestHelper.ArrayLength(r, "series") == 2 && r.Text.Contains("job=\"prometheus\"", StringComparison.Ordinal), r.Text);
            ToolResult sum = await Run(new PrometheusConnector(), Prom(), "prom_query", "{\"query\":\"sum by (job) (up)\"}", token).ConfigureAwait(false);
            ObsTestHelper.Assert(sum.Text.Contains("{job=\"unreachable\"} | 0", StringComparison.Ordinal), "aggregation: " + sum.Text);
        }

        private static async Task PromQueryRange(CancellationToken token)
        {
            ToolResult r = await Run(new PrometheusConnector(), Prom(), "prom_query_range", "{\"query\":\"up{job=\\\"prometheus\\\"}\",\"start\":\"2m\",\"step\":\"15s\"}", token).ConfigureAwait(false);
            ObsTestHelper.Ok(r, "range");
            ObsTestHelper.Assert(ObsTestHelper.ArrayLength(r, "series") == 1 && r.Text.Contains("=1", StringComparison.Ordinal), r.Text);
            ToolResult raised = await Run(new PrometheusConnector(), Prom(), "prom_query_range", "{\"query\":\"up\",\"start\":\"6h\",\"step\":\"1s\"}", token).ConfigureAwait(false);
            ObsTestHelper.Ok(raised, "raised");
            ObsTestHelper.Assert(raised.Metrics["stepRaised"] == 1 && raised.Metrics["stepSeconds"] == 91, "step raised to 91: " + raised.Metrics["stepSeconds"]);
        }

        private static async Task PromAlerts(CancellationToken token)
        {
            ToolResult r = await Run(new PrometheusConnector(), Prom(), "prom_alerts", "{\"state\":\"firing\"}", token).ConfigureAwait(false);
            ObsTestHelper.Ok(r, "alerts");
            ObsTestHelper.Assert(r.Text.Contains("HugoTestbedAlwaysFiring", StringComparison.Ordinal) && r.Text.Contains("Always firing", StringComparison.Ordinal), r.Text);
        }

        private static async Task PromRules(CancellationToken token)
        {
            ToolResult r = await Run(new PrometheusConnector(), Prom(), "prom_rules", "{}", token).ConfigureAwait(false);
            ObsTestHelper.Ok(r, "rules");
            ObsTestHelper.Assert(r.Text.Contains("hugo:up:sum", StringComparison.Ordinal) && r.Text.Contains("alerting", StringComparison.Ordinal) && ObsTestHelper.Long(r, "count") == 2, r.Text);
            ToolResult record = await Run(new PrometheusConnector(), Prom(), "prom_rules", "{\"type\":\"record\"}", token).ConfigureAwait(false);
            ObsTestHelper.Assert(ObsTestHelper.Long(record, "count") == 1, "record only");
        }

        private static async Task PromTargets(CancellationToken token)
        {
            ToolResult r = await Run(new PrometheusConnector(), Prom(), "prom_targets", "{}", token).ConfigureAwait(false);
            ObsTestHelper.Ok(r, "targets");
            ObsTestHelper.Assert(r.Text.StartsWith("2 target(s), 1 down", StringComparison.Ordinal) && r.Text.Contains("connection refused", StringComparison.Ordinal), r.Text);
        }

        private static async Task PromSeries(CancellationToken token)
        {
            ToolResult r = await Run(new PrometheusConnector(), Prom(), "prom_series", "{\"match\":[\"up\"],\"start\":\"10m\"}", token).ConfigureAwait(false);
            ObsTestHelper.Ok(r, "series");
            ObsTestHelper.Assert(ObsTestHelper.Long(r, "count") == 2 && r.Text.Contains("up{", StringComparison.Ordinal), r.Text);
            ToolResult capped = await Run(new PrometheusConnector(), Prom(), "prom_series", "{\"match\":[\"{__name__=~\\\"prometheus_.*\\\"}\"],\"limit\":5}", token).ConfigureAwait(false);
            ObsTestHelper.Assert(ObsTestHelper.Long(capped, "count") == 5 && capped.Truncated, "limit respected");
        }

        private static async Task PromLabelValues(CancellationToken token)
        {
            ToolResult jobs = await Run(new PrometheusConnector(), Prom(), "prom_label_values", "{\"label\":\"job\"}", token).ConfigureAwait(false);
            ObsTestHelper.Ok(jobs, "jobs");
            ObsTestHelper.Assert(jobs.Text.Contains("prometheus\n", StringComparison.Ordinal) && jobs.Text.Contains("unreachable\n", StringComparison.Ordinal), jobs.Text);
            ToolResult names = await Run(new PrometheusConnector(), Prom(), "prom_label_values", "{}", token).ConfigureAwait(false);
            ObsTestHelper.Assert(names.Text.Contains("__name__", StringComparison.Ordinal) && names.Text.Contains("instance", StringComparison.Ordinal), names.Text);
            ToolResult metrics = await Run(new PrometheusConnector(), Prom(), "prom_label_values", "{\"label\":\"__name__\",\"match\":[\"{__name__=~\\\"hugo.*\\\"}\"]}", token).ConfigureAwait(false);
            ObsTestHelper.Assert(metrics.Text.Contains("hugo:up:sum", StringComparison.Ordinal), "recorded metric: " + metrics.Text);
        }

        private static async Task PromBadQuery(CancellationToken token)
        {
            ToolResult r = await Run(new PrometheusConnector(), Prom(), "prom_query", "{\"query\":\"sum(up\"}", token).ConfigureAwait(false);
            ObsTestHelper.Error(r, "invalid_argument", "syntax");
            ObsTestHelper.Assert(r.Text.Contains("parse error", StringComparison.Ordinal), r.Text);
        }

        private static async Task LokiTestConnection(CancellationToken token)
        {
            ConnectorHealthResult h = await new LokiConnector().TestConnectionAsync(Loki(), token).ConfigureAwait(false);
            ObsTestHelper.Assert(h.Success && h.Details["tenant"] == ObservabilityTestbed.LokiTenant, "health: " + h.Message);
        }

        private static async Task LokiMissingTenant(CancellationToken token)
        {
            ToolResult r = await Run(new LokiConnector(), Loki(false), "loki_labels", "{}", token).ConfigureAwait(false);
            ObsTestHelper.Error(r, "auth_failed", "no tenant");
            ConnectorHealthResult h = await new LokiConnector().TestConnectionAsync(Loki(false), token).ConfigureAwait(false);
            ObsTestHelper.Assert(!h.Success && h.ErrorCode == "auth_failed", "health auth_failed");
        }

        private static async Task LokiLabels(CancellationToken token)
        {
            ToolResult names = await Run(new LokiConnector(), Loki(), "loki_labels", "{\"start\":\"24h\"}", token).ConfigureAwait(false);
            ObsTestHelper.Ok(names, "labels");
            ObsTestHelper.Assert(names.Text.Contains("app\n", StringComparison.Ordinal) && names.Text.Contains("env\n", StringComparison.Ordinal), names.Text);
            ToolResult apps = await Run(new LokiConnector(), Loki(), "loki_label_values", "{\"label\":\"app\",\"start\":\"24h\",\"query\":\"{env=\\\"testbed\\\"}\"}", token).ConfigureAwait(false);
            ObsTestHelper.Ok(apps, "values");
            ObsTestHelper.Assert(apps.Text.Contains("hugo-api\nhugo-worker\n", StringComparison.Ordinal), apps.Text);
        }

        private static async Task LokiQueryRange(CancellationToken token)
        {
            ToolResult r = await Run(new LokiConnector(), Loki(), "loki_query_range", "{\"query\":\"{app=\\\"hugo-api\\\"}\",\"start\":\"24h\",\"limit\":1000}", token).ConfigureAwait(false);
            ObsTestHelper.Ok(r, "lines");
            ObsTestHelper.Assert(ObsTestHelper.Long(r, "count") == 30 && r.Text.Contains("msg=\"request 29 handled\"", StringComparison.Ordinal), "30 seeded lines: " + r.Text.Split('\n')[0]);
            ToolResult errors = await Run(new LokiConnector(), Loki(), "loki_query_range", "{\"query\":\"{app=\\\"hugo-api\\\"} |= \\\"level=error\\\"\",\"start\":\"24h\"}", token).ConfigureAwait(false);
            ObsTestHelper.Assert(ObsTestHelper.Long(errors, "count") == 6, "6 error lines, got " + ObsTestHelper.Long(errors, "count"));
            ToolResult capped = await Run(new LokiConnector(), Loki(), "loki_query_range", "{\"query\":\"{env=\\\"testbed\\\"}\",\"start\":\"24h\",\"limit\":5,\"direction\":\"forward\"}", token).ConfigureAwait(false);
            ObsTestHelper.Assert(ObsTestHelper.Long(capped, "count") == 5 && capped.Text.Contains("oldest first", StringComparison.Ordinal), "capped forward");
            ToolResult tooLong = await Run(new LokiConnector(), Loki(), "loki_query_range", "{\"query\":\"{app=\\\"hugo-api\\\"}\",\"start\":\"2d\"}", token).ConfigureAwait(false);
            ObsTestHelper.Error(tooLong, "time_range_exceeded", "2d");
        }

        private static async Task LokiMetric(CancellationToken token)
        {
            ToolResult instant = await Run(new LokiConnector(), Loki(), "loki_query", "{\"query\":\"sum by (app) (count_over_time({env=\\\"testbed\\\"}[24h]))\"}", token).ConfigureAwait(false);
            ObsTestHelper.Ok(instant, "instant");
            ObsTestHelper.Assert(instant.Text.Contains("{app=\"hugo-api\"} | 30", StringComparison.Ordinal) && instant.Text.Contains("{app=\"hugo-worker\"} | 10", StringComparison.Ordinal), instant.Text);
            ToolResult range = await Run(new LokiConnector(), Loki(), "loki_query_range", "{\"query\":\"sum(count_over_time({env=\\\"testbed\\\"}[1m]))\",\"start\":\"24h\",\"step\":\"5m\"}", token).ConfigureAwait(false);
            ObsTestHelper.Ok(range, "range metric");
            ObsTestHelper.Assert(range.Text.Contains("| series | points |", StringComparison.Ordinal), range.Text);
        }

        private static async Task LokiBadQuery(CancellationToken token)
        {
            ToolResult r = await Run(new LokiConnector(), Loki(), "loki_query_range", "{\"query\":\"{app=\\\"hugo-api\\\"} |~ \\\"(\\\"\"}", token).ConfigureAwait(false);
            ObsTestHelper.Error(r, "invalid_argument", "bad regex");
        }

        private static async Task EsTestConnection(CancellationToken token)
        {
            ConnectorHealthResult h = await new ElasticsearchConnector().TestConnectionAsync(Es(), token).ConfigureAwait(false);
            ObsTestHelper.Assert(h.Success && h.Details["distribution"] == "OpenSearch" && !h.Details.ContainsKey("flavorWarning"), "health: " + h.Message);
            ConnectorResourceContext wrong = ObsTestHelper.Resource("elasticsearch", new Dictionary<string, string> { { "baseUrl", ObservabilityTestbed.OpenSearchUrl } });
            ConnectorHealthResult w = await new ElasticsearchConnector().TestConnectionAsync(wrong, token).ConfigureAwait(false);
            ObsTestHelper.Assert(w.Success && w.Details.ContainsKey("flavorWarning"), "flavor mismatch warned");
        }

        private static async Task EsClusterHealth(CancellationToken token)
        {
            ToolResult r = await Run(new ElasticsearchConnector(), Es(), "es_cluster_health", "{}", token).ConfigureAwait(false);
            ObsTestHelper.Ok(r, "health");
            ObsTestHelper.Assert(r.Text.Contains("status: green", StringComparison.Ordinal) || r.Text.Contains("status: yellow", StringComparison.Ordinal), r.Text);
        }

        private static async Task EsIndices(CancellationToken token)
        {
            ToolResult r = await Run(new ElasticsearchConnector(), Es(), "es_indices", "{}", token).ConfigureAwait(false);
            ObsTestHelper.Ok(r, "indices");
            ObsTestHelper.Assert(r.Text.StartsWith("1 index(es)", StringComparison.Ordinal) && r.Text.Contains("hugo-logs-2026.10", StringComparison.Ordinal) && !r.Text.Contains("hugo-secret", StringComparison.Ordinal), r.Text);
            ToolResult open = await Run(new ElasticsearchConnector(), Es(String.Empty), "es_indices", "{\"pattern\":\"hugo-*\"}", token).ConfigureAwait(false);
            ObsTestHelper.Assert(open.Text.StartsWith("2 index(es)", StringComparison.Ordinal) && !open.Text.Contains(".plugins", StringComparison.Ordinal), open.Text);
        }

        private static async Task EsMappings(CancellationToken token)
        {
            ToolResult r = await Run(new ElasticsearchConnector(), Es(), "es_mappings", "{\"index\":\"hugo-logs-2026.10\"}", token).ConfigureAwait(false);
            ObsTestHelper.Ok(r, "mappings");
            ObsTestHelper.Assert(r.Text.Contains("  message.keyword: keyword\n", StringComparison.Ordinal) && r.Text.Contains("  http.method: keyword\n", StringComparison.Ordinal) && r.Text.Contains("  @timestamp: date\n", StringComparison.Ordinal), r.Text);
        }

        private static async Task EsSearch(CancellationToken token)
        {
            string args = "{\"index\":\"hugo-logs-*\",\"size\":3,\"query\":{\"bool\":{\"filter\":[{\"term\":{\"level\":\"error\"}}]}},\"sort\":[{\"@timestamp\":\"desc\"}],\"_source\":[\"message\",\"service\"],\"aggs\":{\"by_service\":{\"terms\":{\"field\":\"service\"}}}}";
            ToolResult r = await Run(new ElasticsearchConnector(), Es(), "es_search", args, token).ConfigureAwait(false);
            ObsTestHelper.Ok(r, "search");
            ObsTestHelper.Assert(ObsTestHelper.Long(r, "total") == 5 && ObsTestHelper.ArrayLength(r, "hits") == 3, r.Text);
            ObsTestHelper.Assert(r.Text.Contains("failed with timeout", StringComparison.Ordinal) && r.Text.Contains("\"by_service\"", StringComparison.Ordinal) && r.Text.Contains("sort=[", StringComparison.Ordinal), r.Text);
            ToolResult capped = await Run(new ElasticsearchConnector(), ObsTestHelper.Resource("elasticsearch", new Dictionary<string, string> { { "baseUrl", ObservabilityTestbed.OpenSearchUrl }, { "flavor", "OpenSearch" }, { "allowedIndexPatterns", "hugo-logs-*" }, { "maxHits", "7" } }), "es_search", "{\"index\":\"hugo-logs-2026.10\",\"size\":40}", token).ConfigureAwait(false);
            ObsTestHelper.Assert(ObsTestHelper.ArrayLength(capped, "hits") == 7 && capped.Truncated, "maxHits");
        }

        private static async Task EsCount(CancellationToken token)
        {
            ToolResult all = await Run(new ElasticsearchConnector(), Es(), "es_count", "{\"index\":\"hugo-logs-2026.10\"}", token).ConfigureAwait(false);
            ObsTestHelper.Assert(ObsTestHelper.Long(all, "count") == 50, all.Text);
            ToolResult term = await Run(new ElasticsearchConnector(), Es(), "es_count", "{\"index\":\"hugo-logs-*\",\"query\":{\"term\":{\"service\":\"checkout\"}}}", token).ConfigureAwait(false);
            ObsTestHelper.Assert(ObsTestHelper.Long(term, "count") == 25, term.Text);
        }

        private static async Task EsAllowList(CancellationToken token)
        {
            ToolResult denied = await Run(new ElasticsearchConnector(), Es(), "es_search", "{\"index\":\"hugo-secret\"}", token).ConfigureAwait(false);
            ObsTestHelper.Error(denied, "permission_denied", "secret");
            ToolResult star = await Run(new ElasticsearchConnector(), Es(), "es_search", "{\"index\":\"hugo-*\"}", token).ConfigureAwait(false);
            ObsTestHelper.Error(star, "permission_denied", "wider pattern");
            ToolResult alias = await Run(new ElasticsearchConnector(), Es(), "es_search", "{\"index\":\"hugo-alias\"}", token).ConfigureAwait(false);
            ObsTestHelper.Ok(alias, "alias");
            ObsTestHelper.Assert(ObsTestHelper.ArrayLength(alias, "hits") == 0 && ObsTestHelper.Long(alias, "filtered") == 3 && !alias.Text.Contains("classified", StringComparison.Ordinal), alias.Text);
        }

        private static async Task EsErrors(CancellationToken token)
        {
            ToolResult missing = await Run(new ElasticsearchConnector(), Es(), "es_search", "{\"index\":\"hugo-logs-1999\"}", token).ConfigureAwait(false);
            ObsTestHelper.Error(missing, "not_found", "missing index");
            ToolResult bad = await Run(new ElasticsearchConnector(), Es(), "es_search", "{\"index\":\"hugo-logs-2026.10\",\"query\":{\"bogus_query\":{}}}", token).ConfigureAwait(false);
            ObsTestHelper.Error(bad, "invalid_argument", "bad query");
            ObsTestHelper.Assert(bad.Text.Contains("bogus_query", StringComparison.Ordinal), bad.Text);
        }

        #endregion
    }
}
