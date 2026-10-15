namespace Test.Shared.Connectors.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Logging;
    using Touchstone.Core;

    /// <summary>
    /// Family-level cases: descriptors, schema parity across connectors, time range parsing and caps, cursors, and
    /// rendering limits.
    /// </summary>
    public static class LoggingCommonCases
    {
        #region Public-Methods

        /// <summary>
        /// Build the cases.
        /// </summary>
        /// <returns>Cases.</returns>
        public static List<TestCaseDescriptor> Build()
        {
            string suite = "connectors.logging.common";
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(new TestCaseDescriptor(suite, "descriptors", "Both connectors declare type, family logs, Logging category, logs icon, apiToken slot", ct =>
            {
                PapertrailConnector pt = new PapertrailConnector();
                SolarWindsObservabilityConnector swo = new SolarWindsObservabilityConnector();
                LoggingTestHelper.Assert(pt.Descriptor.Type == "papertrail" && swo.Descriptor.Type == "solarwinds-observability", "types");
                foreach (IConnector c in new IConnector[] { pt, swo })
                {
                    LoggingTestHelper.Assert(c.Descriptor.Family == "logs", c.Descriptor.Type + " family");
                    LoggingTestHelper.Assert(c.Descriptor.Category == ConnectorCategoryEnum.Logging, c.Descriptor.Type + " category");
                    LoggingTestHelper.Assert(c.Descriptor.IconKey == "logs", c.Descriptor.Type + " icon");
                    SecretSlotDescriptor? slot = c.Descriptor.SecretSlots.FirstOrDefault(s => s.Name == "apiToken");
                    LoggingTestHelper.Assert(slot != null && slot.Required && slot.AcceptedKinds.SequenceEqual(new[] { SecretKindEnum.ApiToken }), c.Descriptor.Type + " apiToken slot");
                    LoggingTestHelper.Assert(c.Descriptor.DefaultInstructionsKey == "connector." + c.Descriptor.Type + ".instructions", c.Descriptor.Type + " instructions key");
                    LoggingTestHelper.Assert(c.Descriptor.DefaultInstructions.Contains("bound", StringComparison.OrdinalIgnoreCase), c.Descriptor.Type + " instructions loaded");
                    LoggingTestHelper.Assert(c.Descriptor.SettingsSchema.Find("maxEvents")!.DefaultValue == "500", c.Descriptor.Type + " maxEvents default");
                    LoggingTestHelper.Assert(c.Descriptor.SettingsSchema.Find("maxTimeRangeHours")!.DefaultValue == "168", c.Descriptor.Type + " maxTimeRangeHours default");
                }
                LoggingTestHelper.Assert(pt.Descriptor.SettingsSchema.Find("baseUrl")!.DefaultValue == "https://papertrailapp.com/api/v1", "papertrail baseUrl default");
                LoggingTestHelper.Assert(swo.Descriptor.SettingsSchema.Find("dataCenter")!.AllowedValues.SequenceEqual(new[] { "na-01", "na-02", "eu-01", "ap-01" }), "swo data centers");
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor(suite, "tool-sets", "Each connector exposes the planned tools, all Read", ct =>
            {
                PapertrailConnector pt = new PapertrailConnector();
                SolarWindsObservabilityConnector swo = new SolarWindsObservabilityConnector();
                string[] ptTools = new[] { "logs_search", "logs_tail", "logs_volume", "logs_list_systems", "logs_list_groups", "logs_list_saved_searches" };
                string[] swoTools = new[] { "logs_search", "logs_tail", "swo_list_entities", "swo_get_entity", "swo_query_metric" };
                LoggingTestHelper.Assert(pt.Tools.Select(t => t.Descriptor.Name).OrderBy(n => n).SequenceEqual(ptTools.OrderBy(n => n)), "papertrail tools");
                LoggingTestHelper.Assert(swo.Tools.Select(t => t.Descriptor.Name).OrderBy(n => n).SequenceEqual(swoTools.OrderBy(n => n)), "swo tools");
                foreach (IConnector c in new IConnector[] { pt, swo })
                {
                    ConnectorInvocationContext ctx = LoggingTestHelper.Context(LoggingTestHelper.Resource(c.Descriptor.Type, new Dictionary<string, string>(), "t"));
                    foreach (IConnectorTool t in c.Tools)
                    {
                        LoggingTestHelper.Assert(t.Descriptor.BaseEffect == EffectEnum.Read && !t.Descriptor.ClassifiedPerCall, t.Descriptor.Name + " base effect Read");
                        EffectClassification cls = t.Classify(ctx, new ToolArguments("{\"query\":\"rm -rf\"}"));
                        LoggingTestHelper.Assert(cls.Effect == EffectEnum.Read && !cls.Rejected, t.Descriptor.Name + " classifies Read");
                        LoggingTestHelper.Assert(!String.IsNullOrWhiteSpace(t.Descriptor.Description) && !String.IsNullOrWhiteSpace(t.Descriptor.Title), t.Descriptor.Name + " documented");
                        using (JsonDocument doc = JsonDocument.Parse(t.Descriptor.InputSchemaJson))
                        {
                            LoggingTestHelper.Assert(doc.RootElement.GetProperty("type").GetString() == "object", t.Descriptor.Name + " schema is an object");
                        }
                    }
                }
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor(suite, "schema-parity", "Tools with the same name have identical input schemas across the family", ct =>
            {
                List<IConnector> family = new List<IConnector> { new PapertrailConnector(), new SolarWindsObservabilityConnector() };
                Dictionary<string, string> seen = new Dictionary<string, string>(StringComparer.Ordinal);
                int shared = 0;
                foreach (IConnector c in family)
                {
                    foreach (IConnectorTool t in c.Tools)
                    {
                        if (seen.TryGetValue(t.Descriptor.Name, out string? schema))
                        {
                            shared++;
                            LoggingTestHelper.Assert(schema == t.Descriptor.InputSchemaJson, t.Descriptor.Name + " schema differs between connectors");
                        }
                        else
                        {
                            seen[t.Descriptor.Name] = t.Descriptor.InputSchemaJson;
                        }
                    }
                }
                LoggingTestHelper.Assert(shared == 2, "logs_search and logs_tail are shared (found " + shared + ")");
                using (JsonDocument doc = JsonDocument.Parse(LogsToolSchemas.SearchInputSchema))
                {
                    JsonElement props = doc.RootElement.GetProperty("properties");
                    foreach (string p in new[] { "query", "startTime", "endTime", "limit", "cursor", "providerOptions" })
                        LoggingTestHelper.Assert(props.TryGetProperty(p, out JsonElement _), "logs_search has " + p);
                }
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor(suite, "time-parse", "Relative and ISO-8601 times parse; garbage is invalid_argument", ct =>
            {
                DateTime now = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
                LoggingTestHelper.Assert(TimeRangeResolver.ParsePoint("15m", now, "x") == now.AddMinutes(-15), "15m");
                LoggingTestHelper.Assert(TimeRangeResolver.ParsePoint("2h", now, "x") == now.AddHours(-2), "2h");
                LoggingTestHelper.Assert(TimeRangeResolver.ParsePoint("1d", now, "x") == now.AddDays(-1), "1d");
                LoggingTestHelper.Assert(TimeRangeResolver.ParsePoint("30s", now, "x") == now.AddSeconds(-30), "30s");
                LoggingTestHelper.Assert(TimeRangeResolver.ParsePoint("1w", now, "x") == now.AddDays(-7), "1w");
                LoggingTestHelper.Assert(TimeRangeResolver.ParsePoint("now", now, "x") == now, "now");
                LoggingTestHelper.Assert(TimeRangeResolver.ParsePoint("2026-10-14T05:00:00-07:00", now, "x") == now, "offset");
                LoggingTestHelper.Assert(TimeRangeResolver.ParsePoint("2026-10-14T12:00:00", now, "x") == now, "no offset is UTC");
                bool threw = false;
                try { TimeRangeResolver.ParsePoint("yesterday-ish", now, "startTime"); } catch (ConnectorException e) when (e.Code == "invalid_argument") { threw = true; }
                LoggingTestHelper.Assert(threw, "garbage rejected");
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor(suite, "time-range-caps", "Windows over maxTimeRangeHours fail with time_range_exceeded; reversed windows are invalid", ct =>
            {
                DateTime now = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
                TimeRange r = TimeRangeResolver.Resolve(null, null, "1h", 168, now);
                LoggingTestHelper.Assert(r.Start == now.AddHours(-1) && r.End == now, "default window");
                r = TimeRangeResolver.Resolve("7d", "now", "1h", 168, now);
                LoggingTestHelper.Assert(r.Duration == TimeSpan.FromDays(7), "exactly the cap is allowed");
                string code = String.Empty;
                try { TimeRangeResolver.Resolve("8d", null, "1h", 168, now); } catch (ConnectorException e) { code = e.Code; }
                LoggingTestHelper.Assert(code == "time_range_exceeded", "8d over cap");
                code = String.Empty;
                try { TimeRangeResolver.Resolve("1h", "2h", "1h", 168, now); } catch (ConnectorException e) { code = e.Code; }
                LoggingTestHelper.Assert(code == "invalid_argument", "start after end");
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor(suite, "cursor-roundtrip", "Cursors round-trip and reject foreign or garbage values", ct =>
            {
                LogCursor c = new LogCursor { Provider = "papertrail", Kind = "search", MaxId = "123", Start = DateTime.UtcNow.AddHours(-1), End = DateTime.UtcNow, Query = "a b", Group = "31" };
                string encoded = c.Encode();
                LoggingTestHelper.Assert(!encoded.Contains('=') && !encoded.Contains('+') && !encoded.Contains('/'), "base64url");
                LogCursor back = LogCursor.Decode(encoded, "papertrail", "search");
                LoggingTestHelper.Assert(back.MaxId == "123" && back.Query == "a b" && back.Group == "31", "fields");
                string code = String.Empty;
                try { LogCursor.Decode(encoded, "solarwinds-observability", "search"); } catch (ConnectorException e) { code = e.Code; }
                LoggingTestHelper.Assert(code == "invalid_argument", "foreign provider");
                code = String.Empty;
                try { LogCursor.Decode("not a cursor!!", "papertrail", "search"); } catch (ConnectorException e) { code = e.Code; }
                LoggingTestHelper.Assert(code == "invalid_argument", "garbage");
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor(suite, "swo-skip-token", "SolarWinds nextPage is accepted as a token or as a link", ct =>
            {
                LoggingTestHelper.Assert(SwoClient.ExtractSkipToken("abc123") == "abc123", "plain");
                LoggingTestHelper.Assert(SwoClient.ExtractSkipToken("/v1/logs?pageSize=5&skipToken=a%2Bb%3D") == "a+b=", "link");
                LoggingTestHelper.Assert(SwoClient.ExtractSkipToken("https://api.na-01.cloud.solarwinds.com/v1/logs?skipToken=xyz") == "xyz", "absolute link");
                LoggingTestHelper.Assert(SwoClient.ExtractSkipToken(String.Empty) == null && SwoClient.ExtractSkipToken(null) == null, "empty");
                LoggingTestHelper.Assert(SwoSettings.BaseUrlFor("eu-01") == "https://api.eu-01.cloud.solarwinds.com/v1", "eu-01 url");
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor(suite, "provider-options", "Unknown providerOptions keys and bad direction are invalid_argument", ct =>
            {
                string code = String.Empty;
                try { LogsProviderOptions.FromArguments(new ToolArguments("{\"providerOptions\":{\"bogus\":\"x\"}}")); } catch (ConnectorException e) { code = e.Code; }
                LoggingTestHelper.Assert(code == "invalid_argument", "unknown key");
                code = String.Empty;
                try { LogsProviderOptions.FromArguments(new ToolArguments("{\"providerOptions\":{\"direction\":\"sideways\"}}")); } catch (ConnectorException e) { code = e.Code; }
                LoggingTestHelper.Assert(code == "invalid_argument", "bad direction");
                LogsProviderOptions o = LogsProviderOptions.FromArguments(new ToolArguments("{\"providerOptions\":{\"group\":\"Web\",\"direction\":\"FORWARD\"}}"));
                LoggingTestHelper.Assert(o.Group == "Web" && o.Direction == "forward", "parsed");
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor(suite, "renderer-limits", "Rendering keeps within line and byte limits and shortens long messages", ct =>
            {
                List<LogEvent> events = new List<LogEvent>();
                for (int i = 0; i < 50; i++)
                    events.Add(new LogEvent { Id = i.ToString(), Timestamp = DateTime.UtcNow, Host = "h", Program = "p", Message = i == 0 ? new string('x', 5000) : "line " + i });
                LogEventRenderer.ShortenMessages(events, 100);
                LoggingTestHelper.Assert(events[0].Message.Length == 100 && events[0].Message.EndsWith("...", StringComparison.Ordinal), "shortened");
                OutputLimits limits = new OutputLimits { MaxLines = 14, MaxBytes = 65536 };
                List<LogEvent> newest = LogEventRenderer.Fit(events, limits, true);
                LoggingTestHelper.Assert(newest.Count == 10 && newest.Last().Id == "49" && newest.First().Id == "40", "keeps newest 10");
                List<LogEvent> oldest = LogEventRenderer.Fit(events, limits, false);
                LoggingTestHelper.Assert(oldest.Count == 10 && oldest.First().Id == "0", "keeps oldest 10");
                LogSearchPayload payload = new LogSearchPayload { Provider = "papertrail", Kind = "search", Events = newest, Omitted = 40, NextCursor = "abc" };
                ToolResult r = LogEventRenderer.Render(payload, "logs_search", "More older events are available", limits);
                LoggingTestHelper.Assert(r.Truncated && r.Text.Split('\n').Length <= 15, "bounded text");
                LoggingTestHelper.Assert(r.Text.Contains("cursor \"abc\"", StringComparison.Ordinal), "cursor hint");
                LoggingTestHelper.Assert(r.Text.Contains(" h p: line 49", StringComparison.Ordinal), "compact line format");
                return Task.CompletedTask;
            }));

            return cases;
        }

        #endregion
    }
}
