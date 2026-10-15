namespace Test.Shared.Connectors.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Logging;
    using Touchstone.Core;

    /// <summary>
    /// Papertrail connector cases against <see cref="PapertrailStub"/>.
    /// </summary>
    public static class PapertrailCases
    {
        #region Public-Methods

        /// <summary>
        /// Build the cases.
        /// </summary>
        /// <returns>Cases.</returns>
        public static List<TestCaseDescriptor> Build()
        {
            string suite = "connectors.logging.papertrail";
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(new TestCaseDescriptor(suite, "validate", "ValidateAsync flags a missing token, bad URL, and out-of-range limits", async ct =>
            {
                PapertrailConnector c = new PapertrailConnector();
                ConnectorValidationResult ok = await c.ValidateAsync(LoggingTestHelper.Resource("papertrail", new Dictionary<string, string>(), "t"), ct).ConfigureAwait(false);
                LoggingTestHelper.Assert(ok.IsValid, "defaults valid");
                ConnectorValidationResult bad = await c.ValidateAsync(LoggingTestHelper.Resource("papertrail", new Dictionary<string, string> { { "baseUrl", "ftp://x" }, { "maxEvents", "0" }, { "maxTimeRangeHours", "99999" } }, null), ct).ConfigureAwait(false);
                List<string> fields = bad.Errors.Select(e => e.Field).ToList();
                LoggingTestHelper.Assert(fields.Contains("baseUrl") && fields.Contains("maxEvents") && fields.Contains("maxTimeRangeHours") && fields.Contains("apiToken"), "errors: " + String.Join(",", fields));
            }));

            cases.Add(new TestCaseDescriptor(suite, "test-connection", "TestConnectionAsync lists systems; a bad token is auth_failed", async ct =>
            {
                using (PapertrailStub stub = new PapertrailStub())
                {
                    PapertrailConnector c = new PapertrailConnector();
                    ConnectorHealthResult ok = await c.TestConnectionAsync(Resource(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.Assert(ok.Success && ok.Details["systems"] == "3", "ok: " + ok.Message);
                    ConnectorHealthResult bad = await c.TestConnectionAsync(Resource(stub, "wrong"), ct).ConfigureAwait(false);
                    LoggingTestHelper.Assert(!bad.Success && bad.ErrorCode == "auth_failed", "auth_failed: " + bad.ErrorCode);
                    LoggingTestHelper.Assert(!bad.Message.Contains("wrong", StringComparison.Ordinal), "token not echoed");
                }
            }));

            cases.Add(new TestCaseDescriptor(suite, "search-basic", "logs_search returns compact lines in the window and sends epoch times and the query", async ct =>
            {
                using (PapertrailStub stub = new PapertrailStub())
                {
                    List<StubLogEvent> ev = stub.Events;
                    string args = Window(ev[100].Time, ev[199].Time, "\"limit\":200");
                    ToolResult r = await LoggingTestHelper.RunAsync(new PapertrailConnector(), "logs_search", args, Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertOk(r, "search");
                    List<string> ids = LoggingTestHelper.EventIds(r);
                    LoggingTestHelper.Assert(ids.Count == 100, "100 events, got " + ids.Count);
                    LoggingTestHelper.Assert(ids.First() == ev[100].Id.ToString(CultureInfo.InvariantCulture) && ids.Last() == ev[199].Id.ToString(CultureInfo.InvariantCulture), "oldest first");
                    LoggingTestHelper.Assert(LoggingTestHelper.StructuredString(r, "nextCursor") == null, "no more pages");
                    string firstLine = Format(ev[100].Time) + " " + ev[100].Host + " " + ev[100].Program + ": " + ev[100].Message;
                    LoggingTestHelper.Assert(r.Text.Contains(firstLine, StringComparison.Ordinal), "line format with UTC time: " + firstLine);
                    string? minTime = stub.LastQuery("/api/v1/events/search.json")["min_time"];
                    LoggingTestHelper.Assert(minTime == new DateTimeOffset(ev[100].Time.AddSeconds(-15)).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), "min_time epoch");

                    ToolResult q = await LoggingTestHelper.RunAsync(new PapertrailConnector(), "logs_search", Window(ev[100].Time, ev[199].Time, "\"query\":\"ERROR\""), Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertOk(q, "query");
                    LoggingTestHelper.Assert(LoggingTestHelper.EventIds(q).Count == 10, "10 errors");
                    LoggingTestHelper.Assert(stub.LastQuery("/api/v1/events/search.json")["q"] == "ERROR", "q sent");
                }
            }));

            cases.Add(new TestCaseDescriptor(suite, "search-paging", "Following nextCursor pages backward through the window without gaps or duplicates", async ct =>
            {
                using (PapertrailStub stub = new PapertrailStub())
                {
                    List<StubLogEvent> ev = stub.Events;
                    PapertrailConnector c = new PapertrailConnector();
                    List<string> all = new List<string>();
                    string args = Window(ev[100].Time, ev[199].Time, "\"limit\":30");
                    int pages = 0;
                    while (true)
                    {
                        ToolResult r = await LoggingTestHelper.RunAsync(c, "logs_search", args, Context(stub), ct).ConfigureAwait(false);
                        LoggingTestHelper.AssertOk(r, "page " + pages);
                        all.AddRange(LoggingTestHelper.EventIds(r));
                        pages++;
                        string? cursor = LoggingTestHelper.StructuredString(r, "nextCursor");
                        if (cursor == null || pages > 10) break;
                        args = "{\"cursor\":\"" + cursor + "\",\"limit\":30}";
                    }
                    List<string> expected = ev.Skip(100).Take(100).Select(e => e.Id.ToString(CultureInfo.InvariantCulture)).ToList();
                    LoggingTestHelper.Assert(pages == 4, "4 pages, got " + pages);
                    LoggingTestHelper.Assert(all.Count == 100 && all.Distinct().Count() == 100 && expected.All(all.Contains), "all 100 events once, got " + all.Count);
                    LoggingTestHelper.Assert(stub.LastQuery("/api/v1/events/search.json")["max_id"] != null, "max_id paging");
                }
            }));

            cases.Add(new TestCaseDescriptor(suite, "search-output-limits", "When output limits drop events, the cursor resumes at the oldest shown event", async ct =>
            {
                using (PapertrailStub stub = new PapertrailStub())
                {
                    List<StubLogEvent> ev = stub.Events;
                    PapertrailConnector c = new PapertrailConnector();
                    OutputLimits limits = new OutputLimits { MaxLines = 14 };
                    List<string> all = new List<string>();
                    string args = Window(ev[100].Time, ev[199].Time, "\"limit\":30");
                    bool sawTruncation = false;
                    for (int i = 0; i < 20; i++)
                    {
                        ToolResult r = await LoggingTestHelper.RunAsync(c, "logs_search", args, Context(stub, 30, limits), ct).ConfigureAwait(false);
                        LoggingTestHelper.AssertOk(r, "page");
                        sawTruncation = sawTruncation || r.Truncated;
                        LoggingTestHelper.Assert(r.Text.Split('\n').Length <= 15, "text within MaxLines");
                        all.AddRange(LoggingTestHelper.EventIds(r));
                        string? cursor = LoggingTestHelper.StructuredString(r, "nextCursor");
                        if (cursor == null) break;
                        args = "{\"cursor\":\"" + cursor + "\",\"limit\":30}";
                    }
                    LoggingTestHelper.Assert(sawTruncation, "truncation reported");
                    LoggingTestHelper.Assert(all.Count == 100 && all.Distinct().Count() == 100, "no events lost, got " + all.Count);
                }
            }));

            cases.Add(new TestCaseDescriptor(suite, "cursor-rejects-new-arguments", "A cursor call refuses a new query, window, or providerOptions instead of ignoring them", async ct =>
            {
                using (PapertrailStub stub = new PapertrailStub())
                {
                    List<StubLogEvent> ev = stub.Events;
                    PapertrailConnector c = new PapertrailConnector();
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

            cases.Add(new TestCaseDescriptor(suite, "limits-and-caps", "limit is capped by maxEvents; windows beyond maxTimeRangeHours are rejected before any request", async ct =>
            {
                using (PapertrailStub stub = new PapertrailStub())
                {
                    PapertrailConnector c = new PapertrailConnector();
                    ConnectorInvocationContext ctx = Context(stub, 30, null, new Dictionary<string, string> { { "maxEvents", "25" }, { "maxTimeRangeHours", "2" } });
                    ToolResult r = await LoggingTestHelper.RunAsync(c, "logs_search", "{\"startTime\":\"2h\",\"limit\":5000}", ctx, ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertOk(r, "capped search");
                    LoggingTestHelper.Assert(stub.LastQuery("/api/v1/events/search.json")["limit"] == "25", "limit capped to maxEvents");
                    LoggingTestHelper.Assert(LoggingTestHelper.EventIds(r).Count == 25, "25 events");
                    int before = stub.Requests.Count;
                    ToolResult tooLong = await LoggingTestHelper.RunAsync(c, "logs_search", "{\"startTime\":\"3h\"}", ctx, ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertError(tooLong, "time_range_exceeded", "3h window");
                    ToolResult volume = await LoggingTestHelper.RunAsync(c, "logs_volume", "{\"startTime\":\"1d\"}", ctx, ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertError(volume, "time_range_exceeded", "volume 1d window");
                    LoggingTestHelper.Assert(stub.Requests.Count == before, "no upstream call for rejected windows");
                    ToolResult bad = await LoggingTestHelper.RunAsync(c, "logs_search", "{\"startTime\":\"soon\"}", ctx, ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertError(bad, "invalid_argument", "bad time");
                }
            }));

            cases.Add(new TestCaseDescriptor(suite, "filters", "Group names resolve to IDs, systems pass through, defaults apply, and unsupported options are rejected", async ct =>
            {
                using (PapertrailStub stub = new PapertrailStub())
                {
                    PapertrailConnector c = new PapertrailConnector();
                    ToolResult r = await LoggingTestHelper.RunAsync(c, "logs_search", "{\"startTime\":\"30m\",\"providerOptions\":{\"group\":\"web servers\"}}", Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertOk(r, "group search");
                    LoggingTestHelper.Assert(stub.LastQuery("/api/v1/events/search.json")["group_id"] == "32", "group name resolved to 32");
                    LoggingTestHelper.Assert(r.Text.Contains("web-0", StringComparison.Ordinal) && !r.Text.Contains("db-01", StringComparison.Ordinal), "only web systems");

                    r = await LoggingTestHelper.RunAsync(c, "logs_search", "{\"startTime\":\"30m\",\"providerOptions\":{\"system\":\"db-01\"}}", Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertOk(r, "system search");
                    LoggingTestHelper.Assert(stub.LastQuery("/api/v1/events/search.json")["system_id"] == "db-01", "system passed");
                    LoggingTestHelper.Assert(!r.Text.Contains("web-0", StringComparison.Ordinal), "only db-01");

                    ConnectorInvocationContext withDefault = Context(stub, 30, null, new Dictionary<string, string> { { "defaultGroup", "32" } });
                    r = await LoggingTestHelper.RunAsync(c, "logs_tail", "{}", withDefault, ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertOk(r, "default group tail");
                    LoggingTestHelper.Assert(stub.LastQuery("/api/v1/events/search.json")["group_id"] == "32", "defaultGroup applied");

                    r = await LoggingTestHelper.RunAsync(c, "logs_search", "{\"providerOptions\":{\"group\":\"No Such Group\"}}", Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertError(r, "not_found", "unknown group");
                    r = await LoggingTestHelper.RunAsync(c, "logs_search", "{\"providerOptions\":{\"entityId\":\"e-1\"}}", Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertError(r, "invalid_argument", "entityId unsupported");
                    r = await LoggingTestHelper.RunAsync(new SolarWindsObservabilityConnector(), "logs_search", "{}", LoggingTestHelper.Context(Resource(stub)), ct).ConfigureAwait(false);
                    LoggingTestHelper.Assert(r.IsError, "swo connector against a papertrail resource fails cleanly");
                }
            }));

            cases.Add(new TestCaseDescriptor(suite, "tail", "logs_tail returns the newest events and the cursor returns only newer ones", async ct =>
            {
                using (PapertrailStub stub = new PapertrailStub())
                {
                    PapertrailConnector c = new PapertrailConnector();
                    List<StubLogEvent> ev = stub.Events;
                    ToolResult r = await LoggingTestHelper.RunAsync(c, "logs_tail", "{\"limit\":5}", Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertOk(r, "tail");
                    List<string> ids = LoggingTestHelper.EventIds(r);
                    LoggingTestHelper.Assert(ids.Count == 5 && ids.Last() == ev.Last().Id.ToString(CultureInfo.InvariantCulture), "newest 5");
                    string? cursor = LoggingTestHelper.StructuredString(r, "nextCursor");
                    LoggingTestHelper.Assert(cursor != null, "tail cursor");

                    ToolResult none = await LoggingTestHelper.RunAsync(c, "logs_tail", "{\"cursor\":\"" + cursor + "\"}", Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.Assert(LoggingTestHelper.EventIds(none).Count == 0, "nothing new yet");
                    string? cursor2 = LoggingTestHelper.StructuredString(none, "nextCursor");
                    LoggingTestHelper.Assert(cursor2 != null, "cursor kept");

                    StubLogEvent added = stub.AddEvent("web-02", "deploy finished xyz");
                    ToolResult more = await LoggingTestHelper.RunAsync(c, "logs_tail", "{\"cursor\":\"" + cursor2 + "\"}", Context(stub), ct).ConfigureAwait(false);
                    List<string> newIds = LoggingTestHelper.EventIds(more);
                    LoggingTestHelper.Assert(newIds.Count == 1 && newIds[0] == added.Id.ToString(CultureInfo.InvariantCulture), "only the new event");
                    LoggingTestHelper.Assert(more.Text.Contains("deploy finished xyz", StringComparison.Ordinal), "new message shown");
                    LoggingTestHelper.Assert(stub.LastQuery("/api/v1/events/search.json")["min_id"] != null, "min_id sent");

                    ToolResult wrong = await LoggingTestHelper.RunAsync(c, "logs_search", "{\"cursor\":\"" + cursor + "\"}", Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertError(wrong, "invalid_argument", "tail cursor rejected by search");
                }
            }));

            cases.Add(new TestCaseDescriptor(suite, "list-tools", "logs_list_systems, logs_list_groups, and logs_list_saved_searches render tables and JSON", async ct =>
            {
                using (PapertrailStub stub = new PapertrailStub())
                {
                    PapertrailConnector c = new PapertrailConnector();
                    ToolResult systems = await LoggingTestHelper.RunAsync(c, "logs_list_systems", "{}", Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertOk(systems, "systems");
                    LoggingTestHelper.Assert(systems.Text.Contains("| 103 | db-01 | 10.0.0.13 |", StringComparison.Ordinal), "system row: " + systems.Text);
                    ToolResult filtered = await LoggingTestHelper.RunAsync(c, "logs_list_systems", "{\"nameContains\":\"WEB\"}", Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.Assert(filtered.Text.StartsWith("Papertrail systems: 2 items", StringComparison.Ordinal), "filter");

                    ToolResult groups = await LoggingTestHelper.RunAsync(c, "logs_list_groups", "{}", Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertOk(groups, "groups");
                    LoggingTestHelper.Assert(groups.Text.Contains("| 32 | Web Servers | web-* | web-01, web-02 |", StringComparison.Ordinal), "group row: " + groups.Text);

                    ToolResult saved = await LoggingTestHelper.RunAsync(c, "logs_list_saved_searches", "{}", Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertOk(saved, "saved");
                    LoggingTestHelper.Assert(saved.Text.Contains("Upstream errors", StringComparison.Ordinal) && saved.Text.Contains("\"upstream timeout\"", StringComparison.Ordinal), "saved row");
                    LoggingTestHelper.Assert(saved.StructuredJson != null && saved.StructuredJson.Contains("\"searches\"", StringComparison.Ordinal), "structured");

                    OutputLimits one = new OutputLimits { MaxRows = 1 };
                    ToolResult capped = await LoggingTestHelper.RunAsync(c, "logs_list_systems", "{}", Context(stub, 30, one), ct).ConfigureAwait(false);
                    LoggingTestHelper.Assert(capped.Truncated && capped.Text.Contains("Showing the first 1", StringComparison.Ordinal), "row cap");
                }
            }));

            cases.Add(new TestCaseDescriptor(suite, "volume", "logs_volume counts every event into buckets, and reports partial scans", async ct =>
            {
                using (PapertrailStub stub = new PapertrailStub())
                {
                    PapertrailConnector c = new PapertrailConnector();
                    List<StubLogEvent> ev = stub.Events;
                    ToolResult r = await LoggingTestHelper.RunAsync(c, "logs_volume", Window(ev[0].Time, ev[299].Time, "\"buckets\":10"), Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertOk(r, "volume");
                    LoggingTestHelper.Assert(LoggingTestHelper.StructuredLong(r, "total") == 300, "total 300, got " + LoggingTestHelper.StructuredLong(r, "total"));
                    LoggingTestHelper.Assert(LoggingTestHelper.StructuredBool(r, "complete"), "complete");
                    LoggingTestHelper.Assert(r.Text.Contains("| 30 | yes |", StringComparison.Ordinal), "even buckets of 30: " + r.Text);

                    ToolResult errors = await LoggingTestHelper.RunAsync(c, "logs_volume", Window(ev[0].Time, ev[299].Time, "\"buckets\":3,\"query\":\"ERROR\""), Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.Assert(LoggingTestHelper.StructuredLong(errors, "total") == 30, "30 errors");

                    ToolResult partial = await LoggingTestHelper.RunAsync(c, "logs_volume", Window(ev[0].Time, ev[299].Time, "\"buckets\":10,\"maxScanEvents\":100"), Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertOk(partial, "partial");
                    LoggingTestHelper.Assert(!LoggingTestHelper.StructuredBool(partial, "complete") && LoggingTestHelper.StructuredLong(partial, "total") == 100, "partial scan of 100");
                    LoggingTestHelper.Assert(partial.Text.Contains("partial", StringComparison.Ordinal) && partial.Text.Contains("maxScanEvents", StringComparison.Ordinal), "partial explained");
                }
            }));

            cases.Add(new TestCaseDescriptor(suite, "rate-limited", "HTTP 429 becomes rate_limited with the Retry-After seconds", async ct =>
            {
                using (PapertrailStub stub = new PapertrailStub())
                {
                    stub.RetryAfterSeconds = 4;
                    stub.FailNextWithRateLimit(1);
                    ToolResult r = await LoggingTestHelper.RunAsync(new PapertrailConnector(), "logs_search", "{}", Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertError(r, "rate_limited", "429");
                    LoggingTestHelper.Assert(r.Metrics.ContainsKey("retryAfterSeconds") && r.Metrics["retryAfterSeconds"] == 4, "retryAfterSeconds metric");
                    LoggingTestHelper.Assert(r.Text.Contains("Retry after 4 seconds", StringComparison.Ordinal), "message: " + r.Text);
                    ToolResult again = await LoggingTestHelper.RunAsync(new PapertrailConnector(), "logs_search", "{}", Context(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertOk(again, "after the limit clears");
                }
            }));

            cases.Add(new TestCaseDescriptor(suite, "auth-failed", "HTTP 401 becomes auth_failed on every tool, and a missing secret is secret_missing", async ct =>
            {
                using (PapertrailStub stub = new PapertrailStub())
                {
                    PapertrailConnector c = new PapertrailConnector();
                    foreach (IConnectorTool t in c.Tools)
                    {
                        ToolResult r = await t.ExecuteAsync(LoggingTestHelper.Context(Resource(stub, "bad-token")), new ToolArguments("{}"), ct).ConfigureAwait(false);
                        LoggingTestHelper.AssertError(r, "auth_failed", t.Descriptor.Name);
                        LoggingTestHelper.Assert(!r.Text.Contains("bad-token", StringComparison.Ordinal), "token not echoed");
                    }
                    ConnectorResourceContext noSecret = LoggingTestHelper.Resource("papertrail", new Dictionary<string, string> { { "baseUrl", stub.ApiBaseUrl } }, null);
                    ToolResult missing = await LoggingTestHelper.RunAsync(c, "logs_search", "{}", LoggingTestHelper.Context(noSecret), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertError(missing, "secret_missing", "no secret");
                }
            }));

            cases.Add(new TestCaseDescriptor(suite, "timeout", "A slow upstream returns timeout within the call timeout", async ct =>
            {
                using (PapertrailStub stub = new PapertrailStub())
                {
                    stub.DelayMilliseconds = 4000;
                    Stopwatch sw = Stopwatch.StartNew();
                    ToolResult r = await LoggingTestHelper.RunAsync(new PapertrailConnector(), "logs_search", "{}", Context(stub, 1), ct).ConfigureAwait(false);
                    LoggingTestHelper.AssertError(r, "timeout", "slow upstream");
                    LoggingTestHelper.Assert(sw.Elapsed < TimeSpan.FromSeconds(3), "returned promptly: " + sw.Elapsed);

                    PapertrailConnector quick = new PapertrailConnector(new LoggingConnectorOptions { TestConnectionTimeout = TimeSpan.FromSeconds(1) });
                    ConnectorHealthResult h = await quick.TestConnectionAsync(Resource(stub), ct).ConfigureAwait(false);
                    LoggingTestHelper.Assert(!h.Success && h.ErrorCode == "timeout", "test connection timeout");
                }
            }));

            cases.Add(new TestCaseDescriptor(suite, "cancellation", "Caller cancellation propagates as OperationCanceledException", async ct =>
            {
                using (PapertrailStub stub = new PapertrailStub())
                {
                    stub.DelayMilliseconds = 4000;
                    using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        cts.CancelAfter(300);
                        bool cancelled = false;
                        Stopwatch sw = Stopwatch.StartNew();
                        try
                        {
                            await LoggingTestHelper.RunAsync(new PapertrailConnector(), "logs_volume", "{}", Context(stub), cts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            cancelled = true;
                        }
                        LoggingTestHelper.Assert(cancelled && sw.Elapsed < TimeSpan.FromSeconds(3), "cancelled promptly");
                    }
                }
            }));

            cases.Add(new TestCaseDescriptor(suite, "unreachable", "An unreachable base URL is connection_failed", async ct =>
            {
                ConnectorResourceContext res = LoggingTestHelper.Resource("papertrail", new Dictionary<string, string> { { "baseUrl", "http://127.0.0.1:1/api/v1" } }, "t");
                ToolResult r = await LoggingTestHelper.RunAsync(new PapertrailConnector(), "logs_list_systems", "{}", LoggingTestHelper.Context(res), ct).ConfigureAwait(false);
                LoggingTestHelper.AssertError(r, "connection_failed", "unreachable");
            }));

            return cases;
        }

        #endregion

        #region Private-Methods

        private static ConnectorResourceContext Resource(PapertrailStub stub, string? token = null, Dictionary<string, string>? extra = null)
        {
            Dictionary<string, string> settings = new Dictionary<string, string> { { "baseUrl", stub.ApiBaseUrl } };
            if (extra != null) foreach (KeyValuePair<string, string> kv in extra) settings[kv.Key] = kv.Value;
            return LoggingTestHelper.Resource("papertrail", settings, token ?? stub.ApiToken);
        }

        private static ConnectorInvocationContext Context(PapertrailStub stub, double timeoutSeconds = 30, OutputLimits? limits = null, Dictionary<string, string>? extra = null)
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
