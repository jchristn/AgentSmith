namespace Test.Shared.Connectors.Observability
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Observability;
    using Hugo.Core.Connectors;
    using Touchstone.Core;

    /// <summary>
    /// Cases shared by the observability connectors: descriptors, schemas, the family rule, settings validation, time
    /// parsing, step planning, every authentication type, OAuth2 token caching, transport error mapping, timeout,
    /// cancellation, and secret hygiene.
    /// </summary>
    public static class ObservabilityCommonCases
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public static readonly string SuiteId = "connectors.observability.common";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the cases.
        /// </summary>
        /// <returns>Cases.</returns>
        public static List<TestCaseDescriptor> Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            Add(cases, "descriptors", "Descriptors have type, family, category, icon, slots, settings, and instructions", Descriptors);
            Add(cases, "tool-schemas", "Every tool has a family-prefixed name, a parseable described schema, and is Read", ToolSchemas);
            Add(cases, "family-registry", "All three connectors register; same-named tools in a family share one schema", FamilyRegistry);
            Add(cases, "settings-schema-validator", "Hugo's settings validator accepts defaults and rejects out-of-range values", SettingsSchemaValidator);
            Add(cases, "validate", "ValidateAsync reports missing URLs, auth secrets, header syntax, and ranges", Validate);
            Add(cases, "time-parsing", "Durations, points, and windows parse; reversed and over-long windows fail", TimeParsing);
            Add(cases, "step-plan", "Range steps respect the point budget and report when raised", StepPlan);
            Add(cases, "auth-none-basic-bearer-apikey", "None, Basic, Bearer, and ApiKeyHeader send the right header", AuthHeaders);
            Add(cases, "auth-failed", "A rejected credential maps to auth_failed without echoing the secret", AuthFailed);
            Add(cases, "auth-secret-missing", "A missing secret for the authType maps to secret_missing", SecretMissing);
            Add(cases, "oauth2-caching", "OAuth2 client credentials tokens are cached, refreshed after revocation, and scopes are sent", OAuthCaching);
            Add(cases, "oauth2-bad-client", "A rejected OAuth2 client maps to auth_failed", OAuthBadClient);
            Add(cases, "oauth2-short-lifetime", "A token that expires within the refresh margin is requested again", OAuthShortLifetime);
            Add(cases, "default-headers", "Default headers are sent with every request", DefaultHeaders);
            Add(cases, "rate-limited", "HTTP 429 maps to rate_limited with RetryAfterSeconds", RateLimited);
            Add(cases, "timeout", "A slow backend maps to timeout", TimeoutCase);
            Add(cases, "cancellation", "Caller cancellation throws OperationCanceledException", Cancellation);
            Add(cases, "too-large", "Responses over maxResponseBytes fail with too_large", TooLarge);
            Add(cases, "connection-failed", "An unreachable backend maps to connection_failed and test connection fails cleanly", ConnectionFailed);
            Add(cases, "invalid-setting", "An invalid baseUrl surfaces as invalid_setting from tools", InvalidSetting);
            return cases;
        }

        #endregion

        #region Private-Methods

        private static void Add(List<TestCaseDescriptor> cases, string id, string name, Func<CancellationToken, Task> body)
        {
            cases.Add(new TestCaseDescriptor(SuiteId, id, name, body));
        }

        private static List<IConnector> All()
        {
            return new List<IConnector> { new PrometheusConnector(), new LokiConnector(), new ElasticsearchConnector() };
        }

        private static Task Descriptors(CancellationToken token)
        {
            Dictionary<string, string> families = new Dictionary<string, string> { { "prometheus", "prom" }, { "loki", "loki" }, { "elasticsearch", "es" } };
            foreach (IConnector c in All())
            {
                ConnectorDescriptor d = c.Descriptor;
                ObsTestHelper.Assert(families[d.Type] == d.Family, d.Type + " family");
                ObsTestHelper.Assert(d.Category == ConnectorCategoryEnum.Observability, d.Type + " category");
                ObsTestHelper.Assert(!String.IsNullOrEmpty(d.IconKey) && d.IconKey != "plug", d.Type + " icon");
                ObsTestHelper.Assert(d.DefaultInstructionsKey == "connector." + d.Type + ".instructions", d.Type + " instructions key");
                ObsTestHelper.Assert(d.DefaultInstructions.Length > 500 && d.DefaultInstructions.Contains("##", StringComparison.Ordinal), d.Type + " instructions text");
                ObsTestHelper.Assert(!d.DefaultInstructions.Contains('—') && !d.Description.Contains('—'), d.Type + " no em-dash");
                ObsTestHelper.Assert(d.SecretSlots.Select(s => s.Name).SequenceEqual(new[] { "basicAuth", "bearerToken", "apiKey", "clientCredentials" }), d.Type + " slots");
                ObsTestHelper.Assert(d.SecretSlots.All(s => !s.Required), d.Type + " slots optional");
                foreach (string key in new[] { "baseUrl", "authType", "apiKeyHeaderName", "apiKeyPrefix", "tokenUrl", "scopes", "defaultHeaders", "verifyTls", "maxResponseBytes" })
                    ObsTestHelper.Assert(d.SettingsSchema.Find(key) != null, d.Type + " setting " + key);
                ObsTestHelper.Assert(d.SettingsSchema.Find("baseUrl")!.Required, "baseUrl required");
                ObsTestHelper.Assert(d.SettingsSchema.Settings.All(s => !String.IsNullOrEmpty(s.Help)), d.Type + " every setting has help");
            }
            ObsTestHelper.Assert(new PrometheusConnector().Descriptor.SettingsSchema.Find("maxPointsPerSeries")!.DefaultValue == "240", "prom maxPointsPerSeries default");
            ObsTestHelper.Assert(new LokiConnector().Descriptor.SettingsSchema.Find("tenantHeader") != null, "loki tenantHeader");
            ObsTestHelper.Assert(new ElasticsearchConnector().Descriptor.SettingsSchema.Find("flavor")!.AllowedValues.SequenceEqual(new[] { "Elasticsearch", "OpenSearch" }), "es flavor values");
            return Task.CompletedTask;
        }

        private static Task ToolSchemas(CancellationToken token)
        {
            Dictionary<string, string[]> expected = new Dictionary<string, string[]>
            {
                { "prometheus", new[] { "prom_query", "prom_query_range", "prom_alerts", "prom_rules", "prom_targets", "prom_series", "prom_label_values" } },
                { "loki", new[] { "loki_query", "loki_query_range", "loki_labels", "loki_label_values" } },
                { "elasticsearch", new[] { "es_search", "es_count", "es_cluster_health", "es_indices", "es_mappings" } }
            };
            foreach (IConnector c in All())
            {
                ObsTestHelper.Assert(c.Tools.Select(t => t.Descriptor.Name).SequenceEqual(expected[c.Descriptor.Type]), c.Descriptor.Type + " tool set");
                foreach (IConnectorTool t in c.Tools)
                {
                    ToolDescriptor d = t.Descriptor;
                    ObsTestHelper.Assert(d.Name.StartsWith(c.Descriptor.Family + "_", StringComparison.Ordinal), d.Name + " prefix");
                    ObsTestHelper.Assert(d.BaseEffect == EffectEnum.Read && !d.ClassifiedPerCall, d.Name + " is Read");
                    ObsTestHelper.Assert(d.Description.Length > 60 && !String.IsNullOrEmpty(d.Title) && !String.IsNullOrEmpty(d.OutputDescription), d.Name + " described");
                    using (JsonDocument doc = JsonDocument.Parse(d.InputSchemaJson))
                    {
                        JsonElement root = doc.RootElement;
                        ObsTestHelper.Assert(root.GetProperty("type").GetString() == "object", d.Name + " object schema");
                        ObsTestHelper.Assert(root.GetProperty("additionalProperties").ValueKind == JsonValueKind.False, d.Name + " closed schema");
                        foreach (JsonProperty p in root.GetProperty("properties").EnumerateObject())
                            ObsTestHelper.Assert(p.Value.TryGetProperty("description", out JsonElement desc) && desc.GetString()!.Length > 10, d.Name + "." + p.Name + " described");
                    }
                    ConnectorInvocationContext ctx = ObsTestHelper.Context(ObsTestHelper.Resource(c.Descriptor.Type, new Dictionary<string, string>()));
                    EffectClassification e = t.Classify(ctx, new ToolArguments("{}"));
                    ObsTestHelper.Assert(e.Effect == EffectEnum.Read && !e.Rejected && e.ApprovalSummary.Contains("obs", StringComparison.Ordinal), d.Name + " classifies Read");
                }
            }
            return Task.CompletedTask;
        }

        private static Task FamilyRegistry(CancellationToken token)
        {
            using (ConnectorRegistry registry = new ConnectorRegistry())
            {
                foreach (IConnector c in All()) registry.Register(c);
                List<FamilyTool> prom = registry.FamilyTools("prom");
                List<FamilyTool> loki = registry.FamilyTools("loki");
                List<FamilyTool> es = registry.FamilyTools("es");
                ObsTestHelper.Assert(prom.Count == 7 && loki.Count == 4 && es.Count == 5, "family tool counts " + prom.Count + "/" + loki.Count + "/" + es.Count);
                ObsTestHelper.Assert(prom.All(t => t.ConnectorTypes.SequenceEqual(new[] { "prometheus" })), "prom tools owned by prometheus");
            }
            foreach (IConnector a in All())
            {
                foreach (IConnector b in All().Where(x => x.Descriptor.Family == a.Descriptor.Family))
                {
                    foreach (IConnectorTool ta in a.Tools)
                    {
                        IConnectorTool? tb = b.Tools.FirstOrDefault(t => t.Descriptor.Name == ta.Descriptor.Name);
                        if (tb != null) ObsTestHelper.Assert(tb.Descriptor.InputSchemaJson == ta.Descriptor.InputSchemaJson, ta.Descriptor.Name + " schema stable");
                    }
                }
            }
            return Task.CompletedTask;
        }

        private static Task SettingsSchemaValidator(CancellationToken token)
        {
            foreach (IConnector c in All())
            {
                Dictionary<string, string> values = ConnectorSettingsValidator.ApplyDefaults(c.Descriptor, new Dictionary<string, string> { { "baseUrl", "http://127.0.0.1:9" } });
                ConnectorValidationResult ok = ConnectorSettingsValidator.Validate(c.Descriptor, values, null);
                ObsTestHelper.Assert(ok.IsValid, c.Descriptor.Type + " defaults valid: " + String.Join("; ", ok.Errors.Select(e => e.Field + " " + e.Message)));
                values["maxResponseBytes"] = "10";
                values["authType"] = "Kerberos";
                ConnectorValidationResult bad = ConnectorSettingsValidator.Validate(c.Descriptor, values, null);
                List<string> fields = bad.Errors.Select(e => e.Field).ToList();
                ObsTestHelper.Assert(fields.Contains("maxResponseBytes") && fields.Contains("authType"), c.Descriptor.Type + " invalid values: " + String.Join(",", fields));
            }
            return Task.CompletedTask;
        }

        private static async Task Validate(CancellationToken token)
        {
            foreach (IConnector c in All())
            {
                ConnectorValidationResult ok = await c.ValidateAsync(ObsTestHelper.Resource(c.Descriptor.Type, new Dictionary<string, string> { { "baseUrl", "http://127.0.0.1:9" } }), token).ConfigureAwait(false);
                ObsTestHelper.Assert(ok.IsValid, c.Descriptor.Type + " minimal valid");

                ConnectorValidationResult noUrl = await c.ValidateAsync(ObsTestHelper.Resource(c.Descriptor.Type, new Dictionary<string, string> { { "baseUrl", "ftp://x" }, { "authType", "Basic" }, { "defaultHeaders", "bad header line" } }), token).ConfigureAwait(false);
                List<string> f = noUrl.Errors.Select(e => e.Field).ToList();
                ObsTestHelper.Assert(f.Contains("baseUrl") && f.Contains("basicAuth") && f.Contains("defaultHeaders"), c.Descriptor.Type + " errors: " + String.Join(",", f));

                ConnectorValidationResult oauth = await c.ValidateAsync(ObsTestHelper.Resource(c.Descriptor.Type, new Dictionary<string, string> { { "baseUrl", "http://127.0.0.1:9" }, { "authType", "OAuth2ClientCredentials" } }), token).ConfigureAwait(false);
                f = oauth.Errors.Select(e => e.Field).ToList();
                ObsTestHelper.Assert(f.Contains("tokenUrl") && f.Contains("clientCredentials"), c.Descriptor.Type + " oauth errors: " + String.Join(",", f));

                Dictionary<string, Dictionary<string, string>> creds = new Dictionary<string, Dictionary<string, string>> { { "clientCredentials", new Dictionary<string, string> { { "username", "id" }, { "password", "pw" } } } };
                ConnectorValidationResult oauthOk = await c.ValidateAsync(ObsTestHelper.Resource(c.Descriptor.Type, new Dictionary<string, string> { { "baseUrl", "http://127.0.0.1:9" }, { "authType", "OAuth2ClientCredentials" }, { "tokenUrl", "http://127.0.0.1:9/token" } }, creds), token).ConfigureAwait(false);
                ObsTestHelper.Assert(oauthOk.IsValid, c.Descriptor.Type + " oauth with Password-kind secret valid");

                ConnectorValidationResult header = await c.ValidateAsync(ObsTestHelper.Resource(c.Descriptor.Type, new Dictionary<string, string> { { "baseUrl", "http://127.0.0.1:9" }, { "authType", "ApiKeyHeader" }, { "apiKeyHeaderName", "Bad Header" } }), token).ConfigureAwait(false);
                f = header.Errors.Select(e => e.Field).ToList();
                ObsTestHelper.Assert(f.Contains("apiKeyHeaderName") && f.Contains("apiKey"), c.Descriptor.Type + " api key errors: " + String.Join(",", f));
            }

            ConnectorValidationResult prom = await new PrometheusConnector().ValidateAsync(ObsTestHelper.Resource("prometheus", new Dictionary<string, string> { { "baseUrl", "http://x" }, { "maxSeries", "0" }, { "maxPointsPerSeries", "5" }, { "maxRangeHours", "x" } }), token).ConfigureAwait(false);
            ObsTestHelper.Assert(prom.Errors.Count == 3, "prom ranges: " + String.Join(",", prom.Errors.Select(e => e.Field)));
            ConnectorValidationResult loki = await new LokiConnector().ValidateAsync(ObsTestHelper.Resource("loki", new Dictionary<string, string> { { "baseUrl", "http://x" }, { "maxLines", "6000" }, { "tenantHeader", "a\nb" } }), token).ConfigureAwait(false);
            ObsTestHelper.Assert(loki.Errors.Select(e => e.Field).OrderBy(x => x).SequenceEqual(new[] { "maxLines", "tenantHeader" }), "loki errors: " + String.Join(",", loki.Errors.Select(e => e.Field)));
            ConnectorValidationResult es = await new ElasticsearchConnector().ValidateAsync(ObsTestHelper.Resource("elasticsearch", new Dictionary<string, string> { { "baseUrl", "http://x" }, { "flavor", "Solr" }, { "maxHits", "0" }, { "allowedIndexPatterns", "logs-*\nBad Pattern" } }), token).ConfigureAwait(false);
            ObsTestHelper.Assert(es.Errors.Select(e => e.Field).OrderBy(x => x).SequenceEqual(new[] { "allowedIndexPatterns", "flavor", "maxHits" }), "es errors: " + String.Join(",", es.Errors.Select(e => e.Field)));
            ConnectorValidationResult esOk = await new ElasticsearchConnector().ValidateAsync(ObsTestHelper.Resource("elasticsearch", new Dictionary<string, string> { { "baseUrl", "http://x" }, { "allowedIndexPatterns", "logs-*\n.ds-logs-*\nmetrics-2026.*" } }), token).ConfigureAwait(false);
            ObsTestHelper.Assert(esOk.IsValid, "es patterns valid: " + String.Join(",", esOk.Errors.Select(e => e.Message)));
        }

        private static Task TimeParsing(CancellationToken token)
        {
            DateTimeOffset now = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
            ObsTestHelper.Assert(ObservabilityTime.ParseDuration("90", "x") == TimeSpan.FromSeconds(90), "plain seconds");
            ObsTestHelper.Assert(ObservabilityTime.ParseDuration("1h30m", "x") == TimeSpan.FromMinutes(90), "1h30m");
            ObsTestHelper.Assert(ObservabilityTime.ParseDuration("500ms", "x") == TimeSpan.FromMilliseconds(500), "500ms");
            ObsTestHelper.Assert(ObservabilityTime.ParseDuration("1w", "x") == TimeSpan.FromDays(7), "1w");
            ObsTestHelper.Assert(ObservabilityTime.ParsePoint("now", now, "x") == now, "now");
            ObsTestHelper.Assert(ObservabilityTime.ParsePoint("15m", now, "x") == now.AddMinutes(-15), "15m before now");
            ObsTestHelper.Assert(ObservabilityTime.ParsePoint("now-2h", now, "x") == now.AddHours(-2), "now-2h");
            ObsTestHelper.Assert(ObservabilityTime.ParsePoint("2026-10-14T08:00:00Z", now, "x") == new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero), "rfc3339");
            ObsTestHelper.Assert(ObservabilityTime.ParsePoint("2026-10-14T10:00:00+02:00", now, "x") == new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero), "rfc3339 offset");
            ObsTestHelper.Assert(ObservabilityTime.ParsePoint("1760443200", now, "x") == DateTimeOffset.FromUnixTimeSeconds(1760443200), "unix seconds");
            ObsTestHelper.Assert(ObservabilityTime.ParsePoint("1760443200.5", now, "x") == DateTimeOffset.FromUnixTimeMilliseconds(1760443200500), "unix fraction");
            ObsTestHelper.Assert(ObservabilityTime.UnixNanoseconds(DateTimeOffset.FromUnixTimeSeconds(1760443200)) == "1760443200000000000", "nanoseconds");
            ObsTestHelper.Assert(ObservabilityTime.UnixSeconds(DateTimeOffset.FromUnixTimeMilliseconds(1760443200250)) == "1760443200.25", "unix seconds text");
            ObsTestHelper.Assert(ObservabilityTime.FromUnixNanoseconds("1760443200000000000") == DateTimeOffset.FromUnixTimeSeconds(1760443200), "from ns");
            ObsTestHelper.Assert(ObservabilityTime.FormatDuration(TimeSpan.FromSeconds(3725)) == "1h2m5s", "format duration");
            ExpectCode(() => ObservabilityTime.ParseDuration("soon", "step"), "invalid_argument");
            ExpectCode(() => ObservabilityTime.ParsePoint("yesterday-ish", now, "start"), "invalid_argument");

            ObservabilityTimeWindow w = ObservabilityTime.ResolveWindow(null, null, "1h", 24, now);
            ObsTestHelper.Assert(w.Start == now.AddHours(-1) && w.End == now, "default window");
            ObservabilityTimeWindow w2 = ObservabilityTime.ResolveWindow("30m", "10m", "1h", 24, now);
            ObsTestHelper.Assert(w2.Length == TimeSpan.FromMinutes(20), "relative window");
            ExpectCode(() => ObservabilityTime.ResolveWindow("10m", "30m", "1h", 24, now), "invalid_argument");
            ExpectCode(() => ObservabilityTime.ResolveWindow("2d", null, "1h", 24, now), "time_range_exceeded");
            return Task.CompletedTask;
        }

        private static Task StepPlan(CancellationToken token)
        {
            PromStepPlan auto = PromStepPlan.Compute(TimeSpan.FromHours(1), null, 240, 60);
            ObsTestHelper.Assert(auto.StepSeconds == 62 && !auto.Raised && auto.Points <= 60, "auto 1h: " + auto.StepSeconds + " " + auto.Points);
            PromStepPlan fine = PromStepPlan.Compute(TimeSpan.FromHours(1), TimeSpan.FromSeconds(1), 240, 60);
            ObsTestHelper.Assert(fine.Raised && fine.StepSeconds == 16 && fine.Points <= 240, "raised: " + fine.StepSeconds + " " + fine.Points);
            PromStepPlan okStep = PromStepPlan.Compute(TimeSpan.FromHours(1), TimeSpan.FromMinutes(1), 240, 60);
            ObsTestHelper.Assert(!okStep.Raised && okStep.StepSeconds == 60 && okStep.Points == 61, "explicit 1m");
            PromStepPlan week = PromStepPlan.Compute(TimeSpan.FromDays(7), TimeSpan.FromSeconds(15), 11000, 60);
            ObsTestHelper.Assert(week.Raised && week.Points <= 11000, "week within prometheus limit");
            PromStepPlan tiny = PromStepPlan.Compute(TimeSpan.FromSeconds(10), null, 240, 60);
            ObsTestHelper.Assert(tiny.StepSeconds == 1 && tiny.Points == 11, "tiny window: " + tiny.StepSeconds);
            return Task.CompletedTask;
        }

        private static async Task<ToolResult> Labels(ObservabilityStub stub, Dictionary<string, string> extra, Dictionary<string, Dictionary<string, string>>? secrets, CancellationToken token, double timeout = 30)
        {
            ConnectorResourceContext r = ObsTestHelper.Resource("prometheus", ObsTestHelper.Settings(stub, extra), secrets);
            return await ObsTestHelper.RunAsync(new PrometheusConnector(), "prom_label_values", "{}", ObsTestHelper.Context(r, timeout), token).ConfigureAwait(false);
        }

        private static ObservabilityStub PromStub()
        {
            ObservabilityStub stub = new ObservabilityStub();
            PromStubRoutes routes = new PromStubRoutes();
            stub.Handler = routes.Handle;
            return stub;
        }

        private static async Task AuthHeaders(CancellationToken token)
        {
            using (ObservabilityStub stub = PromStub())
            {
                ToolResult none = await Labels(stub, new Dictionary<string, string>(), null, token).ConfigureAwait(false);
                ObsTestHelper.Ok(none, "none");
                ObsTestHelper.Assert(stub.Last("/api/v1/labels")!.Headers["Authorization"] == null, "no auth header");

                stub.RequiredHeaderName = "Authorization";
                stub.RequiredHeaderValue = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("grafana:pa:ss"));
                ToolResult basic = await Labels(stub, new Dictionary<string, string> { { "authType", "Basic" } }, Slot("basicAuth", "username", "grafana", "password", "pa:ss"), token).ConfigureAwait(false);
                ObsTestHelper.Ok(basic, "basic");

                stub.RequiredHeaderValue = "Bearer tok-123";
                ToolResult bearer = await Labels(stub, new Dictionary<string, string> { { "authType", "Bearer" } }, Slot("bearerToken", "token", "tok-123"), token).ConfigureAwait(false);
                ObsTestHelper.Ok(bearer, "bearer");

                stub.RequiredHeaderName = "X-API-Key";
                stub.RequiredHeaderValue = "key-9";
                ToolResult apiKey = await Labels(stub, new Dictionary<string, string> { { "authType", "ApiKeyHeader" } }, Slot("apiKey", "token", "key-9"), token).ConfigureAwait(false);
                ObsTestHelper.Ok(apiKey, "api key default header");

                stub.RequiredHeaderName = "Authorization";
                stub.RequiredHeaderValue = "ApiKey abc==";
                ToolResult prefixed = await Labels(stub, new Dictionary<string, string> { { "authType", "ApiKeyHeader" }, { "apiKeyHeaderName", "Authorization" }, { "apiKeyPrefix", "ApiKey " } }, Slot("apiKey", "token", "abc=="), token).ConfigureAwait(false);
                ObsTestHelper.Ok(prefixed, "elastic style api key");
            }
        }

        private static async Task AuthFailed(CancellationToken token)
        {
            using (ObservabilityStub stub = PromStub())
            {
                stub.RequiredHeaderName = "Authorization";
                stub.RequiredHeaderValue = "Bearer right-token";
                ToolResult r = await Labels(stub, new Dictionary<string, string> { { "authType", "Bearer" } }, Slot("bearerToken", "token", "wrong-token-value"), token).ConfigureAwait(false);
                ObsTestHelper.Error(r, "auth_failed", "wrong bearer");
                ObsTestHelper.Assert(!r.Text.Contains("wrong-token-value", StringComparison.Ordinal), "secret not echoed");
                ObsTestHelper.Assert(r.Metrics["httpStatus"] == 401, "status metric");

                ConnectorHealthResult health = await new PrometheusConnector().TestConnectionAsync(ObsTestHelper.Resource("prometheus", ObsTestHelper.Settings(stub, new Dictionary<string, string> { { "authType", "Bearer" } }), Slot("bearerToken", "token", "wrong-token-value")), token).ConfigureAwait(false);
                ObsTestHelper.Assert(!health.Success && health.ErrorCode == "auth_failed" && !health.Message.Contains("wrong-token-value", StringComparison.Ordinal), "health auth_failed");

                ToolResult ok = await Labels(stub, new Dictionary<string, string> { { "authType", "Bearer" } }, Slot("bearerToken", "token", "right-token"), token).ConfigureAwait(false);
                ObsTestHelper.Ok(ok, "right bearer");
                ObsTestHelper.Assert(!ok.Text.Contains("right-token", StringComparison.Ordinal) && !(ok.StructuredJson ?? String.Empty).Contains("right-token", StringComparison.Ordinal), "secret not in output");
            }
        }

        private static async Task SecretMissing(CancellationToken token)
        {
            using (ObservabilityStub stub = PromStub())
            {
                foreach (string auth in new[] { "Basic", "Bearer", "ApiKeyHeader" })
                {
                    ToolResult r = await Labels(stub, new Dictionary<string, string> { { "authType", auth } }, null, token).ConfigureAwait(false);
                    ObsTestHelper.Error(r, "secret_missing", auth);
                }
                ToolResult o = await Labels(stub, new Dictionary<string, string> { { "authType", "OAuth2ClientCredentials" }, { "tokenUrl", stub.BaseAddress + "/oauth/token" } }, null, token).ConfigureAwait(false);
                ObsTestHelper.Error(o, "secret_missing", "oauth");
                ObsTestHelper.Assert(stub.Requests.IsEmpty, "no request sent without a secret");
            }
        }

        private static async Task OAuthCaching(CancellationToken token)
        {
            using (ObservabilityStub stub = PromStub())
            {
                string clientId = "client-" + Guid.NewGuid().ToString("N");
                stub.OAuthClientId = clientId;
                stub.OAuthClientSecret = "s3cret/value";
                Dictionary<string, string> settings = new Dictionary<string, string> { { "authType", "OAuth2ClientCredentials" }, { "tokenUrl", stub.BaseAddress + "/oauth/token" }, { "scopes", "metrics.read\nlogs.read" } };
                Dictionary<string, Dictionary<string, string>> secrets = Slot("clientCredentials", "clientId", clientId, "clientSecret", "s3cret/value");
                for (int i = 0; i < 3; i++)
                {
                    ToolResult r = await Labels(stub, settings, secrets, token).ConfigureAwait(false);
                    ObsTestHelper.Ok(r, "oauth call " + i);
                    ObsTestHelper.Assert(!r.Text.Contains("stub-access-", StringComparison.Ordinal), "token not in output");
                }
                ObsTestHelper.Assert(stub.TokenRequests == 1, "one token request for three calls, got " + stub.TokenRequests);
                ObsTestHelper.Assert(stub.LastScope == "metrics.read logs.read", "scopes sent: " + stub.LastScope);

                stub.RevokeTokens();
                ToolResult again = await Labels(stub, settings, secrets, token).ConfigureAwait(false);
                ObsTestHelper.Ok(again, "after revocation");
                ObsTestHelper.Assert(stub.TokenRequests == 2, "re-requested after 401, got " + stub.TokenRequests);

                Dictionary<string, Dictionary<string, string>> rotated = Slot("clientCredentials", "clientId", clientId, "clientSecret", "rotated-secret");
                ToolResult bad = await Labels(stub, settings, rotated, token).ConfigureAwait(false);
                ObsTestHelper.Error(bad, "auth_failed", "rotated secret is a new cache key and is rejected");
                ObsTestHelper.Assert(stub.TokenRequests == 3, "new key requested a token");
            }
        }

        private static async Task OAuthBadClient(CancellationToken token)
        {
            using (ObservabilityStub stub = PromStub())
            {
                stub.OAuthClientId = "expected";
                stub.OAuthClientSecret = "secret";
                ToolResult r = await Labels(stub, new Dictionary<string, string> { { "authType", "OAuth2ClientCredentials" }, { "tokenUrl", stub.BaseAddress + "/oauth/token" } }, Slot("clientCredentials", "clientId", "intruder", "clientSecret", "guess-" + Guid.NewGuid().ToString("N")), token).ConfigureAwait(false);
                ObsTestHelper.Error(r, "auth_failed", "bad client");
                ObsTestHelper.Assert(!r.Text.Contains("guess-", StringComparison.Ordinal), "client secret not echoed");
            }
        }

        private static async Task OAuthShortLifetime(CancellationToken token)
        {
            using (ObservabilityStub stub = PromStub())
            {
                string clientId = "short-" + Guid.NewGuid().ToString("N");
                stub.OAuthClientId = clientId;
                stub.OAuthClientSecret = "x";
                stub.OAuthExpiresIn = 30;
                Dictionary<string, string> settings = new Dictionary<string, string> { { "authType", "OAuth2ClientCredentials" }, { "tokenUrl", stub.BaseAddress + "/oauth/token" } };
                Dictionary<string, Dictionary<string, string>> secrets = Slot("clientCredentials", "clientId", clientId, "clientSecret", "x");
                ObsTestHelper.Ok(await Labels(stub, settings, secrets, token).ConfigureAwait(false), "first");
                ObsTestHelper.Ok(await Labels(stub, settings, secrets, token).ConfigureAwait(false), "second");
                ObsTestHelper.Assert(stub.TokenRequests == 2, "30s token is inside the 60s refresh margin, so each call refreshes; got " + stub.TokenRequests);
            }
        }

        private static async Task DefaultHeaders(CancellationToken token)
        {
            using (ObservabilityStub stub = PromStub())
            {
                ToolResult r = await Labels(stub, new Dictionary<string, string> { { "defaultHeaders", "X-Team: sre\nX-Trace: on" } }, null, token).ConfigureAwait(false);
                ObsTestHelper.Ok(r, "default headers");
                StubRequest last = stub.Last("/api/v1/labels")!;
                ObsTestHelper.Assert(last.Headers["X-Team"] == "sre" && last.Headers["X-Trace"] == "on", "headers sent");
                ObsTestHelper.Assert((last.Headers["User-Agent"] ?? String.Empty).Contains("Hugo", StringComparison.Ordinal), "user agent");
            }
        }

        private static async Task RateLimited(CancellationToken token)
        {
            using (ObservabilityStub stub = PromStub())
            {
                stub.FailNextWithRateLimit(1);
                ToolResult r = await Labels(stub, new Dictionary<string, string>(), null, token).ConfigureAwait(false);
                ObsTestHelper.Error(r, "rate_limited", "429");
                ObsTestHelper.Assert(r.RetryAfterSeconds == 9 && r.Metrics["retryAfterSeconds"] == 9, "retry after 9");
                ObsTestHelper.Ok(await Labels(stub, new Dictionary<string, string>(), null, token).ConfigureAwait(false), "next call ok");
            }
        }

        private static async Task TimeoutCase(CancellationToken token)
        {
            using (ObservabilityStub stub = PromStub())
            {
                stub.DelayMilliseconds = 4000;
                Stopwatch sw = Stopwatch.StartNew();
                ToolResult r = await Labels(stub, new Dictionary<string, string>(), null, token, 1).ConfigureAwait(false);
                ObsTestHelper.Error(r, "timeout", "slow backend");
                ObsTestHelper.Assert(sw.Elapsed < TimeSpan.FromSeconds(3), "returned at the timeout");
                ObsTestHelper.Assert(r.Text.Contains("prom_label_values", StringComparison.Ordinal), "names the tool");
            }
        }

        private static async Task Cancellation(CancellationToken token)
        {
            using (ObservabilityStub stub = PromStub())
            using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                stub.DelayMilliseconds = 4000;
                cts.CancelAfter(200);
                bool cancelled = false;
                try
                {
                    await Labels(stub, new Dictionary<string, string>(), null, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }
                ObsTestHelper.Assert(cancelled, "OperationCanceledException");

                using (CancellationTokenSource pre = new CancellationTokenSource())
                {
                    pre.Cancel();
                    bool early = false;
                    try
                    {
                        await Labels(stub, new Dictionary<string, string>(), null, pre.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        early = true;
                    }
                    ObsTestHelper.Assert(early, "pre-cancelled token");
                }
            }
        }

        private static async Task TooLarge(CancellationToken token)
        {
            using (ObservabilityStub stub = new ObservabilityStub())
            {
                PromStubRoutes routes = new PromStubRoutes { SeriesCount = 3000 };
                stub.Handler = routes.Handle;
                ConnectorResourceContext r = ObsTestHelper.Resource("prometheus", ObsTestHelper.Settings(stub, new Dictionary<string, string> { { "maxResponseBytes", "65536" } }));
                ToolResult result = await ObsTestHelper.RunAsync(new PrometheusConnector(), "prom_query", "{\"query\":\"up\"}", ObsTestHelper.Context(r), token).ConfigureAwait(false);
                ObsTestHelper.Error(result, "too_large", "huge response");
                ObsTestHelper.Assert(result.Text.Contains("maxResponseBytes", StringComparison.Ordinal), "names the setting");
            }
        }

        private static async Task ConnectionFailed(CancellationToken token)
        {
            Dictionary<string, string> settings = new Dictionary<string, string> { { "baseUrl", "http://127.0.0.1:9" } };
            ToolResult r = await ObsTestHelper.RunAsync(new PrometheusConnector(), "prom_alerts", "{}", ObsTestHelper.Context(ObsTestHelper.Resource("prometheus", settings)), token).ConfigureAwait(false);
            ObsTestHelper.Error(r, "connection_failed", "closed port");
            foreach (IConnector c in All())
            {
                ConnectorHealthResult h = await c.TestConnectionAsync(ObsTestHelper.Resource(c.Descriptor.Type, settings), token).ConfigureAwait(false);
                ObsTestHelper.Assert(!h.Success && h.ErrorCode == "connection_failed", c.Descriptor.Type + " health connection_failed: " + h.ErrorCode);
            }
        }

        private static async Task InvalidSetting(CancellationToken token)
        {
            ToolResult r = await ObsTestHelper.RunAsync(new LokiConnector(), "loki_labels", "{}", ObsTestHelper.Context(ObsTestHelper.Resource("loki", new Dictionary<string, string> { { "baseUrl", "not a url" } })), token).ConfigureAwait(false);
            ObsTestHelper.Error(r, "invalid_setting", "bad baseUrl");
            ToolResult a = await ObsTestHelper.RunAsync(new ElasticsearchConnector(), "es_cluster_health", "{}", ObsTestHelper.Context(ObsTestHelper.Resource("elasticsearch", new Dictionary<string, string> { { "baseUrl", "http://127.0.0.1:9" }, { "authType", "Magic" } })), token).ConfigureAwait(false);
            ObsTestHelper.Error(a, "invalid_setting", "bad authType");
        }

        private static Dictionary<string, Dictionary<string, string>> Slot(string slot, string k1, string v1, string? k2 = null, string? v2 = null)
        {
            Dictionary<string, string> fields = new Dictionary<string, string> { { k1, v1 } };
            if (k2 != null && v2 != null) fields[k2] = v2;
            return new Dictionary<string, Dictionary<string, string>> { { slot, fields } };
        }

        private static void ExpectCode(Action action, string code)
        {
            try
            {
                action();
            }
            catch (ConnectorException e)
            {
                ObsTestHelper.Assert(e.Code == code, "expected " + code + " but got " + e.Code);
                return;
            }
            throw new InvalidOperationException("Assertion failed: expected ConnectorException " + code);
        }

        #endregion
    }
}
