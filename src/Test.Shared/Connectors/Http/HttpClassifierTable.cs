namespace Test.Shared.Connectors.Http
{
    using System.Collections.Generic;
    using Hugo.Connectors.Http;

    /// <summary>
    /// The <c>http_request</c> classifier table: methods, path escapes, allow lists, and effect overrides.
    /// </summary>
    public static class HttpClassifierTable
    {
        #region Public-Methods

        /// <summary>
        /// Build the rows.
        /// </summary>
        /// <returns>Rows.</returns>
        public static List<HttpClassifierCase> Cases()
        {
            List<HttpClassifierCase> rows = new List<HttpClassifierCase>();
            Dictionary<string, string> def = HttpTestHelper.Settings("https://api.example.com/v1");
            Dictionary<string, string> wide = HttpTestHelper.Settings("https://api.example.com", HttpSettingKeys.AllowedMethods, "GET\nPOST\nTRACE\nPROPFIND\nPURGE\nMKCOL\nCONNECT\nLOCK");
            Dictionary<string, string> restricted = HttpTestHelper.Settings("https://api.example.com/v2", HttpSettingKeys.AllowedMethods, "GET\nPOST", HttpSettingKeys.AllowedPathPrefixes, "/api\n/status/");
            Dictionary<string, string> overrides = HttpTestHelper.Settings(
                "https://api.example.com",
                HttpSettingKeys.MethodEffectOverrides,
                "POST /search=Read\n* /admin=Destructive\nGET /admin/health=Read\nDELETE /cache=Write\n* /reports=Read\nPOST /reports=Write");

            Add(rows, "default", def, "GET", "/", "R");
            Add(rows, "default", def, "GET", "/orders", "R");
            Add(rows, "default", def, "get", "/orders", "R");
            Add(rows, "default", def, null, "/orders", "R");
            Add(rows, "default", def, "", null, "R");
            Add(rows, "default", def, "HEAD", "/orders/1", "R");
            Add(rows, "default", def, "OPTIONS", "/orders", "R");
            Add(rows, "default", def, "POST", "/orders", "W");
            Add(rows, "default", def, "PUT", "/orders/1", "W");
            Add(rows, "default", def, "PATCH", "/orders/1", "W");
            Add(rows, "default", def, "DELETE", "/orders/1", "D");
            Add(rows, "default", def, "delete", "/orders/1", "D");
            Add(rows, "default", def, "TRACE", "/", "X");
            Add(rows, "default", def, "CONNECT", "/", "X");
            Add(rows, "default", def, "G(T", "/", "X");
            Add(rows, "default", def, "GET /x", "/", "X");
            Add(rows, "default", def, "GET", "orders", "R");
            Add(rows, "default", def, "GET", "/a/", "R");
            Add(rows, "default", def, "GET", "/v1/projects/p1:run", "R");
            Add(rows, "default", def, "GET", "/caf%C3%A9", "R");
            Add(rows, "default", def, "GET", "/files/a b", "R");
            Add(rows, "default", def, "GET", "/.well-known/openid-configuration", "R");
            Add(rows, "default", def, "GET", "/...", "R");
            Add(rows, "default", def, "GET", "/a/../b", "X");
            Add(rows, "default", def, "GET", "/../etc/passwd", "X");
            Add(rows, "default", def, "GET", "..", "X");
            Add(rows, "default", def, "GET", "/a/./b", "X");
            Add(rows, "default", def, "GET", "/a/..", "X");
            Add(rows, "default", def, "GET", "/%2e%2e/x", "X");
            Add(rows, "default", def, "GET", "/%2E%2E/x", "X");
            Add(rows, "default", def, "GET", "/.%2e/x", "X");
            Add(rows, "default", def, "GET", "/%252e%252e/x", "X");
            Add(rows, "default", def, "GET", "/%25252e%25252e/x", "X");
            Add(rows, "default", def, "GET", "/a%2fb", "X");
            Add(rows, "default", def, "GET", "/a%2Fb", "X");
            Add(rows, "default", def, "GET", "/a%252fb", "X");
            Add(rows, "default", def, "GET", "/a%5cb", "X");
            Add(rows, "default", def, "GET", "http://evil.example/x", "X");
            Add(rows, "default", def, "GET", "https://api.example.com/v1/x", "X");
            Add(rows, "default", def, "GET", "//evil.example/x", "X");
            Add(rows, "default", def, "GET", "https:/x", "X");
            Add(rows, "default", def, "GET", "javascript:alert(1)", "X");
            Add(rows, "default", def, "GET", "/x/http://evil.example", "X");
            Add(rows, "default", def, "GET", "/a\\b", "X");
            Add(rows, "default", def, "GET", "\\\\evil\\share", "X");
            Add(rows, "default", def, "GET", "/a?b=1", "X");
            Add(rows, "default", def, "GET", "/a#frag", "X");
            Add(rows, "default", def, "GET", "/a//b", "X");
            Add(rows, "default", def, "GET", "/a/%00", "X");
            Add(rows, "default", def, "GET", "/a%0d%0aX-Injected: 1", "X");
            Add(rows, "default", def, "GET", "/a%3fb=1", "X");
            Add(rows, "default", def, "GET", "/a%23frag", "X");
            Add(rows, "default", def, "GET", "/a\nb", "X");

            Add(rows, "wide", wide, "TRACE", "/", "D");
            Add(rows, "wide", wide, "PROPFIND", "/dav", "D");
            Add(rows, "wide", wide, "PURGE", "/cache/x", "D");
            Add(rows, "wide", wide, "MKCOL", "/dav/new", "D");
            Add(rows, "wide", wide, "LOCK", "/dav/file", "D");
            Add(rows, "wide", wide, "PUT", "/x", "X");
            Add(rows, "wide", wide, "DELETE", "/x", "X");

            Add(rows, "restricted", restricted, "GET", "/api", "R");
            Add(rows, "restricted", restricted, "GET", "/api/", "R");
            Add(rows, "restricted", restricted, "GET", "/api/orders/1", "R");
            Add(rows, "restricted", restricted, "GET", "/apix", "X");
            Add(rows, "restricted", restricted, "GET", "/api-internal/x", "X");
            Add(rows, "restricted", restricted, "GET", "/status", "R");
            Add(rows, "restricted", restricted, "GET", "/status/db", "R");
            Add(rows, "restricted", restricted, "GET", "/other", "X");
            Add(rows, "restricted", restricted, "GET", "/", "X");
            Add(rows, "restricted", restricted, "GET", "/API/orders", "X");
            Add(rows, "restricted", restricted, "GET", "/api/../admin", "X");
            Add(rows, "restricted", restricted, "GET", "/api/%2e%2e/admin", "X");
            Add(rows, "restricted", restricted, "POST", "/api/orders", "W");
            Add(rows, "restricted", restricted, "PUT", "/api/orders/1", "X");
            Add(rows, "restricted", restricted, "DELETE", "/api/orders/1", "X");
            Add(rows, "restricted", restricted, "HEAD", "/api", "X");

            Add(rows, "overrides", overrides, "POST", "/search", "R");
            Add(rows, "overrides", overrides, "POST", "/search/advanced", "R");
            Add(rows, "overrides", overrides, "POST", "/searchx", "W");
            Add(rows, "overrides", overrides, "PUT", "/search", "W");
            Add(rows, "overrides", overrides, "GET", "/admin", "D");
            Add(rows, "overrides", overrides, "POST", "/admin/users", "D");
            Add(rows, "overrides", overrides, "PATCH", "/admin/users/1", "D");
            Add(rows, "overrides", overrides, "GET", "/admin/health", "R");
            Add(rows, "overrides", overrides, "GET", "/admin/health/deep", "R");
            Add(rows, "overrides", overrides, "GET", "/admin/healthz", "D");
            Add(rows, "overrides", overrides, "POST", "/admin/health", "D");
            Add(rows, "overrides", overrides, "DELETE", "/cache", "W");
            Add(rows, "overrides", overrides, "DELETE", "/cache/key1", "W");
            Add(rows, "overrides", overrides, "DELETE", "/cache2", "D");
            Add(rows, "overrides", overrides, "DELETE", "/other", "D");
            Add(rows, "overrides", overrides, "GET", "/reports/q3", "R");
            Add(rows, "overrides", overrides, "DELETE", "/reports/q3", "R");
            Add(rows, "overrides", overrides, "POST", "/reports", "W");
            Add(rows, "overrides", overrides, "GET", "/admin/../search", "X");
            return rows;
        }

        #endregion

        #region Private-Methods

        private static void Add(List<HttpClassifierCase> rows, string variant, Dictionary<string, string> settings, string? method, string? path, string expected)
        {
            rows.Add(new HttpClassifierCase { Variant = variant, Settings = settings, Method = method, Path = path, Expected = expected });
        }

        #endregion
    }
}
