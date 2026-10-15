namespace Test.Shared.Connectors.Http
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Http;
    using Touchstone.Core;

    /// <summary>
    /// <c>http</c> connector cases against <see cref="HttpStubServer"/> on 127.0.0.1: requests, authentication
    /// (including OAuth2 caching and refresh), allow lists, redirects, limits, timeouts, cancellation, health checks
    /// over HTTP and HTTPS, and secret hygiene.
    /// </summary>
    public static class HttpStubCases
    {
        #region Public-Methods

        /// <summary>
        /// Build the cases.
        /// </summary>
        /// <param name="suiteId">Suite identifier.</param>
        /// <returns>Cases.</returns>
        public static List<TestCaseDescriptor> Build(string suiteId)
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(Case(suiteId, "request-get", "GET joins the base path, encodes the query, sends default headers, and redacts Set-Cookie", RequestGet));
            cases.Add(Case(suiteId, "request-bodies", "JSON, string, and header-typed bodies are sent with the right content type", RequestBodies));
            cases.Add(Case(suiteId, "request-status-codes", "HTTP error statuses are reported, not raised, and 429 carries Retry-After", RequestStatusCodes));
            cases.Add(Case(suiteId, "request-argument-rules", "Reserved headers, bad bodies, disallowed methods, and escaping paths are refused before sending", RequestArgumentRules));
            cases.Add(Case(suiteId, "auth-basic", "Basic credentials are sent; a wrong password surfaces as a 401 status", AuthBasic));
            cases.Add(Case(suiteId, "auth-bearer-apikey", "Bearer tokens and API keys use the right headers, including a custom header name", AuthBearerApiKey));
            cases.Add(Case(suiteId, "auth-oauth2-cache", "OAuth2 client credentials tokens are fetched once, cached, and sent with scopes", AuthOAuth2Cache));
            cases.Add(Case(suiteId, "auth-oauth2-refresh", "A revoked OAuth2 token is refreshed after a 401 and the call retried once; near-expiry tokens are refreshed", AuthOAuth2Refresh));
            cases.Add(Case(suiteId, "auth-oauth2-failure", "A token endpoint refusal is auth_failed and never echoes the client secret", AuthOAuth2Failure));
            cases.Add(Case(suiteId, "auth-secret-missing", "An auth type without its secret is secret_missing", AuthSecretMissing));
            cases.Add(Case(suiteId, "redirects-follow", "Same-origin redirects are followed with auth; 303 turns POST into GET and 307 keeps the body", RedirectsFollow));
            cases.Add(Case(suiteId, "redirects-policy", "Redirects off-host, outside the base path or prefixes, beyond maxRedirects, or when disabled are not followed", RedirectsPolicy));
            cases.Add(Case(suiteId, "redirects-drop-auth", "A redirect to another port on the same host is followed without credentials", RedirectsDropAuth));
            cases.Add(Case(suiteId, "response-caps", "maxResponseBytes cuts the body and output limits bound the text", ResponseCaps));
            cases.Add(Case(suiteId, "binary-body", "Binary bodies are summarized with a base64 preview", BinaryBody));
            cases.Add(Case(suiteId, "timeout", "Slow servers time out at the shorter of the call and resource timeouts", TimeoutCase));
            cases.Add(Case(suiteId, "cancellation", "Caller cancellation propagates as OperationCanceledException", Cancellation));
            cases.Add(Case(suiteId, "connection-refused", "A closed port is connection_failed for requests and unhealthy for health checks", ConnectionRefused));
            cases.Add(Case(suiteId, "test-connection", "TestConnectionAsync reports ok, auth_failed, upstream_error, connection_failed, and timeout", TestConnection));
            cases.Add(Case(suiteId, "health-http", "Health checks report status, latency, body match, expected codes, HEAD, and redirect chains", HealthHttp));
            cases.Add(Case(suiteId, "health-https-unverified", "With verifyTls false the self-signed certificate is accepted and reported with its expiry and errors", HealthHttpsUnverified));
            cases.Add(Case(suiteId, "health-https-verified", "With verifyTls true the self-signed certificate is rejected; the health check still reports it", HealthHttpsVerified));
            cases.Add(Case(suiteId, "https-downgrade", "A redirect from https to http is not followed", HttpsDowngrade));
            cases.Add(Case(suiteId, "secret-hygiene", "No secret value appears in results, errors, or health reports", SecretHygiene));
            return cases;
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string suiteId, string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(suiteId, id, name, body);
        }

        private static ConnectorInvocationContext Ctx(HttpStubServer stub, string basePath, double timeoutSeconds = 30, OutputLimits? limits = null, params string[] extra)
        {
            return HttpTestHelper.Context(HttpTestHelper.Resource(HttpTestHelper.Settings(stub.BaseAddress + basePath, extra), HttpTestHelper.AllSecrets(stub)), timeoutSeconds, limits);
        }

        private static ConnectorInvocationContext Ctx(HttpStubServer stub, params string[] extra)
        {
            return Ctx(stub, "/v1", 30, null, extra);
        }

        private static HttpConnector Isolated()
        {
            return new HttpConnector(new HttpConnectorOptions { TokenCache = new OAuth2TokenCache() });
        }

        private static async Task RequestGet(CancellationToken token)
        {
            using (HttpStubServer stub = new HttpStubServer())
            {
                stub.Handler = r => HttpStubResponse.Json(200, "{\"id\":42,\"status\":\"shipped\"}").With("X-Trace", "t-1").With("Set-Cookie", "session=abc123; HttpOnly");
                ConnectorInvocationContext ctx = Ctx(stub, HttpSettingKeys.DefaultHeaders, "Accept: application/json\nX-Tenant: acme");
                string args = HttpTestHelper.Json(new { method = "GET", path = "/orders/42", query = new Dictionary<string, string> { { "q", "a b&c" }, { "limit", "5" } }, headers = new Dictionary<string, string> { { "X-Request-Id", "r1" } } });
                ToolResult r = await HttpTestHelper.RunAsync(Isolated(), "http_request", args, ctx, token).ConfigureAwait(false);
                HttpTestHelper.AssertOk(r, "get");
                HttpStubRequest seen = stub.Requests.Single();
                HttpTestHelper.Assert(seen.Method == "GET" && seen.Path == "/v1/orders/42", "path " + seen.Path);
                HttpTestHelper.Assert(seen.QueryValue("q") == "a b&c" && seen.QueryValue("limit") == "5", "query " + seen.Query);
                HttpTestHelper.Assert(seen.Header("X-Tenant") == "acme" && seen.Header("Accept") == "application/json" && seen.Header("X-Request-Id") == "r1", "headers");
                HttpTestHelper.Assert(seen.Header("Authorization") == null, "no auth when authType None");
                HttpTestHelper.Assert(r.Text.StartsWith("HTTP 200 OK", StringComparison.Ordinal) && r.Text.Contains("\"status\":\"shipped\"", StringComparison.Ordinal), "text: " + r.Text);
                HttpTestHelper.Assert(r.Text.Contains("X-Trace: t-1", StringComparison.Ordinal) && r.Text.Contains("Set-Cookie: [redacted]", StringComparison.Ordinal), "headers rendered");
                HttpTestHelper.Assert(!r.Text.Contains("abc123", StringComparison.Ordinal) && !(r.StructuredJson ?? String.Empty).Contains("abc123", StringComparison.Ordinal), "cookie redacted");
                HttpTestHelper.Assert(HttpTestHelper.Structured(r, "status") == "200" && r.Metrics["httpStatus"] == 200, "structured status");
                HttpTestHelper.Assert(!r.Truncated, "not truncated");

                ToolResult root = await HttpTestHelper.RunAsync(Isolated(), "http_request", "{}", ctx, token).ConfigureAwait(false);
                HttpTestHelper.AssertOk(root, "root");
                HttpTestHelper.Assert(stub.Requests.Last().Path == "/v1", "root maps to the base URL path: " + stub.Requests.Last().Path);
            }
        }

        private static async Task RequestBodies(CancellationToken token)
        {
            using (HttpStubServer stub = new HttpStubServer())
            {
                stub.Handler = r => HttpStubResponse.Json(201, "{\"ok\":true}");
                HttpConnector c = Isolated();
                ToolResult json = await HttpTestHelper.RunAsync(c, "http_request", "{\"method\":\"POST\",\"path\":\"/orders\",\"body\":{\"name\":\"x\",\"n\":1}}", Ctx(stub), token).ConfigureAwait(false);
                HttpTestHelper.AssertOk(json, "json");
                HttpStubRequest a = stub.Requests.Last();
                HttpTestHelper.Assert(a.Method == "POST" && a.Body == "{\"name\":\"x\",\"n\":1}" && a.Header("Content-Type") == "application/json", "json body " + a.Body + " " + a.Header("Content-Type"));
                HttpTestHelper.Assert(json.Text.StartsWith("HTTP 201", StringComparison.Ordinal), "201");

                ToolResult csv = await HttpTestHelper.RunAsync(c, "http_request", "{\"method\":\"PUT\",\"path\":\"/import\",\"body\":\"a,b\\n1,2\",\"contentType\":\"text/csv\"}", Ctx(stub), token).ConfigureAwait(false);
                HttpTestHelper.AssertOk(csv, "csv");
                HttpStubRequest b = stub.Requests.Last();
                HttpTestHelper.Assert(b.Method == "PUT" && b.Body == "a,b\n1,2" && b.Header("Content-Type") == "text/csv", "string body");

                ToolResult header = await HttpTestHelper.RunAsync(c, "http_request", "{\"method\":\"PATCH\",\"path\":\"/x\",\"body\":\"<a/>\",\"headers\":{\"Content-Type\":\"application/xml\"}}", Ctx(stub), token).ConfigureAwait(false);
                HttpTestHelper.AssertOk(header, "header content type");
                HttpTestHelper.Assert(stub.Requests.Last().Header("Content-Type") == "application/xml", "content type from headers");

                ToolResult array = await HttpTestHelper.RunAsync(c, "http_request", "{\"method\":\"POST\",\"path\":\"/batch\",\"body\":[1,2,3]}", Ctx(stub), token).ConfigureAwait(false);
                HttpTestHelper.AssertOk(array, "array");
                HttpTestHelper.Assert(stub.Requests.Last().Body == "[1,2,3]", "array body");
            }
        }

        private static async Task RequestStatusCodes(CancellationToken token)
        {
            using (HttpStubServer stub = new HttpStubServer())
            {
                stub.Handler = r =>
                {
                    if (r.Path.EndsWith("/missing", StringComparison.Ordinal)) return HttpStubResponse.Json(404, "{\"error\":\"not found\"}");
                    if (r.Path.EndsWith("/busy", StringComparison.Ordinal)) return HttpStubResponse.Text(429, "slow down").With("Retry-After", "7");
                    return HttpStubResponse.Text(500, "boom");
                };
                HttpConnector c = Isolated();
                ToolResult missing = await HttpTestHelper.RunAsync(c, "http_request", "{\"path\":\"/missing\"}", Ctx(stub), token).ConfigureAwait(false);
                HttpTestHelper.Assert(!missing.IsError && missing.Metrics["httpStatus"] == 404 && missing.Text.StartsWith("HTTP 404", StringComparison.Ordinal), "404 reported");
                ToolResult busy = await HttpTestHelper.RunAsync(c, "http_request", "{\"path\":\"/busy\"}", Ctx(stub), token).ConfigureAwait(false);
                HttpTestHelper.Assert(!busy.IsError && busy.RetryAfterSeconds == 7 && HttpTestHelper.Structured(busy, "retryAfterSeconds") == "7", "429 retry-after " + busy.RetryAfterSeconds);
                ToolResult boom = await HttpTestHelper.RunAsync(c, "http_request", "{\"path\":\"/boom\"}", Ctx(stub), token).ConfigureAwait(false);
                HttpTestHelper.Assert(!boom.IsError && boom.Metrics["httpStatus"] == 500 && boom.Text.Contains("boom", StringComparison.Ordinal), "500 reported");
            }
        }

        private static async Task RequestArgumentRules(CancellationToken token)
        {
            using (HttpStubServer stub = new HttpStubServer())
            {
                HttpConnector c = Isolated();
                ConnectorInvocationContext ctx = Ctx(stub, "/v1", 30, null, HttpSettingKeys.AllowedMethods, "GET\nHEAD\nPOST", HttpSettingKeys.AllowedPathPrefixes, "/data", HttpSettingKeys.MaxRequestBytes, "10", HttpSettingKeys.AuthType, "ApiKeyHeader");
                foreach (string h in new[] { "Authorization", "authorization", "Cookie", "Proxy-Authorization", "X-API-Key", "Host", "Content-Length", "Transfer-Encoding", "Connection" })
                {
                    ToolResult r = await HttpTestHelper.RunAsync(c, "http_request", "{\"path\":\"/data\",\"headers\":{\"" + h + "\":\"x\"}}", ctx, token).ConfigureAwait(false);
                    HttpTestHelper.AssertError(r, "invalid_argument", "header " + h);
                }
                Expect(await HttpTestHelper.RunAsync(c, "http_request", "{\"path\":\"/data\",\"headers\":{\"X-Ok\":\"a\\r\\nX-Bad: 1\"}}", ctx, token).ConfigureAwait(false), "invalid_argument", "CRLF header value");
                Expect(await HttpTestHelper.RunAsync(c, "http_request", "{\"path\":\"/data\",\"headers\":{\"Bad Name\":\"a\"}}", ctx, token).ConfigureAwait(false), "invalid_argument", "bad header name");
                Expect(await HttpTestHelper.RunAsync(c, "http_request", "{\"method\":\"HEAD\",\"path\":\"/data\",\"body\":\"x\"}", ctx, token).ConfigureAwait(false), "invalid_argument", "HEAD with body");
                Expect(await HttpTestHelper.RunAsync(c, "http_request", "{\"method\":\"POST\",\"path\":\"/data\",\"body\":\"01234567890\"}", ctx, token).ConfigureAwait(false), "too_large", "body over maxRequestBytes");
                Expect(await HttpTestHelper.RunAsync(c, "http_request", "{\"method\":\"DELETE\",\"path\":\"/data\"}", ctx, token).ConfigureAwait(false), "permission_denied", "method not allowed");
                Expect(await HttpTestHelper.RunAsync(c, "http_request", "{\"method\":\"G T\",\"path\":\"/data\"}", ctx, token).ConfigureAwait(false), "invalid_argument", "invalid method");
                Expect(await HttpTestHelper.RunAsync(c, "http_request", "{\"path\":\"/other\"}", ctx, token).ConfigureAwait(false), "permission_denied", "outside prefixes");
                Expect(await HttpTestHelper.RunAsync(c, "http_request", "{\"path\":\"/data/../admin\"}", ctx, token).ConfigureAwait(false), "invalid_argument", "traversal");
                Expect(await HttpTestHelper.RunAsync(c, "http_request", "{\"path\":\"/data/%2e%2e/admin\"}", ctx, token).ConfigureAwait(false), "invalid_argument", "encoded traversal");
                Expect(await HttpTestHelper.RunAsync(c, "http_request", "{\"path\":\"" + stub.BaseAddress + "/v1/data\"}", ctx, token).ConfigureAwait(false), "invalid_argument", "absolute URL");
                Expect(await HttpTestHelper.RunAsync(c, "http_request", "{\"path\":\"//127.0.0.1:1/data\"}", ctx, token).ConfigureAwait(false), "invalid_argument", "protocol-relative URL");
                Expect(await HttpTestHelper.RunAsync(c, "http_request", "{\"path\":\"/data?x=1\"}", ctx, token).ConfigureAwait(false), "invalid_argument", "query in path");
                Expect(await HttpTestHelper.RunAsync(c, "http_health_check", "{\"path\":\"/other\"}", ctx, token).ConfigureAwait(false), "permission_denied", "health outside prefixes");
                HttpTestHelper.Assert(stub.Requests.IsEmpty, "nothing was sent: " + stub.Requests.Count);

                ToolResult ok = await HttpTestHelper.RunAsync(c, "http_request", "{\"method\":\"POST\",\"path\":\"/data/items\",\"body\":\"0123456789\"}", ctx, token).ConfigureAwait(false);
                HttpTestHelper.AssertOk(ok, "allowed call within limits");
                HttpTestHelper.Assert(stub.Requests.Single().Path == "/v1/data/items", "sent once");
            }
        }

        private static async Task AuthBasic(CancellationToken token)
        {
            using (HttpStubServer stub = new HttpStubServer())
            {
                string expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("stub-user:" + HttpTestHelper.BasicPassword));
                stub.Handler = r => r.Header("Authorization") == expected ? HttpStubResponse.Text(200, "welcome") : HttpStubResponse.Text(401, "denied").With("WWW-Authenticate", "Basic realm=\"stub\"");
                ConnectorInvocationContext ctx = Ctx(stub, HttpSettingKeys.AuthType, "Basic");
                ToolResult ok = await HttpTestHelper.RunAsync(Isolated(), "http_request", "{\"path\":\"/me\"}", ctx, token).ConfigureAwait(false);
                HttpTestHelper.Assert(!ok.IsError && ok.Metrics["httpStatus"] == 200, "basic accepted");

                Dictionary<string, Dictionary<string, string>> wrong = HttpTestHelper.AllSecrets(stub);
                wrong["basicAuth"]["password"] = "wrong-password-xyz";
                ConnectorResourceContext bad = HttpTestHelper.Resource(HttpTestHelper.Settings(stub.BaseAddress + "/v1", HttpSettingKeys.AuthType, "Basic"), wrong);
                ToolResult denied = await HttpTestHelper.RunAsync(Isolated(), "http_request", "{\"path\":\"/me\"}", HttpTestHelper.Context(bad), token).ConfigureAwait(false);
                HttpTestHelper.Assert(!denied.IsError && denied.Metrics["httpStatus"] == 401, "401 reported as status");
                HttpTestHelper.AssertNoSecrets(denied, new[] { "wrong-password-xyz", HttpTestHelper.BasicPassword }, "basic denied");
                ConnectorHealthResult health = await Isolated().TestConnectionAsync(bad, token).ConfigureAwait(false);
                HttpTestHelper.Assert(!health.Success && health.ErrorCode == "auth_failed", "test connection auth_failed: " + health.ErrorCode);
            }
        }

        private static async Task AuthBearerApiKey(CancellationToken token)
        {
            using (HttpStubServer stub = new HttpStubServer())
            {
                stub.Handler = r => HttpStubResponse.Text(200, "ok");
                HttpConnector c = Isolated();
                await HttpTestHelper.RunAsync(c, "http_request", "{\"path\":\"/a\"}", Ctx(stub, HttpSettingKeys.AuthType, "Bearer"), token).ConfigureAwait(false);
                HttpTestHelper.Assert(stub.Requests.Last().Header("Authorization") == "Bearer " + HttpTestHelper.BearerToken, "bearer header");

                await HttpTestHelper.RunAsync(c, "http_request", "{\"path\":\"/b\"}", Ctx(stub, HttpSettingKeys.AuthType, "ApiKeyHeader"), token).ConfigureAwait(false);
                HttpStubRequest def = stub.Requests.Last();
                HttpTestHelper.Assert(def.Header("X-API-Key") == HttpTestHelper.ApiKey && def.Header("Authorization") == null, "default api key header");

                ConnectorInvocationContext custom = Ctx(stub, HttpSettingKeys.AuthType, "ApiKeyHeader", HttpSettingKeys.ApiKeyHeaderName, "X-Custom-Key");
                await HttpTestHelper.RunAsync(c, "http_request", "{\"path\":\"/c\"}", custom, token).ConfigureAwait(false);
                HttpTestHelper.Assert(stub.Requests.Last().Header("X-Custom-Key") == HttpTestHelper.ApiKey, "custom api key header");
                ToolResult refused = await HttpTestHelper.RunAsync(c, "http_request", "{\"path\":\"/c\",\"headers\":{\"x-custom-key\":\"forged\"}}", custom, token).ConfigureAwait(false);
                HttpTestHelper.AssertError(refused, "invalid_argument", "custom key header reserved");
            }
        }

        private static async Task AuthOAuth2Cache(CancellationToken token)
        {
            using (HttpStubServer stub = new HttpStubServer())
            {
                stub.Handler = r => stub.HasValidToken(r) ? HttpStubResponse.Text(200, "data") : HttpStubResponse.Text(401, "bad token");
                HttpConnector c = Isolated();
                ConnectorInvocationContext ctx = Ctx(stub, HttpSettingKeys.AuthType, "OAuth2ClientCredentials", HttpSettingKeys.TokenUrl, stub.BaseAddress + stub.TokenPath, HttpSettingKeys.Scopes, "read write");
                for (int i = 0; i < 3; i++)
                {
                    ToolResult r = await HttpTestHelper.RunAsync(c, "http_request", "{\"path\":\"/items\"}", ctx, token).ConfigureAwait(false);
                    HttpTestHelper.Assert(!r.IsError && r.Metrics["httpStatus"] == 200, "call " + i + " authorized: " + r.Text);
                }
                HttpTestHelper.Assert(stub.TokensIssued == 1, "one token for three calls, got " + stub.TokensIssued);
                HttpTestHelper.Assert(stub.LastTokenScope == "read write", "scope sent: " + stub.LastTokenScope);
                HttpStubRequest tokenRequest = stub.To(stub.TokenPath).Single();
                HttpTestHelper.Assert(tokenRequest.Method == "POST" && tokenRequest.Header("Content-Type")!.StartsWith("application/x-www-form-urlencoded", StringComparison.Ordinal), "form post");
                HttpTestHelper.Assert(!tokenRequest.Body.Contains(stub.ClientSecret, StringComparison.Ordinal), "client secret sent in the Basic header, not the body");

                await Task.WhenAll(Enumerable.Range(0, 8).Select(i => HttpTestHelper.RunAsync(new HttpConnector(new HttpConnectorOptions { TokenCache = new OAuth2TokenCache() }), "http_request", "{\"path\":\"/items\"}", ctx, token))).ConfigureAwait(false);
                OAuth2TokenCache shared = new OAuth2TokenCache();
                HttpConnector sharing = new HttpConnector(new HttpConnectorOptions { TokenCache = shared });
                int before = stub.TokensIssued;
                await Task.WhenAll(Enumerable.Range(0, 8).Select(i => HttpTestHelper.RunAsync(sharing, "http_request", "{\"path\":\"/items\"}", ctx, token))).ConfigureAwait(false);
                HttpTestHelper.Assert(stub.TokensIssued == before + 1, "concurrent calls on one cache fetch one token, got " + (stub.TokensIssued - before));

                Dictionary<string, Dictionary<string, string>> pw = HttpTestHelper.AllSecrets(null);
                pw["clientCredentials"] = new Dictionary<string, string> { { "username", stub.ClientId }, { "password", stub.ClientSecret } };
                ConnectorResourceContext pwResource = HttpTestHelper.Resource(HttpTestHelper.Settings(stub.BaseAddress + "/v1", HttpSettingKeys.AuthType, "OAuth2ClientCredentials", HttpSettingKeys.TokenUrl, stub.BaseAddress + stub.TokenPath), pw);
                ToolResult viaPassword = await HttpTestHelper.RunAsync(Isolated(), "http_request", "{\"path\":\"/items\"}", HttpTestHelper.Context(pwResource), token).ConfigureAwait(false);
                HttpTestHelper.Assert(!viaPassword.IsError && viaPassword.Metrics["httpStatus"] == 200, "Password-kind client credentials work");
            }
        }

        private static async Task AuthOAuth2Refresh(CancellationToken token)
        {
            using (HttpStubServer stub = new HttpStubServer())
            {
                stub.Handler = r => stub.HasValidToken(r) ? HttpStubResponse.Text(200, "data") : HttpStubResponse.Text(401, "bad token");
                HttpConnector c = Isolated();
                ConnectorInvocationContext ctx = Ctx(stub, HttpSettingKeys.AuthType, "OAuth2ClientCredentials", HttpSettingKeys.TokenUrl, stub.BaseAddress + stub.TokenPath);
                ToolResult first = await HttpTestHelper.RunAsync(c, "http_request", "{\"path\":\"/items\"}", ctx, token).ConfigureAwait(false);
                HttpTestHelper.Assert(first.Metrics["httpStatus"] == 200 && HttpTestHelper.Structured(first, "authRetried") == "false", "first call");
                stub.RevokeTokens();
                ToolResult retried = await HttpTestHelper.RunAsync(c, "http_request", "{\"path\":\"/items\"}", ctx, token).ConfigureAwait(false);
                HttpTestHelper.Assert(!retried.IsError && retried.Metrics["httpStatus"] == 200, "retried call succeeds: " + retried.Text);
                HttpTestHelper.Assert(HttpTestHelper.Structured(retried, "authRetried") == "true" && retried.Text.Contains("refreshed", StringComparison.Ordinal), "retry reported");
                HttpTestHelper.Assert(stub.TokensIssued == 2 && stub.To("/v1/items").Count == 3, "one refresh, three API requests");

                stub.Handler = r => HttpStubResponse.Text(401, "always");
                int before = stub.TokensIssued;
                ToolResult stillDenied = await HttpTestHelper.RunAsync(c, "http_request", "{\"path\":\"/items\"}", ctx, token).ConfigureAwait(false);
                HttpTestHelper.Assert(!stillDenied.IsError && stillDenied.Metrics["httpStatus"] == 401 && stub.TokensIssued == before + 1, "retried only once");

                stub.Handler = r => stub.HasValidToken(r) ? HttpStubResponse.Text(200, "data") : HttpStubResponse.Text(401, "bad token");
                stub.TokenLifetimeSeconds = 30;
                HttpConnector shortLived = Isolated();
                int start = stub.TokensIssued;
                await HttpTestHelper.RunAsync(shortLived, "http_request", "{\"path\":\"/items\"}", ctx, token).ConfigureAwait(false);
                await HttpTestHelper.RunAsync(shortLived, "http_request", "{\"path\":\"/items\"}", ctx, token).ConfigureAwait(false);
                HttpTestHelper.Assert(stub.TokensIssued == start + 2, "tokens inside the 60 second refresh window are refreshed, got " + (stub.TokensIssued - start));
            }
        }

        private static async Task AuthOAuth2Failure(CancellationToken token)
        {
            using (HttpStubServer stub = new HttpStubServer())
            {
                Dictionary<string, Dictionary<string, string>> secrets = HttpTestHelper.AllSecrets(stub.ClientId, "wrong-client-secret-555");
                ConnectorResourceContext resource = HttpTestHelper.Resource(HttpTestHelper.Settings(stub.BaseAddress + "/v1", HttpSettingKeys.AuthType, "OAuth2ClientCredentials", HttpSettingKeys.TokenUrl, stub.BaseAddress + stub.TokenPath), secrets);
                ToolResult r = await HttpTestHelper.RunAsync(Isolated(), "http_request", "{\"path\":\"/items\"}", HttpTestHelper.Context(resource), token).ConfigureAwait(false);
                HttpTestHelper.AssertError(r, "auth_failed", "token refused");
                HttpTestHelper.Assert(r.Text.Contains("invalid_client", StringComparison.Ordinal), "endpoint error code included: " + r.Text);
                HttpTestHelper.AssertNoSecrets(r, new[] { "wrong-client-secret-555" }, "oauth failure");
                HttpTestHelper.Assert(stub.To("/v1/items").Count == 0, "API not called without a token");
                ConnectorHealthResult health = await Isolated().TestConnectionAsync(resource, token).ConfigureAwait(false);
                HttpTestHelper.Assert(!health.Success && health.ErrorCode == "auth_failed" && !health.Message.Contains("wrong-client-secret-555", StringComparison.Ordinal), "test connection auth_failed");

                ConnectorResourceContext unreachable = HttpTestHelper.Resource(HttpTestHelper.Settings(stub.BaseAddress + "/v1", HttpSettingKeys.AuthType, "OAuth2ClientCredentials", HttpSettingKeys.TokenUrl, "http://127.0.0.1:" + HttpTestHelper.ClosedPort() + "/token"), HttpTestHelper.AllSecrets(stub));
                ToolResult down = await HttpTestHelper.RunAsync(Isolated(), "http_request", "{\"path\":\"/items\"}", HttpTestHelper.Context(unreachable), token).ConfigureAwait(false);
                HttpTestHelper.AssertError(down, "connection_failed", "token endpoint down");
            }
        }

        private static async Task AuthSecretMissing(CancellationToken token)
        {
            using (HttpStubServer stub = new HttpStubServer())
            {
                foreach (string auth in new[] { "Basic", "Bearer", "ApiKeyHeader", "OAuth2ClientCredentials" })
                {
                    ConnectorResourceContext resource = HttpTestHelper.Resource(HttpTestHelper.Settings(stub.BaseAddress, HttpSettingKeys.AuthType, auth, HttpSettingKeys.TokenUrl, stub.BaseAddress + stub.TokenPath));
                    ToolResult r = await HttpTestHelper.RunAsync(Isolated(), "http_request", "{}", HttpTestHelper.Context(resource), token).ConfigureAwait(false);
                    HttpTestHelper.AssertError(r, "secret_missing", auth);
                }
                ToolResult noBase = await HttpTestHelper.RunAsync(Isolated(), "http_request", "{}", HttpTestHelper.Context(HttpTestHelper.Resource(new Dictionary<string, string>())), token).ConfigureAwait(false);
                HttpTestHelper.AssertError(noBase, "invalid_setting", "missing base URL");
                HttpTestHelper.Assert(stub.Requests.IsEmpty, "nothing sent");
            }
        }

        private static async Task RedirectsFollow(CancellationToken token)
        {
            using (HttpStubServer stub = new HttpStubServer())
            {
                stub.Handler = r =>
                {
                    switch (r.Path)
                    {
                        case "/v1/old": return HttpStubResponse.Redirect(302, "/v1/new");
                        case "/v1/submit": return HttpStubResponse.Redirect(303, "/v1/result");
                        case "/v1/temp": return HttpStubResponse.Redirect(307, stub.BaseAddress + "/v1/kept");
                        default: return HttpStubResponse.Text(200, "landed " + r.Method + " " + r.Path + " body=" + r.Body);
                    }
                };
                HttpConnector c = Isolated();
                ToolResult r1 = await HttpTestHelper.RunAsync(c, "http_request", "{\"path\":\"/old\"}", Ctx(stub, HttpSettingKeys.AuthType, "Bearer"), token).ConfigureAwait(false);
                HttpTestHelper.AssertOk(r1, "302");
                HttpTestHelper.Assert(r1.Text.Contains("landed GET /v1/new", StringComparison.Ordinal) && r1.Metrics["redirects"] == 1, "followed: " + r1.Text);
                HttpTestHelper.Assert(r1.Text.Contains("302 " + stub.BaseAddress + "/v1/old -> " + stub.BaseAddress + "/v1/new (followed)", StringComparison.Ordinal), "chain rendered: " + r1.Text);
                HttpTestHelper.Assert(stub.To("/v1/new").Single().Header("Authorization") == "Bearer " + HttpTestHelper.BearerToken, "auth kept on same origin");

                ToolResult r2 = await HttpTestHelper.RunAsync(c, "http_request", "{\"method\":\"POST\",\"path\":\"/submit\",\"body\":{\"a\":1}}", Ctx(stub), token).ConfigureAwait(false);
                HttpStubRequest result = stub.To("/v1/result").Single();
                HttpTestHelper.Assert(result.Method == "GET" && result.Body.Length == 0 && HttpTestHelper.Structured(r2, "method") == "GET", "303 becomes GET without body");

                await HttpTestHelper.RunAsync(c, "http_request", "{\"method\":\"POST\",\"path\":\"/temp\",\"body\":{\"b\":2}}", Ctx(stub), token).ConfigureAwait(false);
                HttpStubRequest kept = stub.To("/v1/kept").Single();
                HttpTestHelper.Assert(kept.Method == "POST" && kept.Body == "{\"b\":2}", "307 keeps method and body");
            }
        }

        private static async Task RedirectsPolicy(CancellationToken token)
        {
            using (HttpStubServer stub = new HttpStubServer())
            {
                stub.Handler = r =>
                {
                    switch (r.Path)
                    {
                        case "/v1/data/offhost": return HttpStubResponse.Redirect(302, "http://evil.example/v1/data/x");
                        case "/v1/data/outside": return HttpStubResponse.Redirect(302, "/v1/admin");
                        case "/v1/data/outbase": return HttpStubResponse.Redirect(302, "/other/data");
                        case "/v1/data/scheme": return HttpStubResponse.Redirect(302, "ftp://127.0.0.1/v1/data");
                        case "/v1/data/escape": return HttpStubResponse.Redirect(302, "/v1/data/%2e%2e/admin");
                        case "/v1/data/loop": return HttpStubResponse.Redirect(302, "/v1/data/loop");
                        case "/v1/data/hop": return HttpStubResponse.Redirect(301, "/v1/data/final");
                        default: return HttpStubResponse.Text(200, "landed " + r.Path);
                    }
                };
                HttpConnector c = Isolated();
                ConnectorInvocationContext ctx = Ctx(stub, "/v1", 30, null, HttpSettingKeys.AllowedPathPrefixes, "/data", HttpSettingKeys.MaxRedirects, "2");
                await NotFollowed(c, ctx, "/data/offhost", "differs from the resource host", token).ConfigureAwait(false);
                await NotFollowed(c, ctx, "/data/outside", "outside the allowed path prefixes", token).ConfigureAwait(false);
                await NotFollowed(c, ctx, "/data/outbase", "outside the base URL path", token).ConfigureAwait(false);
                await NotFollowed(c, ctx, "/data/scheme", "not http or https", token).ConfigureAwait(false);
                await NotFollowed(c, ctx, "/data/escape", "not followed", token).ConfigureAwait(false);
                HttpTestHelper.Assert(stub.To("/v1/admin").Count == 0 && stub.To("/other/data").Count == 0, "refused targets never requested");

                ToolResult loop = await HttpTestHelper.RunAsync(c, "http_request", "{\"path\":\"/data/loop\"}", ctx, token).ConfigureAwait(false);
                HttpTestHelper.Assert(!loop.IsError && loop.Metrics["httpStatus"] == 302 && loop.Metrics["redirects"] == 2 && loop.Text.Contains("maxRedirects (2)", StringComparison.Ordinal), "maxRedirects stops loops: " + loop.Text);
                HttpTestHelper.Assert(stub.To("/v1/data/loop").Count == 3, "three requests for two follows");

                ConnectorInvocationContext off = Ctx(stub, "/v1", 30, null, HttpSettingKeys.FollowRedirects, "false");
                ToolResult disabled = await HttpTestHelper.RunAsync(c, "http_request", "{\"path\":\"/data/hop\"}", off, token).ConfigureAwait(false);
                HttpTestHelper.Assert(disabled.Metrics["httpStatus"] == 301 && disabled.Text.Contains("redirects are disabled", StringComparison.Ordinal), "followRedirects false");
                HttpTestHelper.Assert(stub.To("/v1/data/final").Count == 0, "final not requested");
            }
        }

        private static async Task NotFollowed(HttpConnector c, ConnectorInvocationContext ctx, string path, string reason, CancellationToken token)
        {
            ToolResult r = await HttpTestHelper.RunAsync(c, "http_request", "{\"path\":\"" + path + "\"}", ctx, token).ConfigureAwait(false);
            HttpTestHelper.Assert(!r.IsError && r.Metrics["httpStatus"] == 302 && r.Metrics["redirects"] == 0, path + " not followed: " + r.Text);
            HttpTestHelper.Assert(r.Text.Contains("not followed", StringComparison.Ordinal) && r.Text.Contains(reason, StringComparison.Ordinal), path + " reason '" + reason + "' in: " + r.Text);
        }

        private static async Task RedirectsDropAuth(CancellationToken token)
        {
            using (HttpStubServer a = new HttpStubServer())
            using (HttpStubServer b = new HttpStubServer())
            {
                a.Handler = r => HttpStubResponse.Redirect(302, b.BaseAddress + "/v1/landing");
                b.Handler = r => HttpStubResponse.Text(200, "landed on b");
                foreach (string auth in new[] { "Bearer", "ApiKeyHeader", "Basic" })
                {
                    ToolResult r = await HttpTestHelper.RunAsync(Isolated(), "http_request", "{\"path\":\"/go\"}", Ctx(a, HttpSettingKeys.AuthType, auth), token).ConfigureAwait(false);
                    HttpTestHelper.AssertOk(r, auth);
                    HttpTestHelper.Assert(r.Text.Contains("landed on b", StringComparison.Ordinal) && r.Text.Contains("authentication is not sent to a different origin", StringComparison.Ordinal), "followed with note: " + r.Text);
                    HttpStubRequest landed = b.Requests.Last();
                    HttpTestHelper.Assert(landed.Header("Authorization") == null && landed.Header("X-API-Key") == null, auth + " credentials dropped off-origin");
                    HttpTestHelper.Assert(a.Requests.Last().Header("Authorization") != null || a.Requests.Last().Header("X-API-Key") != null, auth + " sent to the base origin");
                }
            }
        }

        private static async Task ResponseCaps(CancellationToken token)
        {
            using (HttpStubServer stub = new HttpStubServer())
            {
                StringBuilder big = new StringBuilder();
                for (int i = 0; i < 400; i++) big.Append("line ").Append(i.ToString("D4", CultureInfo.InvariantCulture)).Append(" xxxxxxxxxxxxxxxx\n");
                stub.Handler = r => HttpStubResponse.Text(200, big.ToString());
                HttpConnector c = Isolated();
                ToolResult capped = await HttpTestHelper.RunAsync(c, "http_request", "{\"path\":\"/big\"}", Ctx(stub, HttpSettingKeys.MaxResponseBytes, "2048"), token).ConfigureAwait(false);
                HttpTestHelper.AssertOk(capped, "capped");
                HttpTestHelper.Assert(capped.Truncated && HttpTestHelper.Structured(capped, "bodyTruncated") == "true" && HttpTestHelper.Structured(capped, "bodyBytes") == "2048", "body cut at 2048");
                HttpTestHelper.Assert(capped.OriginalBytes >= big.Length && capped.Text.Contains("cut at the resource's maxResponseBytes", StringComparison.Ordinal), "original size reported");
                HttpTestHelper.Assert(!capped.Text.Contains("line 0399", StringComparison.Ordinal), "tail not shown");

                ToolResult lines = await HttpTestHelper.RunAsync(c, "http_request", "{\"path\":\"/big\"}", Ctx(stub, "/v1", 30, new OutputLimits { MaxLines = 10 }), token).ConfigureAwait(false);
                HttpTestHelper.Assert(lines.Truncated && lines.Text.Split('\n').Length <= 12 && lines.Text.Contains("output truncated", StringComparison.Ordinal), "output limits applied");
                HttpTestHelper.Assert(HttpTestHelper.Structured(lines, "bodyTruncated") == "false", "body itself complete");

                ToolResult bytes = await HttpTestHelper.RunAsync(c, "http_request", "{\"path\":\"/big\"}", Ctx(stub, "/v1", 30, new OutputLimits { MaxBytes = 2000 }), token).ConfigureAwait(false);
                HttpTestHelper.Assert(bytes.Truncated && Encoding.UTF8.GetByteCount(bytes.Text) < 2200, "byte limit applied");
            }
        }

        private static async Task BinaryBody(CancellationToken token)
        {
            using (HttpStubServer stub = new HttpStubServer())
            {
                byte[] data = Enumerable.Range(0, 300).Select(i => (byte)(i % 256)).ToArray();
                stub.Handler = r =>
                {
                    HttpStubResponse resp = new HttpStubResponse { StatusCode = 200, Body = data };
                    return resp.With("Content-Type", "application/octet-stream");
                };
                ToolResult r = await HttpTestHelper.RunAsync(Isolated(), "http_request", "{\"path\":\"/blob\"}", Ctx(stub), token).ConfigureAwait(false);
                HttpTestHelper.AssertOk(r, "binary");
                HttpTestHelper.Assert(r.Text.Contains("Body: binary, 300 bytes, application/octet-stream", StringComparison.Ordinal), "summarized: " + r.Text);
                HttpTestHelper.Assert(r.Text.Contains(Convert.ToBase64String(data, 0, 64), StringComparison.Ordinal) && HttpTestHelper.Structured(r, "bodyIsText") == "false", "preview");
            }
        }

        private static async Task TimeoutCase(CancellationToken token)
        {
            using (HttpStubServer stub = new HttpStubServer())
            {
                stub.DelayMilliseconds = 4000;
                Stopwatch sw = Stopwatch.StartNew();
                ToolResult r = await HttpTestHelper.RunAsync(Isolated(), "http_request", "{\"path\":\"/slow\"}", Ctx(stub, "/v1", 1), token).ConfigureAwait(false);
                HttpTestHelper.AssertError(r, "timeout", "call timeout");
                HttpTestHelper.Assert(sw.Elapsed < TimeSpan.FromSeconds(3), "stopped near 1 second: " + sw.Elapsed);

                sw.Restart();
                ToolResult resourceTimeout = await HttpTestHelper.RunAsync(Isolated(), "http_health_check", "{}", Ctx(stub, "/v1", 30, null, HttpSettingKeys.TimeoutSeconds, "1"), token).ConfigureAwait(false);
                HttpTestHelper.AssertError(resourceTimeout, "timeout", "resource timeout");
                HttpTestHelper.Assert(sw.Elapsed < TimeSpan.FromSeconds(3) && resourceTimeout.Metrics.ContainsKey("latencyMs"), "resource timeout honored");
            }
        }

        private static async Task Cancellation(CancellationToken token)
        {
            using (HttpStubServer stub = new HttpStubServer())
            {
                stub.DelayMilliseconds = 4000;
                foreach (string tool in new[] { "http_request", "http_health_check" })
                {
                    using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        cts.CancelAfter(200);
                        bool cancelled = false;
                        Stopwatch sw = Stopwatch.StartNew();
                        try
                        {
                            await HttpTestHelper.RunAsync(Isolated(), tool, "{\"path\":\"/slow\"}", Ctx(stub), cts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            cancelled = true;
                        }
                        HttpTestHelper.Assert(cancelled && sw.Elapsed < TimeSpan.FromSeconds(3), tool + " cancelled promptly");
                    }
                }

                using (CancellationTokenSource pre = new CancellationTokenSource())
                {
                    pre.Cancel();
                    bool thrown = false;
                    try
                    {
                        await HttpTestHelper.RunAsync(Isolated(), "http_request", "{}", Ctx(stub), pre.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        thrown = true;
                    }
                    HttpTestHelper.Assert(thrown, "pre-cancelled token throws");
                }
            }
        }

        private static async Task ConnectionRefused(CancellationToken token)
        {
            string baseUrl = "http://127.0.0.1:" + HttpTestHelper.ClosedPort();
            ConnectorInvocationContext ctx = HttpTestHelper.Context(HttpTestHelper.Resource(HttpTestHelper.Settings(baseUrl)));
            ToolResult r = await HttpTestHelper.RunAsync(Isolated(), "http_request", "{}", ctx, token).ConfigureAwait(false);
            HttpTestHelper.AssertError(r, "connection_failed", "refused");
            ToolResult h = await HttpTestHelper.RunAsync(Isolated(), "http_health_check", "{}", ctx, token).ConfigureAwait(false);
            HttpTestHelper.Assert(!h.IsError && HttpTestHelper.Structured(h, "healthy") == "false" && HttpTestHelper.Structured(h, "errorCode") == "connection_failed", "health unhealthy: " + h.Text);
            HttpTestHelper.Assert(h.Text.StartsWith("Healthy: no", StringComparison.Ordinal), "text");
        }

        private static async Task TestConnection(CancellationToken token)
        {
            using (HttpStubServer stub = new HttpStubServer())
            {
                int status = 200;
                stub.Handler = r => HttpStubResponse.Text(status, "x");
                ConnectorResourceContext resource = HttpTestHelper.Resource(HttpTestHelper.Settings(stub.BaseAddress + "/v1", HttpSettingKeys.AllowedPathPrefixes, "/status"));
                ConnectorHealthResult ok = await Isolated().TestConnectionAsync(resource, token).ConfigureAwait(false);
                HttpTestHelper.Assert(ok.Success && ok.Details["status"] == "200" && ok.LatencyMs > 0, "ok: " + ok.Message);
                HttpTestHelper.Assert(stub.Requests.Last().Path == "/v1/status", "probes the first allowed prefix: " + stub.Requests.Last().Path);
                status = 404;
                ConnectorHealthResult reachable = await Isolated().TestConnectionAsync(resource, token).ConfigureAwait(false);
                HttpTestHelper.Assert(reachable.Success && reachable.Details["reachableOnly"] == "true" && reachable.Details["healthChecked"] == "false", "404 without healthPath means reachable only");
                HttpTestHelper.Assert(reachable.Message.Contains("HTTP 404", StringComparison.Ordinal) && reachable.Message.Contains("healthPath", StringComparison.Ordinal) && !reachable.Message.StartsWith("Reached", StringComparison.Ordinal), "the 404 is flagged: " + reachable.Message);
                ConnectorResourceContext withHealth = HttpTestHelper.Resource(HttpTestHelper.Settings(stub.BaseAddress + "/v1", HttpSettingKeys.AllowedPathPrefixes, "/status", HttpSettingKeys.HealthPath, "/status/health"));
                ConnectorHealthResult unhealthy = await Isolated().TestConnectionAsync(withHealth, token).ConfigureAwait(false);
                HttpTestHelper.Assert(!unhealthy.Success && unhealthy.ErrorCode == "upstream_error" && stub.Requests.Last().Path == "/v1/status/health", "404 on healthPath fails: " + unhealthy.Message);
                status = 200;
                ConnectorHealthResult healthy = await Isolated().TestConnectionAsync(withHealth, token).ConfigureAwait(false);
                HttpTestHelper.Assert(healthy.Success && healthy.Details["healthChecked"] == "true" && healthy.Message.StartsWith("Healthy", StringComparison.Ordinal), "2xx on healthPath passes: " + healthy.Message);
                ConnectorValidationResult outside = await Isolated().ValidateAsync(HttpTestHelper.Resource(HttpTestHelper.Settings(stub.BaseAddress + "/v1", HttpSettingKeys.AllowedPathPrefixes, "/status", HttpSettingKeys.HealthPath, "/admin")), token).ConfigureAwait(false);
                HttpTestHelper.Assert(outside.Errors.Any(e => e.Field == HttpSettingKeys.HealthPath), "healthPath outside the allowed prefixes is invalid");
                status = 403;
                HttpTestHelper.Assert((await Isolated().TestConnectionAsync(resource, token).ConfigureAwait(false)).ErrorCode == "auth_failed", "403 auth_failed");
                status = 503;
                HttpTestHelper.Assert((await Isolated().TestConnectionAsync(resource, token).ConfigureAwait(false)).ErrorCode == "upstream_error", "5xx upstream_error");

                stub.DelayMilliseconds = 4000;
                HttpConnector quick = new HttpConnector(new HttpConnectorOptions { TestConnectionTimeout = TimeSpan.FromSeconds(1), TokenCache = new OAuth2TokenCache() });
                ConnectorHealthResult slow = await quick.TestConnectionAsync(resource, token).ConfigureAwait(false);
                HttpTestHelper.Assert(!slow.Success && slow.ErrorCode == "timeout", "timeout: " + slow.ErrorCode);
            }
            ConnectorResourceContext closed = HttpTestHelper.Resource(HttpTestHelper.Settings("http://127.0.0.1:" + HttpTestHelper.ClosedPort()));
            ConnectorHealthResult refused = await Isolated().TestConnectionAsync(closed, token).ConfigureAwait(false);
            HttpTestHelper.Assert(!refused.Success && refused.ErrorCode == "connection_failed", "refused: " + refused.ErrorCode);
            ConnectorHealthResult invalid = await Isolated().TestConnectionAsync(HttpTestHelper.Resource(new Dictionary<string, string>()), token).ConfigureAwait(false);
            HttpTestHelper.Assert(!invalid.Success && invalid.ErrorCode == "invalid_setting", "missing base URL: " + invalid.ErrorCode);
        }

        private static async Task HealthHttp(CancellationToken token)
        {
            using (HttpStubServer stub = new HttpStubServer())
            {
                stub.Handler = r =>
                {
                    if (r.Path == "/v1/h") return HttpStubResponse.Redirect(301, "/v1/health");
                    if (r.Path == "/v1/down") return HttpStubResponse.Text(503, "maintenance");
                    return HttpStubResponse.Json(200, "{\"status\":\"ok\"}");
                };
                HttpConnector c = Isolated();
                ConnectorInvocationContext ctx = Ctx(stub, HttpSettingKeys.AuthType, "Bearer");
                ToolResult ok = await HttpTestHelper.RunAsync(c, "http_health_check", "{\"path\":\"/health\",\"bodyContains\":\"\\\"ok\\\"\"}", ctx, token).ConfigureAwait(false);
                HttpTestHelper.AssertOk(ok, "health");
                HttpTestHelper.Assert(HttpTestHelper.Structured(ok, "healthy") == "true" && HttpTestHelper.Structured(ok, "bodyMatched") == "true" && ok.Metrics["httpStatus"] == 200, "healthy: " + ok.Text);
                HttpTestHelper.Assert(Double.Parse(HttpTestHelper.Structured(ok, "latencyMs")!, CultureInfo.InvariantCulture) > 0 && ok.Text.Contains("Latency:", StringComparison.Ordinal), "latency");
                HttpTestHelper.Assert(HttpTestHelper.Structured(ok, "tls") == null && !ok.Text.Contains("TLS certificate", StringComparison.Ordinal), "no TLS over http");
                HttpTestHelper.Assert(stub.Requests.Last().Header("Authorization") == "Bearer " + HttpTestHelper.BearerToken, "auth applied to health checks");

                ToolResult miss = await HttpTestHelper.RunAsync(c, "http_health_check", "{\"path\":\"/health\",\"bodyContains\":\"degraded\"}", ctx, token).ConfigureAwait(false);
                HttpTestHelper.Assert(HttpTestHelper.Structured(miss, "healthy") == "false" && HttpTestHelper.Structured(miss, "bodyMatched") == "false", "body mismatch unhealthy");
                ToolResult expect = await HttpTestHelper.RunAsync(c, "http_health_check", "{\"path\":\"/health\",\"expectStatus\":[204]}", ctx, token).ConfigureAwait(false);
                HttpTestHelper.Assert(HttpTestHelper.Structured(expect, "healthy") == "false" && expect.Text.Contains("expected 204, NOT ok", StringComparison.Ordinal), "expected status: " + expect.Text);
                ToolResult down = await HttpTestHelper.RunAsync(c, "http_health_check", "{\"path\":\"/down\"}", ctx, token).ConfigureAwait(false);
                HttpTestHelper.Assert(!down.IsError && HttpTestHelper.Structured(down, "healthy") == "false" && down.Metrics["httpStatus"] == 503 && down.Metrics["healthy"] == 0, "503 unhealthy");
                ToolResult head = await HttpTestHelper.RunAsync(c, "http_health_check", "{\"path\":\"/health\",\"method\":\"HEAD\"}", ctx, token).ConfigureAwait(false);
                HttpTestHelper.Assert(HttpTestHelper.Structured(head, "healthy") == "true" && stub.Requests.Last().Method == "HEAD", "HEAD");
                HttpTestHelper.AssertError(await HttpTestHelper.RunAsync(c, "http_health_check", "{\"method\":\"HEAD\",\"bodyContains\":\"x\"}", ctx, token).ConfigureAwait(false), "invalid_argument", "HEAD with bodyContains");
                HttpTestHelper.AssertError(await HttpTestHelper.RunAsync(c, "http_health_check", "{\"method\":\"POST\"}", ctx, token).ConfigureAwait(false), "invalid_argument", "POST health");
                HttpTestHelper.AssertError(await HttpTestHelper.RunAsync(c, "http_health_check", "{\"expectStatus\":[42]}", ctx, token).ConfigureAwait(false), "invalid_argument", "bad expectStatus");

                ToolResult chain = await HttpTestHelper.RunAsync(c, "http_health_check", "{\"path\":\"/h\"}", ctx, token).ConfigureAwait(false);
                HttpTestHelper.Assert(HttpTestHelper.Structured(chain, "healthy") == "true" && HttpTestHelper.Structured(chain, "finalUrl") == stub.BaseAddress + "/v1/health", "redirect followed");
                HttpTestHelper.Assert(chain.Text.Contains("301 " + stub.BaseAddress + "/v1/h -> " + stub.BaseAddress + "/v1/health (followed)", StringComparison.Ordinal), "chain: " + chain.Text);
                ToolResult noFollow = await HttpTestHelper.RunAsync(c, "http_health_check", "{\"path\":\"/h\",\"followRedirects\":false}", ctx, token).ConfigureAwait(false);
                HttpTestHelper.Assert(HttpTestHelper.Structured(noFollow, "status") == "301" && HttpTestHelper.Structured(noFollow, "healthy") == "true", "3xx healthy by default when not followed");
            }
        }

        private static async Task HealthHttpsUnverified(CancellationToken token)
        {
            using (HttpStubServer stub = new HttpStubServer(true, 30))
            {
                stub.Handler = r => HttpStubResponse.Text(200, "ok");
                ConnectorInvocationContext ctx = Ctx(stub, HttpSettingKeys.VerifyTls, "false");
                ToolResult h = await HttpTestHelper.RunAsync(Isolated(), "http_health_check", "{\"path\":\"/health\"}", ctx, token).ConfigureAwait(false);
                HttpTestHelper.AssertOk(h, "https health");
                HttpTestHelper.Assert(HttpTestHelper.Structured(h, "healthy") == "true", "healthy: " + h.Text);
                string? tls = HttpTestHelper.Structured(h, "tls");
                HttpTestHelper.Assert(tls != null && tls.Contains("hugo-http-stub", StringComparison.Ordinal) && tls.Contains("\"valid\":false", StringComparison.Ordinal) && tls.Contains("RemoteCertificateChainErrors", StringComparison.Ordinal), "tls: " + tls);
                double days = h.Metrics["tlsDaysRemaining"];
                HttpTestHelper.Assert(days >= 29 && days <= 30, "days remaining " + days);
                HttpTestHelper.Assert(h.Text.Contains("accepted only because verifyTls is false", StringComparison.Ordinal) && h.Text.Contains("days remaining", StringComparison.Ordinal), "text: " + h.Text);

                ToolResult r = await HttpTestHelper.RunAsync(Isolated(), "http_request", "{\"path\":\"/x\"}", ctx, token).ConfigureAwait(false);
                HttpTestHelper.Assert(!r.IsError && r.Metrics["httpStatus"] == 200, "request accepted with verifyTls false");
            }
            using (HttpStubServer soon = new HttpStubServer(true, 5))
            {
                ToolResult h = await HttpTestHelper.RunAsync(Isolated(), "http_health_check", "{}", Ctx(soon, HttpSettingKeys.VerifyTls, "false"), token).ConfigureAwait(false);
                double days = h.Metrics["tlsDaysRemaining"];
                HttpTestHelper.Assert(days >= 4 && days <= 5, "short expiry " + days);
            }
        }

        private static async Task HealthHttpsVerified(CancellationToken token)
        {
            using (HttpStubServer stub = new HttpStubServer(true, 30))
            {
                ConnectorInvocationContext ctx = Ctx(stub);
                ToolResult h = await HttpTestHelper.RunAsync(Isolated(), "http_health_check", "{\"path\":\"/health\"}", ctx, token).ConfigureAwait(false);
                HttpTestHelper.Assert(!h.IsError && HttpTestHelper.Structured(h, "healthy") == "false" && HttpTestHelper.Structured(h, "errorCode") == "connection_failed", "unhealthy: " + h.Text);
                HttpTestHelper.Assert(h.Text.Contains("TLS", StringComparison.Ordinal) && h.Text.Contains("Validation: FAILED", StringComparison.Ordinal), "certificate still reported: " + h.Text);
                HttpTestHelper.Assert(stub.Requests.IsEmpty, "no request after a failed handshake");
                ToolResult r = await HttpTestHelper.RunAsync(Isolated(), "http_request", "{\"path\":\"/x\"}", ctx, token).ConfigureAwait(false);
                HttpTestHelper.AssertError(r, "connection_failed", "request with verifyTls true");
                HttpTestHelper.Assert(r.Text.Contains("TLS handshake", StringComparison.Ordinal), "TLS message: " + r.Text);
            }
        }

        private static async Task HttpsDowngrade(CancellationToken token)
        {
            using (HttpStubServer tls = new HttpStubServer(true, 30))
            using (HttpStubServer plain = new HttpStubServer())
            {
                tls.Handler = r => HttpStubResponse.Redirect(302, plain.BaseAddress + "/v1/x");
                ToolResult r = await HttpTestHelper.RunAsync(Isolated(), "http_request", "{\"path\":\"/down\"}", Ctx(tls, HttpSettingKeys.VerifyTls, "false"), token).ConfigureAwait(false);
                HttpTestHelper.Assert(!r.IsError && r.Metrics["httpStatus"] == 302 && r.Text.Contains("downgrade", StringComparison.Ordinal), "downgrade refused: " + r.Text);
                HttpTestHelper.Assert(plain.Requests.IsEmpty, "plain server never contacted");
            }
        }

        private static async Task SecretHygiene(CancellationToken token)
        {
            using (HttpStubServer stub = new HttpStubServer())
            {
                stub.Handler = r => stub.HasValidToken(r) || r.Header("Authorization") != null || r.Header("X-API-Key") != null ? HttpStubResponse.Text(200, "ok") : HttpStubResponse.Text(401, "no");
                List<string> secrets = new List<string> { HttpTestHelper.BearerToken, HttpTestHelper.ApiKey, HttpTestHelper.BasicPassword, stub.ClientSecret };
                foreach (string auth in new[] { "Basic", "Bearer", "ApiKeyHeader", "OAuth2ClientCredentials" })
                {
                    ConnectorInvocationContext ctx = Ctx(stub, HttpSettingKeys.AuthType, auth, HttpSettingKeys.TokenUrl, stub.BaseAddress + stub.TokenPath);
                    ToolResult r = await HttpTestHelper.RunAsync(Isolated(), "http_request", "{\"path\":\"/x\"}", ctx, token).ConfigureAwait(false);
                    HttpTestHelper.AssertOk(r, auth);
                    HttpTestHelper.AssertNoSecrets(r, secrets, auth + " request");
                    ToolResult h = await HttpTestHelper.RunAsync(Isolated(), "http_health_check", "{}", ctx, token).ConfigureAwait(false);
                    HttpTestHelper.AssertNoSecrets(h, secrets, auth + " health");
                    ToolResult refused = await HttpTestHelper.RunAsync(Isolated(), "http_request", "{\"headers\":{\"Authorization\":\"Bearer " + HttpTestHelper.BearerToken + "\"}}", ctx, token).ConfigureAwait(false);
                    HttpTestHelper.AssertError(refused, "invalid_argument", "forged auth");
                    HttpTestHelper.AssertNoSecrets(refused, secrets, auth + " refusal");
                    ConnectorHealthResult test = await Isolated().TestConnectionAsync(ctx.Resource, token).ConfigureAwait(false);
                    HttpTestHelper.Assert(secrets.All(s => !test.Message.Contains(s, StringComparison.Ordinal) && test.Details.Values.All(v => !v.Contains(s, StringComparison.Ordinal))), auth + " test connection");
                }
            }
        }

        private static void Expect(ToolResult result, string code, string what)
        {
            HttpTestHelper.AssertError(result, code, what);
        }

        #endregion
    }
}
