namespace Test.Shared.Connectors.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Logging;
    using Touchstone.Core;

    /// <summary>
    /// SolarWinds Observability connector cases against <see cref="SwoStub"/>.
    /// </summary>
    public static class SwoCases
    {
        #region Public-Methods

        /// <summary>
        /// Build the cases.
        /// </summary>
        /// <returns>Cases.</returns>
        public static List<TestCaseDescriptor> Build()
        {
            string suite = "connectors.logging.swo";
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(new TestCaseDescriptor(suite, "validate", "ValidateAsync checks data center, base URL, limits, and token", async ct =>
            {
                SolarWindsObservabilityConnector c = new SolarWindsObservabilityConnector();
                ConnectorValidationResult ok = await c.ValidateAsync(LoggingTestHelper.Resource("solarwinds-observability", new Dictionary<string, string> { { "dataCenter", "eu-01" } }, "t"), ct).ConfigureAwait(false);
                LoggingTestHelper.Assert(ok.IsValid, "eu-01 valid");
                ConnectorValidationResult bad = await c.ValidateAsync(LoggingTestHelper.Resource("solarwinds-observability", new Dictionary<string, string> { { "dataCenter", "mars-01" }, { "maxEvents", "x" } }, null), ct).ConfigureAwait(false);
                List<string> fields = bad.Errors.Select(e => e.Field).ToList();
                LoggingTestHelper.Assert(fields.Contains("dataCenter") && fields.Contains("maxEvents") && fields.Contains("apiToken"), "errors: " + String.Join(",", fields));
                ConnectorValidationResult url = await c.ValidateAsync(LoggingTestHelper.Resource("solarwinds-observability", new Dictionary<string, string> { { "baseUrl", "not a url" } }, "t"), ct).ConfigureAwait(false);
                LoggingTestHelper.Assert(url.Errors.Any(e => e.Field == "baseUrl"), "bad baseUrl");

                SwoSettings s = SwoSettings.FromResource(LoggingTestHelper.Resource("solarwinds-observability", new Dictionary<string, string> { { "dataCenter", "AP-01" } }, "t"));
                LoggingTestHelper.Assert(s.BaseUrl == "https://api.ap-01.cloud.solarwinds.com/v1", "data center URL: " + s.BaseUrl);
                s = SwoSettings.FromResource(LoggingTestHelper.Resource("solarwinds-observability", new Dictionary<string, string>(), "t"));
                LoggingTestHelper.Assert(s.BaseUrl == "https://api.na-01.cloud.solarwinds.com/v1", "default na-01");
            }));

            cases.Add(new TestCaseDescriptor(suite, "test-connection", "TestConnectionAsync lists one entity; a bad token is auth_failed", async ct =>
            {
                using (SwoStub stub = new SwoStub())
                {
                    SolarWindsObservabilityConnector c = new SolarWindsObservabilityConnector();
                    ConnectorHealthResult ok = await c.TestConnectionAsync(Resource(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.Assert(ok.Success, "ok: " + ok.Message);
                    LoggingTestHelper.Assert(stub.LastQuery("/v1/entities")["pageSize"] == "1", "page size 1 probe");
                    ConnectorHealthResult bad = await c.TestConnectionAsync(Resource(stub, "wrong"), ct).ConfigureAwait(false);
                    LoggingTestHelper.Assert(!bad.Success && bad.ErrorCode == "auth_failed", "auth_failed: " + bad.ErrorCode + " " + bad.Message);
                }
            }));

            cases.Add(new TestCaseDescriptor(suite, "search-and-paging", "logs_search sends ISO times and pages with skipToken (link form) without gaps", async ct =>
            {
                using (SwoStub stub = new SwoStub())
                {
                    List<StubLogEvent> ev = stub.Events;
                    SolarWindsObservabilityConnector c = new SolarWindsObservabilityConnector();
                    string args = Window(ev[100].Time, ev[199].Time, "\"limit\":30");
                    List<string> all = new List<string>();
                    int pages = 0;
                    while (true)
                    {
                        ToolResult r = await LoggingTestHelper.RunAsync(c, "logs_search", args, Context(stub), ct).ConfigureAwait(false);
                        LoggingTestHelper.AssertOk(r, "page " + pages);
                        all.AddRange(LoggingTestHelper.EventIds(r));
                        pages++;
                        if (pages == 1)
                        {
                            LoggingTestHelper.Assert(stub.LastQuery("/v1/logs")["startTime"] == Format(ev[100].Time.AddSeconds(-15)), "startTime ISO");
                            LoggingTestHelper.Assert(stub.LastQuery("/v1/logs")["pageSize"] == "30", "pageSize");
                            LoggingTestHelper.Assert(r.Text.Contains(Format(ev[199].Time) + " " + ev[199].Host + " " + ev[199].Program + ": " + ev[199].Message, StringComparison.Ordinal), "line format");
                        }
                        string? cursor = LoggingTestHelper.StructuredString(r, "nextCursor");
                        if (cursor == null || pages > 10) break;
                        args = "{\"cursor\":\"" + cursor + "\",\"limit\":30}";
                    }
                    LoggingTestHelper.Assert(pages == 4, "4 pages, got " + pages);
                    LoggingTestHelper.Assert(all.Count == 100 && all.Distinct().Count() == 100, "100 unique events, got " + all.Count);
                    LoggingTestHelper.Assert(stub.LastQuery("/v1/logs")["skipToken"] != null && stub.LastQuery("/v1/logs")["startTime"] != null, "skipToken replayed with the window");
                }
            }));

            cases.Add(new TestCaseDescriptor(suite, "filters", "group, entityId, direction, and query reach the API; system is rejected", async ct =>
            {
                using (SwoStub stub = new SwoStub())
                {
                    SolarWindsObservabilityConnector c = new SolarWindsObservabilityConnector();
                    ToolResult r = await LoggingTestHelper.RunAsync(c, "logs_search", "{\"startTime\":\"1h\",\"query\":\"ERROR\",\"providerOptions\":{\"entityId\":\"e-1003\",\"direction\":\"forward\"}}", Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertOk(r, "entity search");
                    NameValueCollection q = stub.LastQuery("/v1/logs");
                    LoggingTestHelper.Assert(q["entityId"] == "e-1003" && q["direction"] == "forward" && q["filter"] == "ERROR", "params sent");
                    LoggingTestHelper.Assert(!r.Text.Contains("web-0", StringComparison.Ordinal) && r.Text.Contains("db-01", StringComparison.Ordinal), "only db-01");

                    ConnectorInvocationContext withGroup = Context(stub, 30, null, new Dictionary<string, string> { { "defaultGroup", "prod-web" } });
                    r = await LoggingTestHelper.RunAsync(c, "logs_search", "{}", withGroup, ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertOk(r, "default group");
                    LoggingTestHelper.Assert(stub.LastQuery("/v1/logs")["group"] == "prod-web", "defaultGroup applied");

                    r = await LoggingTestHelper.RunAsync(c, "logs_search", "{\"providerOptions\":{\"system\":\"web-01\"}}", Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertError(r, "invalid_argument", "system unsupported");
                }
            }));

            cases.Add(new TestCaseDescriptor(suite, "cursor-rejects-new-arguments", "A cursor call refuses a new query, window, or providerOptions instead of ignoring them", async ct =>
            {
                using (SwoStub stub = new SwoStub())
                {
                    List<StubLogEvent> ev = stub.Events;
                    SolarWindsObservabilityConnector c = new SolarWindsObservabilityConnector();
                    ToolResult first = await LoggingTestHelper.RunAsync(c, "logs_search", Window(ev[100].Time, ev[199].Time, "\"limit\":30"), Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertOk(first, "first page");
                    string? cursor = LoggingTestHelper.StructuredString(first, "nextCursor");
                    LoggingTestHelper.Assert(cursor != null, "first page has a cursor");
                    foreach (string extra in new[] { "\"query\":\"other\"", "\"startTime\":\"2h\"", "\"endTime\":\"now\"", "\"providerOptions\":{\"group\":\"x\"}" })
                    {
                        ToolResult conflict = await LoggingTestHelper.RunAsync(c, "logs_search", "{\"cursor\":\"" + cursor + "\"," + extra + "}", Context(stub), ct).ConfigureAwait(false);
                        LoggingTestHelper.AssertError(conflict, "invalid_argument", "search cursor with " + extra);
                    }
                    ToolResult next = await LoggingTestHelper.RunAsync(c, "logs_search", "{\"cursor\":\"" + cursor + "\",\"limit\":10}", Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertOk(next, "limit may change with a cursor");

                    ToolResult tail = await LoggingTestHelper.RunAsync(c, "logs_tail", "{\"limit\":5}", Context(stub), ct).ConfigureAwait(false);
                    string? tailCursor = LoggingTestHelper.StructuredString(tail, "nextCursor");
                    LoggingTestHelper.Assert(tailCursor != null, "tail cursor");
                    ToolResult tailConflict = await LoggingTestHelper.RunAsync(c, "logs_tail", "{\"cursor\":\"" + tailCursor + "\",\"query\":\"other\"}", Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertError(tailConflict, "invalid_argument", "tail cursor with a new query");
                }
            }));

            cases.Add(new TestCaseDescriptor(suite, "limits-and-caps", "limit is capped by maxEvents and windows beyond maxTimeRangeHours are rejected", async ct =>
            {
                using (SwoStub stub = new SwoStub())
                {
                    SolarWindsObservabilityConnector c = new SolarWindsObservabilityConnector();
                    ConnectorInvocationContext ctx = Context(stub, 30, null, new Dictionary<string, string> { { "maxEvents", "20" }, { "maxTimeRangeHours", "1" } });
                    ToolResult r = await LoggingTestHelper.RunAsync(c, "logs_search", "{\"limit\":999}", ctx, ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertOk(r, "capped");
                    LoggingTestHelper.Assert(stub.LastQuery("/v1/logs")["pageSize"] == "20", "pageSize capped");
                    ToolResult tooLong = await LoggingTestHelper.RunAsync(c, "logs_search", "{\"startTime\":\"90m\"}", ctx, ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertError(tooLong, "time_range_exceeded", "90m");
                    ToolResult metric = await LoggingTestHelper.RunAsync(c, "swo_query_metric", "{\"name\":\"system.cpu.utilization\",\"startTime\":\"2h\"}", ctx, ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertError(metric, "time_range_exceeded", "metric 2h");
                }
            }));

            cases.Add(new TestCaseDescriptor(suite, "tail", "logs_tail returns the newest events and the cursor returns only newer ones", async ct =>
            {
                using (SwoStub stub = new SwoStub())
                {
                    SolarWindsObservabilityConnector c = new SolarWindsObservabilityConnector();
                    List<StubLogEvent> ev = stub.Events;
                    ToolResult r = await LoggingTestHelper.RunAsync(c, "logs_tail", "{\"limit\":5}", Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertOk(r, "tail");
                    List<string> ids = LoggingTestHelper.EventIds(r);
                    LoggingTestHelper.Assert(ids.Count == 5 && ids.Last() == ev.Last().Id.ToString(CultureInfo.InvariantCulture), "newest 5, newest last");
                    string? cursor = LoggingTestHelper.StructuredString(r, "nextCursor");
                    LoggingTestHelper.Assert(cursor != null, "cursor");

                    ToolResult none = await LoggingTestHelper.RunAsync(c, "logs_tail", "{\"cursor\":\"" + cursor + "\"}", Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertOk(none, "tail again");
                    LoggingTestHelper.Assert(LoggingTestHelper.EventIds(none).Count == 0, "nothing new, got " + LoggingTestHelper.EventIds(none).Count);
                    string? cursor2 = LoggingTestHelper.StructuredString(none, "nextCursor");

                    StubLogEvent added = stub.AddEvent("cache warmed abc");
                    ToolResult more = await LoggingTestHelper.RunAsync(c, "logs_tail", "{\"cursor\":\"" + cursor2 + "\"}", Context(stub), ct).ConfigureAwait(false);
                    List<string> newIds = LoggingTestHelper.EventIds(more);
                    LoggingTestHelper.Assert(newIds.Count == 1 && newIds[0] == added.Id.ToString(CultureInfo.InvariantCulture), "only the new event, got " + newIds.Count);
                }
            }));

            cases.Add(new TestCaseDescriptor(suite, "entities", "swo_list_entities pages with a cursor and swo_get_entity renders details or not_found", async ct =>
            {
                using (SwoStub stub = new SwoStub())
                {
                    SolarWindsObservabilityConnector c = new SolarWindsObservabilityConnector();
                    ToolResult page1 = await LoggingTestHelper.RunAsync(c, "swo_list_entities", "{\"type\":\"Host\",\"limit\":2}", Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertOk(page1, "list");
                    LoggingTestHelper.Assert(page1.Text.Contains("| e-1001 | Host | web-01 | GOOD | no |", StringComparison.Ordinal), "row: " + page1.Text);
                    string? cursor = LoggingTestHelper.StructuredString(page1, "nextCursor");
                    LoggingTestHelper.Assert(cursor != null, "next cursor");
                    ToolResult page2 = await LoggingTestHelper.RunAsync(c, "swo_list_entities", "{\"type\":\"Host\",\"limit\":2,\"cursor\":\"" + cursor + "\"}", Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertOk(page2, "page 2");
                    LoggingTestHelper.Assert(page2.Text.Contains("db-01", StringComparison.Ordinal) && LoggingTestHelper.StructuredString(page2, "nextCursor") == null, "last page");

                    ToolResult one = await LoggingTestHelper.RunAsync(c, "swo_get_entity", "{\"id\":\"e-1003\"}", Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertOk(one, "get");
                    LoggingTestHelper.Assert(one.Text.Contains("health: MODERATE", StringComparison.Ordinal) && one.Text.Contains("role: db", StringComparison.Ordinal) && one.Text.Contains("osName: Ubuntu", StringComparison.Ordinal), "details: " + one.Text);
                    ToolResult missing = await LoggingTestHelper.RunAsync(c, "swo_get_entity", "{\"id\":\"e-nope\"}", Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertError(missing, "not_found", "404");
                    ToolResult noId = await LoggingTestHelper.RunAsync(c, "swo_get_entity", "{}", Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertError(noId, "invalid_argument", "id required");
                }
            }));

            cases.Add(new TestCaseDescriptor(suite, "metrics", "swo_query_metric summarizes groupings, builds the entity filter, and maps unknown metrics to not_found", async ct =>
            {
                using (SwoStub stub = new SwoStub())
                {
                    SolarWindsObservabilityConnector c = new SolarWindsObservabilityConnector();
                    ToolResult r = await LoggingTestHelper.RunAsync(c, "swo_query_metric", "{\"name\":\"system.cpu.utilization\",\"entityId\":\"e-1002\",\"startTime\":\"30m\",\"aggregateBy\":\"max\"}", Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertOk(r, "metric");
                    NameValueCollection q = stub.LastQuery("/v1/metrics/system.cpu.utilization/measurements");
                    LoggingTestHelper.Assert(q["filter"] == "id:[e-1002]" && q["aggregateBy"] == "MAX" && q["seriesType"] == "TIMESERIES", "query params");
                    LoggingTestHelper.Assert(r.Text.Contains("## id=e-1002", StringComparison.Ordinal) && r.Text.Contains("min ", StringComparison.Ordinal) && r.Text.Contains("null", StringComparison.Ordinal), "summary and null point: " + r.Text);

                    ToolResult paged = await LoggingTestHelper.RunAsync(c, "swo_query_metric", "{\"name\":\"system.cpu.utilization\",\"limit\":2}", Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.Assert(LoggingTestHelper.StructuredString(paged, "nextCursor") != null, "groupings paged");

                    ToolResult unknown = await LoggingTestHelper.RunAsync(c, "swo_query_metric", "{\"name\":\"no.such.metric\"}", Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertError(unknown, "not_found", "unknown metric");
                    ToolResult badAgg = await LoggingTestHelper.RunAsync(c, "swo_query_metric", "{\"name\":\"system.cpu.utilization\",\"aggregateBy\":\"MEDIAN\"}", Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertError(badAgg, "invalid_argument", "bad aggregation");
                }
            }));

            cases.Add(new TestCaseDescriptor(suite, "rate-limited", "HTTP 429 becomes rate_limited with Retry-After", async ct =>
            {
                using (SwoStub stub = new SwoStub())
                {
                    stub.FailNextWithRateLimit(1);
                    ToolResult r = await LoggingTestHelper.RunAsync(new SolarWindsObservabilityConnector(), "swo_list_entities", "{}", Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertError(r, "rate_limited", "429");
                    LoggingTestHelper.Assert(r.Metrics.ContainsKey("retryAfterSeconds") && r.Metrics["retryAfterSeconds"] == 7, "retry after 7");
                }
            }));

            cases.Add(new TestCaseDescriptor(suite, "auth-failed", "HTTP 401 becomes auth_failed on every tool", async ct =>
            {
                using (SwoStub stub = new SwoStub())
                {
                    SolarWindsObservabilityConnector c = new SolarWindsObservabilityConnector();
                    foreach (IConnectorTool t in c.Tools)
                    {
                        string args = t.Descriptor.Name == "swo_get_entity" ? "{\"id\":\"e-1001\"}" : (t.Descriptor.Name == "swo_query_metric" ? "{\"name\":\"system.cpu.utilization\"}" : "{}");
                        ToolResult r = await t.ExecuteAsync(LoggingTestHelper.Context(Resource(stub, "bad-token")), new ToolArguments(args), ct).ConfigureAwait(false);
                        LoggingTestHelper.AssertError(r, "auth_failed", t.Descriptor.Name);
                        LoggingTestHelper.Assert(!r.Text.Contains("bad-token", StringComparison.Ordinal), "token not echoed");
                    }
                }
            }));

            cases.Add(new TestCaseDescriptor(suite, "timeout-and-cancellation", "Slow upstream returns timeout; caller cancellation throws", async ct =>
            {
                using (SwoStub stub = new SwoStub())
                {
                    stub.DelayMilliseconds = 4000;
                    SolarWindsObservabilityConnector c = new SolarWindsObservabilityConnector();
                    Stopwatch sw = Stopwatch.StartNew();
                    ToolResult r = await LoggingTestHelper.RunAsync(c, "logs_search", "{}", Context(stub, 1), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertError(r, "timeout", "slow");
                    LoggingTestHelper.Assert(sw.Elapsed < TimeSpan.FromSeconds(3), "prompt timeout");

                    using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        cts.CancelAfter(300);
                        bool cancelled = false;
                        try
                        {
                            await LoggingTestHelper.RunAsync(c, "swo_list_entities", "{}", Context(stub), cts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            cancelled = true;
                        }
                        LoggingTestHelper.Assert(cancelled, "cancelled");
                    }
                }
            }));

            return cases;
        }

        #endregion

        #region Private-Methods

        private static ConnectorResourceContext Resource(SwoStub stub, string? token = null, Dictionary<string, string>? extra = null)
        {
            Dictionary<string, string> settings = new Dictionary<string, string> { { "baseUrl", stub.ApiBaseUrl } };
            if (extra != null) foreach (KeyValuePair<string, string> kv in extra) settings[kv.Key] = kv.Value;
            return LoggingTestHelper.Resource("solarwinds-observability", settings, token ?? stub.ApiToken);
        }

        private static ConnectorInvocationContext Context(SwoStub stub, double timeoutSeconds = 30, OutputLimits? limits = null, Dictionary<string, string>? extra = null)
        {
            return LoggingTestHelper.Context(Resource(stub, null, extra), timeoutSeconds, limits);
        }

        private static string Window(DateTime first, DateTime last, string extra)
        {
            return "{\"startTime\":\"" + Format(first.AddSeconds(-15)) + "\",\"endTime\":\"" + Format(last.AddSeconds(15)) + "\"" + (String.IsNullOrEmpty(extra) ? String.Empty : "," + extra) + "}";
        }

        private static string Format(DateTime utc)
        {
            return utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
