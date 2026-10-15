namespace Test.Shared.Connectors.Http
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Http;
    using Hugo.Core.Connectors;
    using Touchstone.Core;

    /// <summary>
    /// <c>http</c> connector cases that need no network: descriptor, schemas, family rule, validation, option parsing,
    /// the path guard, overrides, and classification summaries.
    /// </summary>
    public static class HttpUnitCases
    {
        #region Private-Members

        private static readonly HttpConnector _Connector = new HttpConnector();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the cases.
        /// </summary>
        /// <param name="suiteId">Suite identifier.</param>
        /// <returns>Cases.</returns>
        public static List<TestCaseDescriptor> Build(string suiteId)
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(Case(suiteId, "descriptor", "Descriptor has type, family, category, icon, slots, and instructions", Descriptor));
            cases.Add(Case(suiteId, "settings-schema", "Settings schema lists every documented key with defaults and ranges", SettingsSchema));
            cases.Add(Case(suiteId, "tools", "Tool names, base effects, and per-call flags", ToolsShape));
            cases.Add(Case(suiteId, "tool-schemas", "Tool schemas parse, describe every property, and pass the registry", ToolSchemas));
            cases.Add(Case(suiteId, "family-schemas", "Same-named tools in the http family share one input schema", FamilySchemas));
            cases.Add(Case(suiteId, "validate-ok", "Minimal and fully specified resources validate", ValidateOk));
            cases.Add(Case(suiteId, "validate-errors", "Bad base URL, headers, methods, prefixes, overrides, and ranges are reported per field", ValidateErrors));
            cases.Add(Case(suiteId, "validate-auth", "Each auth type requires its secret slot, and OAuth2 requires a token URL", ValidateAuth));
            cases.Add(Case(suiteId, "options-parse", "Options apply defaults, parse lists, and clamp ranges", OptionsParse));
            cases.Add(Case(suiteId, "override-parse", "Effect override lines parse and reject malformed entries", OverrideParse));
            cases.Add(Case(suiteId, "path-guard-encoding", "The path guard normalizes and re-encodes accepted paths", PathGuardEncoding));
            cases.Add(Case(suiteId, "path-guard-codes", "The path guard reports invalid_argument for escapes and permission_denied outside prefixes", PathGuardCodes));
            cases.Add(Case(suiteId, "classify-summary", "http_request classification carries a reason and a METHOD path on alias summary", ClassifySummary));
            cases.Add(Case(suiteId, "classify-health", "http_health_check is always Read", ClassifyHealth));
            cases.Add(Case(suiteId, "instructions", "Default instructions cover the key guidance and contain no em-dashes", Instructions));
            return cases;
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string suiteId, string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(suiteId, id, name, body);
        }

        private static Task Descriptor(CancellationToken token)
        {
            ConnectorDescriptor d = _Connector.Descriptor;
            HttpTestHelper.Assert(d.Type == "http" && d.Family == "http", "type and family");
            HttpTestHelper.Assert(d.Category == ConnectorCategoryEnum.Http && d.IconKey == "globe", "category and icon");
            HttpTestHelper.Assert(d.DefaultInstructionsKey == "connector.http.instructions", "instructions key");
            HttpTestHelper.Assert(d.DefaultInstructions.Length > 500, "instructions loaded");
            List<string> slots = d.SecretSlots.Select(s => s.Name).ToList();
            HttpTestHelper.Assert(slots.SequenceEqual(new[] { "basicAuth", "bearerToken", "apiKey", "clientCredentials" }), "slots: " + String.Join(",", slots));
            SecretSlotDescriptor cc = d.SecretSlots.Last();
            HttpTestHelper.Assert(cc.AcceptedKinds.Contains(SecretKindEnum.Generic) && cc.AcceptedKinds.Contains(SecretKindEnum.Password), "client credentials kinds");
            HttpTestHelper.Assert(d.SecretSlots[0].AcceptedKinds.SequenceEqual(new[] { SecretKindEnum.Password }), "basic kind");
            return Task.CompletedTask;
        }

        private static Task SettingsSchema(CancellationToken token)
        {
            string[] keys = new[]
            {
                "baseUrl", "defaultHeaders", "authType", "apiKeyHeaderName", "tokenUrl", "scopes", "allowedMethods", "allowedPathPrefixes",
                "methodEffectOverrides", "timeoutSeconds", "maxResponseBytes", "maxRequestBytes", "followRedirects", "maxRedirects", "verifyTls"
            };
            ConnectorSettingsSchema schema = _Connector.Descriptor.SettingsSchema;
            foreach (string k in keys) HttpTestHelper.Assert(schema.Find(k) != null, "setting " + k);
            HttpTestHelper.Assert(schema.Settings.Count == keys.Length, "no undocumented settings");
            HttpTestHelper.Assert(schema.Find("baseUrl")!.Required, "baseUrl required");
            HttpTestHelper.Assert(schema.Find("authType")!.AllowedValues.SequenceEqual(new[] { "None", "Basic", "Bearer", "ApiKeyHeader", "OAuth2ClientCredentials" }), "auth types");
            HttpTestHelper.Assert(schema.Find("timeoutSeconds")!.DefaultValue == "30" && schema.Find("timeoutSeconds")!.Maximum == 600, "timeout range");
            HttpTestHelper.Assert(schema.Find("maxResponseBytes")!.DefaultValue == "1048576", "max response default");
            HttpTestHelper.Assert(schema.Find("verifyTls")!.DefaultValue == "true" && schema.Find("followRedirects")!.DefaultValue == "true", "boolean defaults");
            HttpTestHelper.Assert(schema.Settings.All(s => !String.IsNullOrEmpty(s.Help)), "every setting has help");

            Dictionary<string, string> applied = ConnectorSettingsValidator.ApplyDefaults(_Connector.Descriptor, new Dictionary<string, string> { { "baseUrl", "https://api.example.com" } });
            ConnectorValidationResult valid = ConnectorSettingsValidator.Validate(_Connector.Descriptor, applied, null);
            HttpTestHelper.Assert(valid.IsValid, "defaults pass the server-side schema validator: " + String.Join("; ", valid.Errors.Select(e => e.Field + " " + e.Message)));
            return Task.CompletedTask;
        }

        private static Task ToolsShape(CancellationToken token)
        {
            List<string> names = _Connector.Tools.Select(t => t.Descriptor.Name).ToList();
            HttpTestHelper.Assert(names.SequenceEqual(new[] { "http_request", "http_health_check" }), "tools: " + String.Join(",", names));
            ToolDescriptor req = HttpTestHelper.Tool(_Connector, "http_request").Descriptor;
            HttpTestHelper.Assert(req.ClassifiedPerCall && req.BaseEffect == EffectEnum.Read, "http_request classified per call");
            ToolDescriptor health = HttpTestHelper.Tool(_Connector, "http_health_check").Descriptor;
            HttpTestHelper.Assert(!health.ClassifiedPerCall && health.BaseEffect == EffectEnum.Read, "health Read");
            HttpTestHelper.Assert(_Connector.Tools.All(t => t.Descriptor.Description.Length > 80 && !String.IsNullOrEmpty(t.Descriptor.Title) && !String.IsNullOrEmpty(t.Descriptor.OutputDescription)), "descriptions");
            return Task.CompletedTask;
        }

        private static Task ToolSchemas(CancellationToken token)
        {
            foreach (IConnectorTool tool in _Connector.Tools)
            {
                using (JsonDocument doc = JsonDocument.Parse(tool.Descriptor.InputSchemaJson))
                {
                    JsonElement root = doc.RootElement;
                    HttpTestHelper.Assert(root.GetProperty("type").GetString() == "object", tool.Descriptor.Name + " object schema");
                    HttpTestHelper.Assert(root.GetProperty("additionalProperties").ValueKind == JsonValueKind.False, tool.Descriptor.Name + " closed schema");
                    foreach (JsonProperty p in root.GetProperty("properties").EnumerateObject())
                    {
                        HttpTestHelper.Assert(p.Value.TryGetProperty("description", out JsonElement desc) && desc.GetString()!.Length > 10, tool.Descriptor.Name + "." + p.Name + " described");
                        HttpTestHelper.Assert(p.Name != "resource", "no reserved resource argument");
                    }
                }
            }
            using (ConnectorRegistry registry = new ConnectorRegistry())
            {
                registry.Register(_Connector);
                HttpTestHelper.Assert(registry.FindTool("http", "http_request") != null, "registered");
            }
            return Task.CompletedTask;
        }

        private static Task FamilySchemas(CancellationToken token)
        {
            using (ConnectorRegistry registry = new ConnectorRegistry())
            {
                registry.Register(_Connector);
                List<FamilyTool> tools = registry.FamilyTools("http");
                HttpTestHelper.Assert(tools.Count == 2, "two family tools");
                foreach (FamilyTool ft in tools)
                {
                    HttpTestHelper.Assert(ft.Descriptor.Name.StartsWith("http_", StringComparison.Ordinal), "family prefix " + ft.Descriptor.Name);
                    HttpTestHelper.Assert(ft.ConnectorTypes.SequenceEqual(new[] { "http" }), "connector types");
                }

                HttpConnector second = new HttpConnector(new HttpConnectorOptions { BinaryPreviewBytes = 0 });
                foreach (IConnectorTool tool in second.Tools)
                {
                    IConnectorTool peer = HttpTestHelper.Tool(_Connector, tool.Descriptor.Name);
                    HttpTestHelper.Assert(peer.Descriptor.InputSchemaJson == tool.Descriptor.InputSchemaJson, "identical schema for " + tool.Descriptor.Name + " across instances");
                }
            }
            return Task.CompletedTask;
        }

        private static async Task ValidateOk(CancellationToken token)
        {
            ConnectorValidationResult minimal = await _Connector.ValidateAsync(HttpTestHelper.Resource(HttpTestHelper.Settings("https://api.example.com")), token).ConfigureAwait(false);
            HttpTestHelper.Assert(minimal.IsValid, "minimal valid: " + Errors(minimal));
            Dictionary<string, string> full = HttpTestHelper.Settings(
                "https://api.example.com/v1/",
                HttpSettingKeys.DefaultHeaders, "Accept: application/json\nX-Tenant: acme",
                HttpSettingKeys.AuthType, "OAuth2ClientCredentials",
                HttpSettingKeys.TokenUrl, "https://login.example.com/oauth/token",
                HttpSettingKeys.Scopes, "read write\nadmin",
                HttpSettingKeys.AllowedMethods, "GET\nPOST",
                HttpSettingKeys.AllowedPathPrefixes, "/orders\n/search",
                HttpSettingKeys.MethodEffectOverrides, "POST /search=Read",
                HttpSettingKeys.TimeoutSeconds, "10",
                HttpSettingKeys.MaxResponseBytes, "4096",
                HttpSettingKeys.MaxRequestBytes, "1000",
                HttpSettingKeys.FollowRedirects, "false",
                HttpSettingKeys.MaxRedirects, "0",
                HttpSettingKeys.VerifyTls, "true");
            Dictionary<string, Dictionary<string, string>> secrets = new Dictionary<string, Dictionary<string, string>>
            {
                { "clientCredentials", new Dictionary<string, string> { { "username", "id" }, { "password", "secret" } } }
            };
            ConnectorValidationResult ok = await _Connector.ValidateAsync(HttpTestHelper.Resource(full, secrets), token).ConfigureAwait(false);
            HttpTestHelper.Assert(ok.IsValid, "full valid (Password-kind client credentials): " + Errors(ok));
        }

        private static async Task ValidateErrors(CancellationToken token)
        {
            ConnectorValidationResult missing = await _Connector.ValidateAsync(HttpTestHelper.Resource(new Dictionary<string, string>()), token).ConfigureAwait(false);
            HttpTestHelper.Assert(missing.Errors.Any(e => e.Field == "baseUrl" && e.Code == "required"), "baseUrl required");

            foreach (string bad in new[] { "ftp://x.example", "api.example.com", "https://user:pw@api.example.com", "https://api.example.com/?a=1", "https://api.example.com/#x" })
            {
                ConnectorValidationResult r = await _Connector.ValidateAsync(HttpTestHelper.Resource(HttpTestHelper.Settings(bad)), token).ConfigureAwait(false);
                HttpTestHelper.Assert(r.Errors.Any(e => e.Field == "baseUrl" && e.Code == "invalid_format"), "baseUrl rejected: " + bad);
                HttpTestHelper.Assert(!Errors(r).Contains(":pw@", StringComparison.Ordinal), "credentials not echoed");
            }

            Dictionary<string, string> s = HttpTestHelper.Settings(
                "https://api.example.com",
                HttpSettingKeys.DefaultHeaders, "Authorization: Bearer abc",
                HttpSettingKeys.AuthType, "Kerberos",
                HttpSettingKeys.ApiKeyHeaderName, "X API",
                HttpSettingKeys.AllowedMethods, "GET\nPO ST\nG(T",
                HttpSettingKeys.AllowedPathPrefixes, "orders",
                HttpSettingKeys.MethodEffectOverrides, "POST /search=Maybe",
                HttpSettingKeys.TimeoutSeconds, "0",
                HttpSettingKeys.MaxResponseBytes, "10",
                HttpSettingKeys.MaxRequestBytes, "-1",
                HttpSettingKeys.MaxRedirects, "50",
                HttpSettingKeys.FollowRedirects, "maybe",
                HttpSettingKeys.VerifyTls, "sometimes");
            ConnectorValidationResult errors = await _Connector.ValidateAsync(HttpTestHelper.Resource(s), token).ConfigureAwait(false);
            string[] fields = new[] { "defaultHeaders", "authType", "apiKeyHeaderName", "allowedMethods", "allowedPathPrefixes", "methodEffectOverrides", "timeoutSeconds", "maxResponseBytes", "maxRequestBytes", "maxRedirects", "followRedirects", "verifyTls" };
            foreach (string f in fields) HttpTestHelper.Assert(errors.Errors.Any(e => e.Field == f), "error on " + f + "; got " + Errors(errors));

            foreach (string header in new[] { "Cookie: a=b", "X-API-Key: k", "Host: evil", "NoColon", "Bad Name: v" })
            {
                ConnectorValidationResult r = await _Connector.ValidateAsync(HttpTestHelper.Resource(HttpTestHelper.Settings("https://api.example.com", HttpSettingKeys.DefaultHeaders, header)), token).ConfigureAwait(false);
                HttpTestHelper.Assert(r.Errors.Any(e => e.Field == "defaultHeaders"), "default header rejected: " + header);
            }

            foreach (string prefix in new[] { "/a/../b", "/a?x", "/a#b", "/a%2f", "//x", "/a\\b", "/a*" })
            {
                ConnectorValidationResult r = await _Connector.ValidateAsync(HttpTestHelper.Resource(HttpTestHelper.Settings("https://api.example.com", HttpSettingKeys.AllowedPathPrefixes, prefix)), token).ConfigureAwait(false);
                HttpTestHelper.Assert(r.Errors.Any(e => e.Field == "allowedPathPrefixes"), "prefix rejected: " + prefix);
            }
        }

        private static async Task ValidateAuth(CancellationToken token)
        {
            Dictionary<string, string> expected = new Dictionary<string, string>
            {
                { "Basic", "basicAuth" },
                { "Bearer", "bearerToken" },
                { "ApiKeyHeader", "apiKey" },
                { "OAuth2ClientCredentials", "clientCredentials" }
            };
            foreach (KeyValuePair<string, string> pair in expected)
            {
                Dictionary<string, string> s = HttpTestHelper.Settings("https://api.example.com", HttpSettingKeys.AuthType, pair.Key, HttpSettingKeys.TokenUrl, "https://login.example.com/token");
                ConnectorValidationResult none = await _Connector.ValidateAsync(HttpTestHelper.Resource(s), token).ConfigureAwait(false);
                HttpTestHelper.Assert(none.Errors.Any(e => e.Field == pair.Value && e.Code == "required"), pair.Key + " needs " + pair.Value + "; got " + Errors(none));
                ConnectorValidationResult bound = await _Connector.ValidateAsync(HttpTestHelper.Resource(s, HttpTestHelper.AllSecrets("cid", "csecret")), token).ConfigureAwait(false);
                HttpTestHelper.Assert(bound.IsValid, pair.Key + " valid when bound: " + Errors(bound));
            }

            Dictionary<string, Dictionary<string, string>> noUser = new Dictionary<string, Dictionary<string, string>> { { "basicAuth", new Dictionary<string, string> { { "password", "p" } } } };
            ConnectorValidationResult basic = await _Connector.ValidateAsync(HttpTestHelper.Resource(HttpTestHelper.Settings("https://api.example.com", HttpSettingKeys.AuthType, "Basic"), noUser), token).ConfigureAwait(false);
            HttpTestHelper.Assert(basic.Errors.Any(e => e.Field == "basicAuth"), "basic needs a username");

            ConnectorValidationResult oauthNoUrl = await _Connector.ValidateAsync(HttpTestHelper.Resource(HttpTestHelper.Settings("https://api.example.com", HttpSettingKeys.AuthType, "OAuth2ClientCredentials"), HttpTestHelper.AllSecrets("cid", "csecret")), token).ConfigureAwait(false);
            HttpTestHelper.Assert(oauthNoUrl.Errors.Any(e => e.Field == "tokenUrl" && e.Code == "required"), "tokenUrl required");
            ConnectorValidationResult oauthBadUrl = await _Connector.ValidateAsync(HttpTestHelper.Resource(HttpTestHelper.Settings("https://api.example.com", HttpSettingKeys.AuthType, "OAuth2ClientCredentials", HttpSettingKeys.TokenUrl, "/token"), HttpTestHelper.AllSecrets("cid", "csecret")), token).ConfigureAwait(false);
            HttpTestHelper.Assert(oauthBadUrl.Errors.Any(e => e.Field == "tokenUrl" && e.Code == "invalid_format"), "tokenUrl absolute");
        }

        private static Task OptionsParse(CancellationToken token)
        {
            HttpResourceOptions d = HttpResourceOptions.FromSettings(new ResourceSettings(HttpTestHelper.Settings("https://api.example.com/v1/")));
            HttpTestHelper.Assert(d.BaseUri != null && d.BasePath == "/v1", "base path " + d.BasePath);
            HttpTestHelper.Assert(d.AuthType == HttpAuthTypeEnum.None && d.ApiKeyHeaderName == "X-API-Key", "auth defaults");
            HttpTestHelper.Assert(d.AllowedMethods.SetEquals(new[] { "GET", "HEAD", "OPTIONS", "POST", "PUT", "PATCH", "DELETE" }), "default methods");
            HttpTestHelper.Assert(d.AllowedPathPrefixes.SequenceEqual(new[] { "/" }), "default prefix");
            HttpTestHelper.Assert(d.Timeout == TimeSpan.FromSeconds(30) && d.MaxResponseBytes == 1048576 && d.MaxRequestBytes == 1048576, "limits");
            HttpTestHelper.Assert(d.FollowRedirects && d.MaxRedirects == 5 && d.VerifyTls, "redirect and TLS defaults");

            HttpResourceOptions c = HttpResourceOptions.FromSettings(new ResourceSettings(HttpTestHelper.Settings(
                "https://api.example.com",
                HttpSettingKeys.AllowedMethods, "get, post\nDELETE",
                HttpSettingKeys.AllowedPathPrefixes, "/orders/\n/search",
                HttpSettingKeys.Scopes, "a b\nc,a",
                HttpSettingKeys.DefaultHeaders, "Accept: application/json\nbroken",
                HttpSettingKeys.TimeoutSeconds, "100000",
                HttpSettingKeys.MaxResponseBytes, "1",
                HttpSettingKeys.MaxRedirects, "99",
                HttpSettingKeys.AuthType, "bearer")));
            HttpTestHelper.Assert(c.AllowedMethods.SetEquals(new[] { "GET", "POST", "DELETE" }), "methods parsed");
            HttpTestHelper.Assert(c.AllowedPathPrefixes.SequenceEqual(new[] { "/orders", "/search" }), "prefixes normalized: " + String.Join(",", c.AllowedPathPrefixes));
            HttpTestHelper.Assert(c.Scopes.SequenceEqual(new[] { "a", "b", "c" }), "scopes split and deduplicated");
            HttpTestHelper.Assert(c.DefaultHeaders.Count == 1 && c.DefaultHeaders[0].Key == "Accept", "bad header skipped");
            HttpTestHelper.Assert(c.Timeout == TimeSpan.FromSeconds(600) && c.MaxResponseBytes == 1024 && c.MaxRedirects == 20, "clamped");
            HttpTestHelper.Assert(c.AuthType == HttpAuthTypeEnum.Bearer, "auth type case-insensitive");

            HttpResourceOptions onlyBad = HttpResourceOptions.FromSettings(new ResourceSettings(HttpTestHelper.Settings("https://api.example.com", HttpSettingKeys.AllowedPathPrefixes, "../x")));
            HttpTestHelper.Assert(onlyBad.AllowedPathPrefixes.Count == 1 && onlyBad.AllowedPathPrefixes[0] != "/", "invalid prefixes fail closed instead of allowing everything");
            HttpResourceOptions noBase = HttpResourceOptions.FromSettings(new ResourceSettings(null));
            HttpTestHelper.Assert(noBase.BaseUri == null, "no base");
            bool threw = false;
            try
            {
                noBase.RequireBaseUri();
            }
            catch (ConnectorException e) when (e.Code == ConnectorErrorCodes.InvalidSetting)
            {
                threw = true;
            }
            HttpTestHelper.Assert(threw, "RequireBaseUri throws invalid_setting");
            return Task.CompletedTask;
        }

        private static Task OverrideParse(CancellationToken token)
        {
            HttpTestHelper.Assert(HttpEffectOverride.TryParse("POST /search=Read", out HttpEffectOverride? a, out string _) && a!.Method == "POST" && a.Prefix == "/search" && a.Effect == EffectEnum.Read, "basic");
            HttpTestHelper.Assert(HttpEffectOverride.TryParse("  * /admin/ = destructive ", out HttpEffectOverride? b, out string _) && b!.Method == "*" && b.Prefix == "/admin" && b.Effect == EffectEnum.Destructive, "wildcard, spaces, case");
            HttpTestHelper.Assert(HttpEffectOverride.TryParse("delete /q=Write", out HttpEffectOverride? c, out string _) && c!.Method == "DELETE" && c.ToString() == "DELETE /q=Write", "upper-cased");
            HttpTestHelper.Assert(HttpEffectOverride.TryParse("GET /a=b=Read", out HttpEffectOverride? eq, out string _) && eq!.Prefix == "/a=b", "the last = separates the effect");
            foreach (string bad in new[] { "", "POST /search", "POST=Read", "/search=Read", "POST search=Read", "POST /search=Maybe", "POST /search=1", "PO(ST /x=Read", "POST /a /b=Read", "POST /../x=Read", "POST /x=" })
            {
                HttpTestHelper.Assert(!HttpEffectOverride.TryParse(bad, out HttpEffectOverride? _, out string error) && error.Length > 0, "rejected: '" + bad + "'");
            }
            return Task.CompletedTask;
        }

        private static Task PathGuardEncoding(CancellationToken token)
        {
            Check("/orders/42", "/orders/42");
            Check("orders", "/orders");
            Check(null, "/");
            Check("", "/");
            Check("/caf%C3%A9", "/caf%C3%A9");
            Check("/café", "/caf%C3%A9");
            Check("/files/a b", "/files/a%20b");
            Check("/v1/projects/p1:run", "/v1/projects/p1:run");
            Check("/a/", "/a/");
            Check("/x@y/z,1;2", "/x@y/z,1;2");
            Check("/a%20b", "/a%20b");
            HttpTestHelper.Assert(HttpPathGuard.MatchesPrefix("/api", "/api") && HttpPathGuard.MatchesPrefix("/api/", "/api") && HttpPathGuard.MatchesPrefix("/api/x", "/api"), "prefix matches");
            HttpTestHelper.Assert(!HttpPathGuard.MatchesPrefix("/apix", "/api") && !HttpPathGuard.MatchesPrefix("/ap", "/api"), "segment boundary");
            HttpTestHelper.Assert(HttpPathGuard.TryRelativeToBase("/v1/x", "/v1", out string rel) && rel == "/x", "relative to base");
            HttpTestHelper.Assert(!HttpPathGuard.TryRelativeToBase("/v10/x", "/v1", out string _), "base boundary");
            HttpTestHelper.Assert(HttpPathGuard.TryRelativeToBase("/v1", "/v1", out string root) && root == "/", "base itself");
            return Task.CompletedTask;
        }

        private static Task PathGuardCodes(CancellationToken token)
        {
            List<string> prefixes = new List<string> { "/api" };
            HttpPathCheck outside = HttpPathGuard.Check("/admin", prefixes);
            HttpTestHelper.Assert(!outside.Allowed && outside.Code == ConnectorErrorCodes.PermissionDenied, "outside prefix is permission_denied");
            foreach (string bad in new[] { "/api/../admin", "http://x/api", "//x/api", "/api%2f..", "/api?x=1", "/api/%2e%2e", "/api\\x" })
            {
                HttpPathCheck c = HttpPathGuard.Check(bad, prefixes);
                HttpTestHelper.Assert(!c.Allowed && c.Code == ConnectorErrorCodes.InvalidArgument && c.Reason.Length > 0, "invalid_argument for " + bad + " got " + c.Code);
            }
            HttpTestHelper.Assert(HttpPathGuard.Check("/%2525252e%2525252e/x", null).Code == ConnectorErrorCodes.InvalidArgument, "deep encoding refused");
            return Task.CompletedTask;
        }

        private static Task ClassifySummary(CancellationToken token)
        {
            ConnectorResourceContext resource = HttpTestHelper.Resource(HttpTestHelper.Settings("https://api.example.com/v1", HttpSettingKeys.MethodEffectOverrides, "POST /search=Read"), HttpTestHelper.AllSecrets(null));
            ConnectorInvocationContext ctx = HttpTestHelper.Context(resource);
            IConnectorTool tool = HttpTestHelper.Tool(_Connector, "http_request");

            EffectClassification post = tool.Classify(ctx, new ToolArguments("{\"method\":\"post\",\"path\":\"/orders\",\"body\":{\"password\":\"hunter2\"}}"));
            HttpTestHelper.Assert(post.Effect == EffectEnum.Write && post.ApprovalSummary == "POST /orders on api", "summary: " + post.ApprovalSummary);
            HttpTestHelper.Assert(!post.ApprovalSummary.Contains("hunter2", StringComparison.Ordinal) && post.Reason.Contains("POST", StringComparison.Ordinal), "no body in summary, reason names method");

            EffectClassification search = tool.Classify(ctx, new ToolArguments("{\"method\":\"POST\",\"path\":\"/search\"}"));
            HttpTestHelper.Assert(search.Effect == EffectEnum.Read && search.Reason.Contains("override", StringComparison.Ordinal), "override reason: " + search.Reason);

            EffectClassification del = tool.Classify(ctx, new ToolArguments("{\"method\":\"DELETE\",\"path\":\"/orders/1\"}"));
            HttpTestHelper.Assert(del.Effect == EffectEnum.Destructive && !del.Rejected, "delete destructive");

            EffectClassification escape = tool.Classify(ctx, new ToolArguments("{\"path\":\"/../admin\"}"));
            HttpTestHelper.Assert(escape.Rejected && escape.Reason.Length > 0, "escape rejected");

            ConnectorInvocationContext noAlias = HttpTestHelper.Context(resource);
            noAlias.BindingAlias = String.Empty;
            EffectClassification named = tool.Classify(noAlias, new ToolArguments("{}"));
            HttpTestHelper.Assert(named.ApprovalSummary == "GET / on api-test", "resource name fallback: " + named.ApprovalSummary);

            EffectClassification broken = tool.Classify(HttpTestHelper.Context(HttpTestHelper.Resource(new Dictionary<string, string>())), new ToolArguments("{\"method\":\"DELETE\"}"));
            HttpTestHelper.Assert(broken.Effect == EffectEnum.Destructive, "classification works without a base URL");
            return Task.CompletedTask;
        }

        private static Task ClassifyHealth(CancellationToken token)
        {
            IConnectorTool tool = HttpTestHelper.Tool(_Connector, "http_health_check");
            ConnectorInvocationContext ctx = HttpTestHelper.Context(HttpTestHelper.Resource(HttpTestHelper.Settings("https://api.example.com")));
            EffectClassification c = tool.Classify(ctx, new ToolArguments("{\"path\":\"/health\"}"));
            HttpTestHelper.Assert(c.Effect == EffectEnum.Read && !c.Rejected && c.ApprovalSummary.Length > 0, "read");
            return Task.CompletedTask;
        }

        private static Task Instructions(CancellationToken token)
        {
            string text = _Connector.Descriptor.DefaultInstructions;
            foreach (string needle in new[] { "http_health_check", "http_request", "allowed prefixes", "Never put credentials", "Read", "Destructive" })
                HttpTestHelper.Assert(text.Contains(needle, StringComparison.Ordinal), "instructions mention " + needle);
            HttpTestHelper.Assert(!text.Contains((char)0x2014), "no em-dashes");
            return Task.CompletedTask;
        }

        private static void Check(string? path, string expectedEncoded)
        {
            HttpPathCheck c = HttpPathGuard.Check(path, null);
            HttpTestHelper.Assert(c.Allowed && c.EncodedPath == expectedEncoded, "path '" + path + "' encoded as " + c.EncodedPath + " (" + c.Reason + "), expected " + expectedEncoded);
        }

        private static string Errors(ConnectorValidationResult r)
        {
            return String.Join("; ", r.Errors.Select(e => e.Field + "/" + e.Code + ": " + e.Message));
        }

        #endregion
    }
}
