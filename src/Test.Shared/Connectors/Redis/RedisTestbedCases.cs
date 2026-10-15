namespace Test.Shared.Connectors.Redis
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Test.Shared.Connectors.Ssh;
    using Touchstone.Core;

    /// <summary>
    /// Redis connector cases against <c>docker/testbed/redis/compose.yaml</c>. They run only when <c>HUGO_TESTBED=1</c>;
    /// otherwise every case is skipped with a reason. Keys written by the suite live under a per-run prefix and are
    /// removed by the cases that create them.
    /// </summary>
    public static class RedisTestbedCases
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public static string SuiteId { get; } = "testbed.redis";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            Add(cases, "test-connection", "TestConnection reports version, mode, and database", TestConnection);
            Add(cases, "auth-failed", "A wrong or missing password maps to auth_failed", AuthFailed);
            Add(cases, "acl-user", "An ACL user from the secret connects and NOPERM maps to permission_denied", AclUser);
            Add(cases, "info", "redis_info returns parsed sections", Info);
            Add(cases, "scan-paging", "redis_scan pages through 500 keys with a cursor and no duplicates", ScanPaging);
            Add(cases, "scan-type-and-limit", "redis_scan filters by type and stops at the scan limit with a cursor", ScanTypeAndLimit);
            Add(cases, "get-string", "redis_get reads strings, cuts big values, and shows binary as base64", GetString);
            Add(cases, "get-hash", "redis_get reads hashes by field and pages large hashes", GetHash);
            Add(cases, "get-list", "redis_get reads lists forward, reversed, and by offset", GetList);
            Add(cases, "get-set-zset", "redis_get reads sets and sorted sets with scores", GetSetZset);
            Add(cases, "get-stream", "redis_get reads streams and pages with an exclusive ID", GetStream);
            Add(cases, "get-missing", "redis_get on a missing key is not_found", GetMissing);
            Add(cases, "ttl", "redis_ttl distinguishes TTL, no expiry, and missing", Ttl);
            Add(cases, "memory-usage", "redis_memory_usage measures keys", MemoryUsage);
            Add(cases, "slowlog", "redis_slowlog lists recent commands", Slowlog);
            Add(cases, "client-list", "redis_client_list shows Hugo's connection", ClientList);
            Add(cases, "latency", "redis_latency reports the threshold and history", Latency);
            Add(cases, "bigkeys", "redis_bigkeys finds the big string first", BigKeys);
            Add(cases, "write-lifecycle", "redis_set, redis_expire, and redis_delete work end to end", WriteLifecycle);
            Add(cases, "command", "redis_command runs reads, maps errors, and caps replies", Command);
            Add(cases, "output-limits", "Output limits truncate large reads and mark the result", OutputLimits);
            Add(cases, "timeout", "A slow command ends with a timeout error", Timeout);
            Add(cases, "cancellation", "Caller cancellation during a slow command propagates", Cancellation);
            Add(cases, "pooling", "Calls on one resource reuse one pooled connection", Pooling);
            Add(cases, "secret-scrubbing", "No tool output contains the password", SecretScrubbing);
            return new TestSuiteDescriptor(SuiteId, "Redis connector against the testbed", cases);
        }

        #endregion

        #region Private-Methods

        private static void Add(List<TestCaseDescriptor> cases, string id, string name, Func<CancellationToken, Task> body)
        {
            bool skip = !RedisTestbed.Enabled;
            cases.Add(new TestCaseDescriptor(SuiteId, id, name, body, null, skip, skip ? RedisTestbed.SkipReason : null!));
        }

        private static async Task<ToolResult> Run(string tool, string json, CancellationToken token, ConnectorInvocationContext? ctx = null)
        {
            using (CountingSessionPool pool = new CountingSessionPool())
            {
                ConnectorInvocationContext c = ctx ?? RedisTestHelper.Context(RedisTestHelper.Resource(RedisTestbed.Settings(), RedisTestbed.Password, pool));
                return await RedisTestHelper.RunAsync(tool, json, c, token).ConfigureAwait(false);
            }
        }

        private static async Task TestConnection(CancellationToken token)
        {
            ConnectorHealthResult h = await RedisTestHelper.Connector.TestConnectionAsync(RedisTestHelper.Resource(RedisTestbed.Settings(), RedisTestbed.Password), token).ConfigureAwait(false);
            RedisTestHelper.Assert(h.Success, "health: " + h.ErrorCode + " " + h.Message);
            RedisTestHelper.Assert(h.Details["version"].StartsWith("7.", StringComparison.Ordinal) && h.Details["mode"] == "standalone" && h.Details["database"] == "0", "details");
            ConnectorHealthResult warn = await RedisTestHelper.Connector.TestConnectionAsync(RedisTestHelper.Resource(RedisTestbed.Settings(new Dictionary<string, string> { { "clusterMode", "Cluster" } }), RedisTestbed.Password), token).ConfigureAwait(false);
            RedisTestHelper.Assert(warn.Success && warn.Details.ContainsKey("warning"), "cluster mode mismatch warning");
        }

        private static async Task AuthFailed(CancellationToken token)
        {
            ConnectorHealthResult wrong = await RedisTestHelper.Connector.TestConnectionAsync(RedisTestHelper.Resource(RedisTestbed.Settings(), "wrong-password"), token).ConfigureAwait(false);
            RedisTestHelper.Assert(!wrong.Success && wrong.ErrorCode == ConnectorErrorCodes.AuthFailed, "wrong password: " + wrong.ErrorCode + " " + wrong.Message);
            RedisTestHelper.Assert(!wrong.Message.Contains("wrong-password", StringComparison.Ordinal), "password not echoed");
            ToolResult none = await RedisTestHelper.RunAsync("redis_info", "{}", RedisTestHelper.Context(RedisTestHelper.Resource(RedisTestbed.Settings(), null)), token).ConfigureAwait(false);
            RedisTestHelper.AssertError(none, ConnectorErrorCodes.AuthFailed, "no password");
        }

        private static async Task AclUser(CancellationToken token)
        {
            using (CountingSessionPool pool = new CountingSessionPool())
            {
                ConnectorInvocationContext ctx = RedisTestHelper.Context(RedisTestHelper.Resource(RedisTestbed.Settings(), RedisTestbed.ReaderPassword, pool, RedisTestbed.ReaderUser));
                ToolResult read = await RedisTestHelper.RunAsync("redis_get", "{\"key\":\"hugo:string:greeting\"}", ctx, token).ConfigureAwait(false);
                RedisTestHelper.AssertOk(read, "reader get");
                ToolResult write = await RedisTestHelper.RunAsync("redis_set", "{\"key\":\"hugo:test:acl\",\"value\":\"x\"}", ctx, token).ConfigureAwait(false);
                RedisTestHelper.AssertError(write, ConnectorErrorCodes.PermissionDenied, "reader set");
                RedisTestHelper.Assert(write.Text.Contains("NOPERM", StringComparison.Ordinal), "upstream reason kept");
            }
        }

        private static async Task Info(CancellationToken token)
        {
            ToolResult r = await Run("redis_info", "{\"sections\":[\"memory\",\"keyspace\"]}", token).ConfigureAwait(false);
            RedisTestHelper.AssertOk(r, "info");
            RedisTestHelper.Assert(r.Text.Contains("used_memory:", StringComparison.Ordinal) && r.Text.Contains("db0:keys=", StringComparison.Ordinal), "sections present");
            RedisTestHelper.Assert(!r.Text.Contains("redis_version", StringComparison.Ordinal), "only the requested sections");
            string nodes = RedisTestHelper.Raw(r, "nodes") ?? String.Empty;
            RedisTestHelper.Assert(nodes.Contains("\"memory\"", StringComparison.Ordinal) && nodes.Contains("\"used_memory\"", StringComparison.Ordinal), "structured sections");
            ToolResult all = await Run("redis_info", "{}", token).ConfigureAwait(false);
            RedisTestHelper.Assert(all.Text.Contains("redis_version:7", StringComparison.Ordinal), "default sections");
            ToolResult badNode = await Run("redis_info", "{\"node\":\"10.9.9.9:1\"}", token).ConfigureAwait(false);
            RedisTestHelper.AssertError(badNode, ConnectorErrorCodes.InvalidArgument, "unknown node");
        }

        private static async Task ScanPaging(CancellationToken token)
        {
            using (CountingSessionPool pool = new CountingSessionPool())
            {
                ConnectorInvocationContext ctx = RedisTestHelper.Context(RedisTestHelper.Resource(RedisTestbed.Settings(), RedisTestbed.Password, pool));
                List<string> all = new List<string>();
                string args = "{\"pattern\":\"hugo:many:*\",\"count\":120,\"details\":false}";
                int pages = 0;
                while (pages < 50)
                {
                    ToolResult r = await RedisTestHelper.RunAsync("redis_scan", args, ctx, token).ConfigureAwait(false);
                    RedisTestHelper.AssertOk(r, "page " + pages);
                    all.AddRange(RedisTestHelper.ScanKeys(r));
                    pages++;
                    string? cursor = RedisTestHelper.Str(r, "nextCursor");
                    if (cursor == null) break;
                    args = "{\"pattern\":\"hugo:many:*\",\"count\":120,\"details\":false,\"cursor\":\"" + cursor + "\"}";
                }
                RedisTestHelper.Assert(pages >= 4, "paged, got " + pages + " page(s)");
                RedisTestHelper.Assert(all.Distinct().Count() == 500, "all 500 keys exactly once, got " + all.Distinct().Count() + " distinct of " + all.Count);
                RedisTestHelper.Assert(all.All(k => k.StartsWith("hugo:many:", StringComparison.Ordinal)), "pattern respected");
                RedisTestHelper.Assert(pool.Created == 1, "one connection for all pages");
            }
        }

        private static async Task ScanTypeAndLimit(CancellationToken token)
        {
            ToolResult hashes = await Run("redis_scan", "{\"pattern\":\"hugo:*\",\"type\":\"hash\"}", token).ConfigureAwait(false);
            RedisTestHelper.AssertOk(hashes, "type scan");
            List<string> keys = RedisTestHelper.ScanKeys(hashes);
            RedisTestHelper.Assert(keys.Count == 2 && keys.Contains("hugo:hash:user:1") && keys.Contains("hugo:hash:large"), "two hashes: " + String.Join(",", keys));
            RedisTestHelper.Assert(hashes.Text.Contains("| hash |", StringComparison.Ordinal) && RedisTestHelper.Raw(hashes, "complete") == "true", "typed rows, complete walk");
            RedisTestHelper.Assert(RedisTestHelper.Num(hashes, "databaseSize") >= RedisTestbed.SeededKeys, "database size reported");

            ConnectorInvocationContext small = RedisTestHelper.Context(RedisTestHelper.Resource(RedisTestbed.Settings(new Dictionary<string, string> { { "maxKeysScanned", "100" } }), RedisTestbed.Password));
            ToolResult limited = await RedisTestHelper.RunAsync("redis_scan", "{\"pattern\":\"no-such-prefix:*\"}", small, token).ConfigureAwait(false);
            RedisTestHelper.AssertOk(limited, "limited scan");
            RedisTestHelper.Assert(RedisTestHelper.Raw(limited, "scanLimitReached") == "true" && RedisTestHelper.Str(limited, "nextCursor") != null, "scan limit with a cursor");
            RedisTestHelper.Assert(limited.Text.Contains("scan limit", StringComparison.Ordinal), "explains the limit");

            ToolResult ttl = await Run("redis_scan", "{\"pattern\":\"hugo:ttl:*\"}", token).ConfigureAwait(false);
            RedisTestHelper.Assert(ttl.Text.Contains("hugo:ttl:session", StringComparison.Ordinal) && ttl.Text.Contains("| string |", StringComparison.Ordinal), "details include type");
        }

        private static async Task GetString(CancellationToken token)
        {
            ToolResult g = await Run("redis_get", "{\"key\":\"hugo:string:greeting\"}", token).ConfigureAwait(false);
            RedisTestHelper.AssertOk(g, "greeting");
            RedisTestHelper.Assert(g.Text.Contains("hello from the hugo testbed", StringComparison.Ordinal) && RedisTestHelper.Str(g, "type") == "string", "string value");
            RedisTestHelper.Assert(RedisTestHelper.Num(g, "ttlMs") == -1, "no ttl");

            ToolResult big = await Run("redis_get", "{\"key\":\"hugo:string:big\"}", token).ConfigureAwait(false);
            RedisTestHelper.AssertOk(big, "big");
            RedisTestHelper.Assert(RedisTestHelper.Num(big, "length") == 200000 && RedisTestHelper.Raw(big, "truncated") == "true" && RedisTestHelper.Str(big, "next") == "65536", "cut at maxValueBytes with next offset");
            RedisTestHelper.Assert(big.Truncated, "result marked truncated");
            ToolResult tail = await Run("redis_get", "{\"key\":\"hugo:string:big\",\"offset\":196608}", token).ConfigureAwait(false);
            RedisTestHelper.Assert(RedisTestHelper.Str(tail, "next") == null && RedisTestHelper.Num(tail, "offset") == 196608, "last page");

            ToolResult bin = await Run("redis_get", "{\"key\":\"hugo:string:binary\"}", token).ConfigureAwait(false);
            RedisTestHelper.Assert(bin.Text.Contains("base64", StringComparison.Ordinal) && (RedisTestHelper.Raw(bin, "value") ?? String.Empty).Contains("\"binary\":true", StringComparison.Ordinal), "binary as base64");

            ToolResult small = await RedisTestHelper.RunAsync("redis_get", "{\"key\":\"hugo:string:greeting\"}", RedisTestHelper.Context(RedisTestHelper.Resource(RedisTestbed.Settings(new Dictionary<string, string> { { "maxValueBytes", "64" } }), RedisTestbed.Password)), token).ConfigureAwait(false);
            RedisTestHelper.AssertOk(small, "64 byte cap");
        }

        private static async Task GetHash(CancellationToken token)
        {
            ToolResult user = await Run("redis_get", "{\"key\":\"hugo:hash:user:1\"}", token).ConfigureAwait(false);
            RedisTestHelper.AssertOk(user, "hash");
            RedisTestHelper.Assert(RedisTestHelper.ItemCount(user) == 3 && user.Text.Contains("Thomas Anderson", StringComparison.Ordinal) && user.Text.Contains("| email |", StringComparison.Ordinal), "three fields");

            ToolResult fields = await Run("redis_get", "{\"key\":\"hugo:hash:user:1\",\"fields\":[\"plan\",\"missing\"]}", token).ConfigureAwait(false);
            RedisTestHelper.Assert(RedisTestHelper.ItemCount(fields) == 2 && fields.Text.Contains("red-pill", StringComparison.Ordinal) && fields.Text.Contains("(nil)", StringComparison.Ordinal), "selected fields");

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            string args = "{\"key\":\"hugo:hash:large\",\"limit\":100}";
            for (int i = 0; i < 20; i++)
            {
                ToolResult page = await Run("redis_get", args, token).ConfigureAwait(false);
                RedisTestHelper.AssertOk(page, "large hash page");
                RedisTestHelper.Assert(RedisTestHelper.Num(page, "length") == 300, "HLEN 300");
                foreach (string line in page.Text.Split('\n').Where(l => l.StartsWith("| field-", StringComparison.Ordinal))) seen.Add(line.Split('|')[1].Trim());
                string? next = RedisTestHelper.Str(page, "next");
                if (next == null) break;
                args = "{\"key\":\"hugo:hash:large\",\"limit\":100,\"cursor\":\"" + next + "\"}";
            }
            RedisTestHelper.Assert(seen.Count == 300, "all 300 fields via paging, got " + seen.Count);
        }

        private static async Task GetList(CancellationToken token)
        {
            ToolResult head = await Run("redis_get", "{\"key\":\"hugo:list:queue\",\"limit\":5}", token).ConfigureAwait(false);
            RedisTestHelper.AssertOk(head, "list");
            RedisTestHelper.Assert(RedisTestHelper.Num(head, "length") == 50 && RedisTestHelper.ItemCount(head) == 5 && head.Text.Contains("| 0 | \"job-1\" |", StringComparison.Ordinal) && RedisTestHelper.Str(head, "next") == "5", "first five");
            ToolResult rev = await Run("redis_get", "{\"key\":\"hugo:list:queue\",\"limit\":3,\"reverse\":true}", token).ConfigureAwait(false);
            RedisTestHelper.Assert(rev.Text.Contains("| 49 | \"job-50\" |", StringComparison.Ordinal) && rev.Text.Contains("| 47 | \"job-48\" |", StringComparison.Ordinal), "tail first: " + rev.Text);
            ToolResult mid = await Run("redis_get", "{\"key\":\"hugo:list:queue\",\"limit\":10,\"offset\":45}", token).ConfigureAwait(false);
            RedisTestHelper.Assert(RedisTestHelper.ItemCount(mid) == 5 && RedisTestHelper.Str(mid, "next") == null, "last five, no next");
        }

        private static async Task GetSetZset(CancellationToken token)
        {
            ToolResult set = await Run("redis_get", "{\"key\":\"hugo:set:tags\",\"limit\":100}", token).ConfigureAwait(false);
            RedisTestHelper.AssertOk(set, "set");
            RedisTestHelper.Assert(RedisTestHelper.Num(set, "length") == 30 && RedisTestHelper.ItemCount(set) == 30 && set.Text.Contains("tag-17", StringComparison.Ordinal), "all members");
            ToolResult z = await Run("redis_get", "{\"key\":\"hugo:zset:leaderboard\",\"limit\":3,\"reverse\":true}", token).ConfigureAwait(false);
            RedisTestHelper.AssertOk(z, "zset");
            RedisTestHelper.Assert(z.Text.Contains("\"player-25\" | 250", StringComparison.Ordinal) && RedisTestHelper.Num(z, "length") == 25, "highest score first: " + z.Text);
            ToolResult zf = await Run("redis_get", "{\"key\":\"hugo:zset:leaderboard\",\"limit\":2,\"offset\":1}", token).ConfigureAwait(false);
            RedisTestHelper.Assert(zf.Text.Contains("| 1 | \"player-02\" | 20 |", StringComparison.Ordinal), "rank offset: " + zf.Text);
        }

        private static async Task GetStream(CancellationToken token)
        {
            ToolResult first = await Run("redis_get", "{\"key\":\"hugo:stream:events\",\"limit\":10}", token).ConfigureAwait(false);
            RedisTestHelper.AssertOk(first, "stream");
            RedisTestHelper.Assert(RedisTestHelper.Num(first, "length") == 30 && RedisTestHelper.ItemCount(first) == 10 && first.Text.Contains("1-1", StringComparison.Ordinal) && first.Text.Contains("type=\"event-1\"", StringComparison.Ordinal), "first ten");
            string? next = RedisTestHelper.Str(first, "next");
            RedisTestHelper.Assert(next == "(10-1", "exclusive next ID: " + next);
            ToolResult second = await Run("redis_get", "{\"key\":\"hugo:stream:events\",\"limit\":10,\"cursor\":\"" + next + "\"}", token).ConfigureAwait(false);
            RedisTestHelper.Assert(second.Text.Contains("| 11-1 |", StringComparison.Ordinal) && !second.Text.Contains("| 10-1 |", StringComparison.Ordinal), "continues after 10-1");
            ToolResult newest = await Run("redis_get", "{\"key\":\"hugo:stream:events\",\"limit\":2,\"reverse\":true}", token).ConfigureAwait(false);
            RedisTestHelper.Assert(newest.Text.Contains("| 30-1 |", StringComparison.Ordinal), "newest first");
        }

        private static async Task GetMissing(CancellationToken token)
        {
            ToolResult r = await Run("redis_get", "{\"key\":\"hugo:does:not:exist\"}", token).ConfigureAwait(false);
            RedisTestHelper.AssertError(r, ConnectorErrorCodes.NotFound, "missing key");
        }

        private static async Task Ttl(CancellationToken token)
        {
            ToolResult r = await Run("redis_ttl", "{\"keys\":[\"hugo:ttl:session\",\"hugo:string:greeting\",\"hugo:nope\"]}", token).ConfigureAwait(false);
            RedisTestHelper.AssertOk(r, "ttl");
            List<string?> ttl = RedisTestHelper.Column(r, 2);
            RedisTestHelper.Assert(Int64.Parse(ttl[0]!, System.Globalization.CultureInfo.InvariantCulture) > 80000000, "session ttl near a day");
            RedisTestHelper.Assert(ttl[1] == "-1" && ttl[2] == "-2", "no expiry and missing");
            RedisTestHelper.Assert(r.Text.Contains("no expiry", StringComparison.Ordinal) && r.Text.Contains("key does not exist", StringComparison.Ordinal), "readable");
        }

        private static async Task MemoryUsage(CancellationToken token)
        {
            ToolResult r = await Run("redis_memory_usage", "{\"keys\":[\"hugo:string:big\",\"hugo:hash:large\",\"hugo:missing\"]}", token).ConfigureAwait(false);
            RedisTestHelper.AssertOk(r, "memory");
            List<string?> bytes = RedisTestHelper.Column(r, 2);
            RedisTestHelper.Assert(Int64.Parse(bytes[0]!, System.Globalization.CultureInfo.InvariantCulture) >= 200000, "big string bytes");
            RedisTestHelper.Assert(bytes[2] == null && r.Text.Contains("(missing)", StringComparison.Ordinal), "missing key");
        }

        private static async Task Slowlog(CancellationToken token)
        {
            await Run("redis_get", "{\"key\":\"hugo:string:greeting\"}", token).ConfigureAwait(false);
            ToolResult r = await Run("redis_slowlog", "{\"count\":50}", token).ConfigureAwait(false);
            RedisTestHelper.AssertOk(r, "slowlog");
            RedisTestHelper.Assert(RedisTestHelper.Column(r, 4).Any(c => c != null && c.Length > 0), "commands listed: " + r.Text);
        }

        private static async Task ClientList(CancellationToken token)
        {
            ToolResult r = await Run("redis_client_list", "{}", token).ConfigureAwait(false);
            RedisTestHelper.AssertOk(r, "clients");
            RedisTestHelper.Assert(RedisTestHelper.Column(r, 3).Contains("hugo"), "Hugo's client name listed");
            RedisTestHelper.Assert(r.Text.Contains("clientsByName", StringComparison.Ordinal), "summary");
        }

        private static async Task Latency(CancellationToken token)
        {
            ToolResult r = await Run("redis_latency", "{\"doctor\":true}", token).ConfigureAwait(false);
            RedisTestHelper.AssertOk(r, "latency");
            RedisTestHelper.Assert(r.Text.Contains("latency-monitor-threshold", StringComparison.Ordinal) && r.Text.Contains("1 ms", StringComparison.Ordinal), "threshold: " + r.Text);
            RedisTestHelper.Assert(r.Text.Contains("LATENCY DOCTOR", StringComparison.Ordinal), "doctor text");
            ToolResult h = await Run("redis_latency", "{\"event\":\"command\"}", token).ConfigureAwait(false);
            RedisTestHelper.AssertOk(h, "history");
        }

        private static async Task BigKeys(CancellationToken token)
        {
            ToolResult r = await Run("redis_bigkeys", "{\"pattern\":\"hugo:*\",\"top\":5}", token).ConfigureAwait(false);
            RedisTestHelper.AssertOk(r, "bigkeys");
            List<string?> keys = RedisTestHelper.Column(r, 0);
            RedisTestHelper.Assert(keys.Count == 5 && keys[0] == "hugo:string:big", "big string first: " + String.Join(",", keys));
            RedisTestHelper.Assert(r.Text.Contains("byType", StringComparison.Ordinal) && r.Text.Contains("sampledKeys", StringComparison.Ordinal), "notes");
        }

        private static async Task WriteLifecycle(CancellationToken token)
        {
            string prefix = "hugo:test:" + Guid.NewGuid().ToString("N").Substring(0, 8) + ":";
            using (CountingSessionPool pool = new CountingSessionPool())
            {
                ConnectorInvocationContext ctx = RedisTestHelper.Context(RedisTestHelper.Resource(RedisTestbed.Settings(), RedisTestbed.Password, pool));
                try
                {
                    ToolResult set = await RedisTestHelper.RunAsync("redis_set", RedisTestHelper.Json(new { key = prefix + "a", value = "one", ttlSeconds = 300 }), ctx, token).ConfigureAwait(false);
                    RedisTestHelper.AssertOk(set, "set");
                    RedisTestHelper.Assert(RedisTestHelper.Raw(set, "set") == "true", "set true");
                    ToolResult nx = await RedisTestHelper.RunAsync("redis_set", RedisTestHelper.Json(new { key = prefix + "a", value = "two", condition = "ifNotExists" }), ctx, token).ConfigureAwait(false);
                    RedisTestHelper.Assert(!nx.IsError && RedisTestHelper.Raw(nx, "set") == "false" && nx.Text.Contains("Not set", StringComparison.Ordinal), "NX on existing key");
                    ToolResult xx = await RedisTestHelper.RunAsync("redis_set", RedisTestHelper.Json(new { key = prefix + "b", value = "two", condition = "ifExists" }), ctx, token).ConfigureAwait(false);
                    RedisTestHelper.Assert(RedisTestHelper.Raw(xx, "set") == "false", "XX on missing key");
                    ToolResult keep = await RedisTestHelper.RunAsync("redis_set", RedisTestHelper.Json(new { key = prefix + "a", value = "three", keepTtl = true }), ctx, token).ConfigureAwait(false);
                    RedisTestHelper.AssertOk(keep, "keepTtl");
                    ToolResult ttl = await RedisTestHelper.RunAsync("redis_ttl", RedisTestHelper.Json(new { keys = new[] { prefix + "a" } }), ctx, token).ConfigureAwait(false);
                    RedisTestHelper.Assert(Int64.Parse(RedisTestHelper.Column(ttl, 2)[0]!, System.Globalization.CultureInfo.InvariantCulture) > 200000, "ttl kept");
                    ToolResult get = await RedisTestHelper.RunAsync("redis_get", RedisTestHelper.Json(new { key = prefix + "a" }), ctx, token).ConfigureAwait(false);
                    RedisTestHelper.Assert(get.Text.Contains("three", StringComparison.Ordinal), "value replaced");

                    ToolResult persist = await RedisTestHelper.RunAsync("redis_expire", RedisTestHelper.Json(new { key = prefix + "a", persist = true }), ctx, token).ConfigureAwait(false);
                    RedisTestHelper.AssertOk(persist, "persist");
                    RedisTestHelper.Assert(persist.Text.Contains("no longer expires", StringComparison.Ordinal), "persisted");
                    ToolResult expire = await RedisTestHelper.RunAsync("redis_expire", RedisTestHelper.Json(new { key = prefix + "a", ttlSeconds = 120 }), ctx, token).ConfigureAwait(false);
                    RedisTestHelper.AssertOk(expire, "expire");
                    ToolResult missing = await RedisTestHelper.RunAsync("redis_expire", RedisTestHelper.Json(new { key = prefix + "zzz", ttlSeconds = 120 }), ctx, token).ConfigureAwait(false);
                    RedisTestHelper.AssertError(missing, ConnectorErrorCodes.NotFound, "expire missing");

                    await RedisTestHelper.RunAsync("redis_set", RedisTestHelper.Json(new { key = prefix + "b", value = "x" }), ctx, token).ConfigureAwait(false);
                    ToolResult del = await RedisTestHelper.RunAsync("redis_delete", RedisTestHelper.Json(new { keys = new[] { prefix + "a", prefix + "b", prefix + "nope" } }), ctx, token).ConfigureAwait(false);
                    RedisTestHelper.AssertOk(del, "delete");
                    RedisTestHelper.Assert(del.Text.StartsWith("Deleted 2 of 3", StringComparison.Ordinal) && del.Metrics["deleted"] == 2, "deleted two");
                    ToolResult gone = await RedisTestHelper.RunAsync("redis_get", RedisTestHelper.Json(new { key = prefix + "a" }), ctx, token).ConfigureAwait(false);
                    RedisTestHelper.AssertError(gone, ConnectorErrorCodes.NotFound, "gone");
                    RedisTestHelper.Assert(pool.Created == 1, "one pooled connection");
                }
                finally
                {
                    await RedisTestHelper.RunAsync("redis_delete", RedisTestHelper.Json(new { keys = new[] { prefix + "a", prefix + "b" } }), ctx, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        private static async Task Command(CancellationToken token)
        {
            ToolResult h = await Run("redis_command", "{\"command\":\"HGETALL hugo:hash:user:1\"}", token).ConfigureAwait(false);
            RedisTestHelper.AssertOk(h, "HGETALL");
            RedisTestHelper.Assert(h.Text.Contains("\"Thomas Anderson\"", StringComparison.Ordinal) && h.Text.StartsWith("1) ", StringComparison.Ordinal), "redis-cli style: " + h.Text);
            RedisTestHelper.Assert((h.StructuredJson ?? String.Empty).Contains("\"command\":\"HGETALL\"", StringComparison.Ordinal), "structured");
            ToolResult cfg = await Run("redis_command", "{\"command\":\"CONFIG GET\",\"args\":[\"latency-monitor-threshold\"]}", token).ConfigureAwait(false);
            RedisTestHelper.Assert(cfg.Text.Contains("\"1\"", StringComparison.Ordinal), "CONFIG GET with args");
            ToolResult n = await Run("redis_command", "{\"command\":\"INCRBY hugo:string:counter 0\"}", token).ConfigureAwait(false);
            RedisTestHelper.Assert(n.Text.Trim() == "(integer) 42", "integer reply: " + n.Text);
            ToolResult wrongType = await Run("redis_command", "{\"command\":\"HGETALL hugo:string:greeting\"}", token).ConfigureAwait(false);
            RedisTestHelper.AssertError(wrongType, ConnectorErrorCodes.InvalidArgument, "WRONGTYPE");
            ToolResult unknown = await Run("redis_command", "{\"command\":\"NOTACOMMAND x\"}", token).ConfigureAwait(false);
            RedisTestHelper.AssertError(unknown, ConnectorErrorCodes.InvalidArgument, "unknown command");
            ToolResult keys = await Run("redis_command", "{\"command\":\"KEYS *\"}", token).ConfigureAwait(false);
            RedisTestHelper.AssertError(keys, ConnectorErrorCodes.PermissionDenied, "KEYS denied");
            ToolResult nil = await Run("redis_command", "{\"command\":\"GET hugo:nothing:here\"}", token).ConfigureAwait(false);
            RedisTestHelper.Assert(nil.Text.Trim() == "(nil)", "nil reply");
        }

        private static async Task OutputLimits(CancellationToken token)
        {
            OutputLimits limits = new OutputLimits { MaxRows = 10, MaxLines = 1000 };
            ConnectorInvocationContext ctx = RedisTestHelper.Context(RedisTestHelper.Resource(RedisTestbed.Settings(), RedisTestbed.Password), AccessModeEnum.ReadWrite, 30, limits);
            ToolResult range = await RedisTestHelper.RunAsync("redis_command", "{\"command\":\"LRANGE hugo:list:queue 0 -1\"}", ctx, token).ConfigureAwait(false);
            RedisTestHelper.AssertOk(range, "lrange");
            RedisTestHelper.Assert(range.Truncated && range.Text.Contains("40 more element(s) omitted", StringComparison.Ordinal), "reply capped at 10 rows: " + range.Text);
            ToolResult get = await RedisTestHelper.RunAsync("redis_get", "{\"key\":\"hugo:hash:large\",\"limit\":5000}", ctx, token).ConfigureAwait(false);
            RedisTestHelper.Assert(RedisTestHelper.ItemCount(get) == 10 && RedisTestHelper.Str(get, "next") != null, "limit capped by MaxRows with a continuation");
            OutputLimits lines = new OutputLimits { MaxLines = 15 };
            ToolResult cut = await RedisTestHelper.RunAsync("redis_get", "{\"key\":\"hugo:list:queue\",\"limit\":50}", RedisTestHelper.Context(RedisTestHelper.Resource(RedisTestbed.Settings(), RedisTestbed.Password), AccessModeEnum.ReadWrite, 30, lines), token).ConfigureAwait(false);
            RedisTestHelper.Assert(cut.Truncated && cut.Text.Split('\n').Length <= 17 && cut.OriginalBytes > 0, "line limit");
        }

        private static async Task Timeout(CancellationToken token)
        {
            ConnectorInvocationContext ctx = RedisTestHelper.Context(RedisTestHelper.Resource(RedisTestbed.Settings(), RedisTestbed.Password), AccessModeEnum.ReadWrite, 1);
            Stopwatch sw = Stopwatch.StartNew();
            ToolResult r = await RedisTestHelper.RunAsync("redis_command", "{\"command\":\"DEBUG SLEEP 3\"}", ctx, token).ConfigureAwait(false);
            RedisTestHelper.AssertError(r, ConnectorErrorCodes.Timeout, "DEBUG SLEEP 3 with a 1 second timeout");
            RedisTestHelper.Assert(sw.Elapsed < TimeSpan.FromSeconds(2.8), "returned at the timeout, took " + sw.Elapsed);
            await Task.Delay(2500, token).ConfigureAwait(false);
        }

        private static async Task Cancellation(CancellationToken token)
        {
            using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500)))
            {
                ConnectorInvocationContext ctx = RedisTestHelper.Context(RedisTestHelper.Resource(RedisTestbed.Settings(), RedisTestbed.Password));
                Stopwatch sw = Stopwatch.StartNew();
                bool threw = false;
                try
                {
                    await RedisTestHelper.RunAsync("redis_command", "{\"command\":\"DEBUG SLEEP 3\"}", ctx, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    threw = true;
                }
                RedisTestHelper.Assert(threw && sw.Elapsed < TimeSpan.FromSeconds(2.8), "cancelled after " + sw.Elapsed);
            }
            await Task.Delay(3000, token).ConfigureAwait(false);
        }

        private static async Task Pooling(CancellationToken token)
        {
            using (CountingSessionPool pool = new CountingSessionPool())
            {
                ConnectorInvocationContext ctx = RedisTestHelper.Context(RedisTestHelper.Resource(RedisTestbed.Settings(), RedisTestbed.Password, pool));
                List<Task<ToolResult>> calls = new List<Task<ToolResult>>();
                for (int i = 0; i < 8; i++) calls.Add(RedisTestHelper.RunAsync("redis_ttl", "{\"keys\":[\"hugo:ttl:session\"]}", ctx, token));
                ToolResult[] results = await Task.WhenAll(calls).ConfigureAwait(false);
                RedisTestHelper.Assert(results.All(r => !r.IsError), "all succeeded");
                RedisTestHelper.Assert(pool.Created == 1, "one connection for eight concurrent calls, got " + pool.Created);
            }
        }

        private static async Task SecretScrubbing(CancellationToken token)
        {
            List<string> outputs = new List<string>();
            string[] calls = new string[]
            {
                "redis_info|{}", "redis_client_list|{}", "redis_slowlog|{\"count\":128}", "redis_command|{\"command\":\"CONFIG GET requirepass\"}",
                "redis_command|{\"command\":\"ACL LIST\"}", "redis_latency|{}"
            };
            foreach (string call in calls)
            {
                string[] parts = call.Split('|', 2);
                ToolResult r = await Run(parts[0], parts[1], token).ConfigureAwait(false);
                outputs.Add(r.Text + (r.StructuredJson ?? String.Empty));
            }
            int leaks = outputs.Count(o => o.Contains(RedisTestbed.Password, StringComparison.Ordinal));
            RedisTestHelper.Assert(leaks == 0, "no output contains the password, found " + leaks);
            RedisTestHelper.Assert(outputs[3].Contains("[redacted]", StringComparison.Ordinal), "CONFIG GET requirepass is redacted by the connector");
        }

        #endregion
    }
}
