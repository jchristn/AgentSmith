namespace Test.Shared.Cli
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Cli.Credentials;
    using Hugo.Cli.Mcp;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;

    /// <summary>
    /// The stdio to Streamable HTTP relay against the in-process fake Hugo server: session and auth headers, JSON and
    /// event-stream responses, notifications, token renewal on 401, session re-initialization on 404, the stateless
    /// revision's headers, resumption, server-to-client requests, failures, and the stdout discipline.
    /// </summary>
    public static class CliRelayCases
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string SuiteId = "cli.relay";

        #endregion

        #region Private-Members

        private const string _Initialize = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{\"roots\":{}},\"clientInfo\":{\"name\":\"cli-test\",\"version\":\"1.0\"}}}";
        private const string _Initialized = "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the cases.
        /// </summary>
        /// <returns>Cases.</returns>
        public static List<TestCaseDescriptor> Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(Case("round-trip", "initialize, tools/list, tools/call carry bearer, session, and version headers; EOF deletes the session", RoundTrip));
            cases.Add(Case("sse-stream", "An event-stream response relays progress and log notifications before the response", SseStream));
            cases.Add(Case("notifications-silent", "Notifications are forwarded and produce no output", NotificationsSilent));
            cases.Add(Case("renew-on-401", "A 401 renews the token once with the access key and retries", RenewOn401));
            cases.Add(Case("renew-updates-cache", "A token renewed from cached keys is written back to the login cache", RenewUpdatesCache));
            cases.Add(Case("401-without-keys", "A 401 without keys answers the request with a JSON-RPC error", RejectedWithoutKeys));
            cases.Add(Case("session-expired", "A 404 on the session replays initialize, then retries; the replay is not written", SessionExpired));
            cases.Add(Case("stateless-headers", "2026-07-28 requests carry MCP-Protocol-Version, Mcp-Method, Mcp-Name, Mcp-Param-*, and no session", StatelessHeaders));
            cases.Add(Case("server-unreachable", "An unreachable server answers requests with JSON-RPC errors", ServerUnreachable));
            cases.Add(Case("parse-error", "A line that is not JSON gets a -32700 error and the relay keeps going", ParseError));
            cases.Add(Case("standalone-stream", "Server-initiated messages on the GET stream are relayed", StandaloneStream));
            cases.Add(Case("resume-stream", "A response stream that breaks is resumed with Last-Event-ID", ResumeStream));
            cases.Add(Case("server-request", "A server request on the stream is answered through stdin while the call is in flight", ServerRequest));
            cases.Add(Case("profile-path", "--profile posts to /mcp/{profile} with the name percent-encoded", ProfilePath));
            cases.Add(Case("batch", "A JSON-RPC batch is forwarded and its array response relayed", Batch));
            cases.Add(Case("stdout-protocol-only", "With --verbose, stdout carries only JSON-RPC messages and diagnostics go to stderr", StdoutProtocolOnly));
            return cases;
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        private static string Call(string id, string tool, string text)
        {
            return "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"tools/call\",\"params\":{\"name\":\"" + tool + "\",\"arguments\":{\"text\":\"" + text + "\"},\"_meta\":{\"progressToken\":\"p1\"}}}";
        }

        private static string Request(string id, string method)
        {
            return "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"" + method + "\"}";
        }

        private static RelayHarness StartWithToken(CliTestEnvironment env, FakeHugoServer server, CancellationToken token, params string[] extra)
        {
            List<string> options = new List<string> { "--server", server.BaseAddress, "--token", server.IssueToken() };
            options.AddRange(extra);
            return RelayHarness.Start(env, token, options.ToArray());
        }

        private static async Task HandshakeAsync(RelayHarness harness)
        {
            await harness.SendAsync(_Initialize).ConfigureAwait(false);
            JsonRpcMessage response = await harness.ReadResponseAsync("1", new List<string>()).ConfigureAwait(false);
            Assert.True(response.Result != null, "initialize result");
            await harness.SendAsync(_Initialized).ConfigureAwait(false);
        }

        private static List<FakeHugoRequest> McpPosts(FakeHugoServer server)
        {
            return server.Where(r => r.Method == "POST" && r.RawPath.StartsWith("/mcp", StringComparison.Ordinal));
        }

        private static void AssertProtocolLine(string line)
        {
            JsonRpcMessage? message = JsonRpcMessage.TryParse(line);
            bool batch = line.StartsWith("[", StringComparison.Ordinal);
            Assert.True(batch || (message != null && message.JsonRpc == "2.0"), "stdout line is a JSON-RPC message: " + line);
            Assert.True(line.IndexOf('\r', StringComparison.Ordinal) < 0, "no carriage return");
        }

        private static async Task RoundTrip(CancellationToken token)
        {
            using (FakeHugoServer server = new FakeHugoServer())
            using (CliTestEnvironment env = new CliTestEnvironment())
            using (RelayHarness harness = StartWithToken(env, server, token))
            {
                List<string> seen = new List<string>();
                await harness.SendAsync(_Initialize).ConfigureAwait(false);
                JsonRpcMessage init = await harness.ReadResponseAsync("1", seen).ConfigureAwait(false);
                Assert.True(seen[0].Contains("\"protocolVersion\":\"2025-06-18\"", StringComparison.Ordinal), "initialize result relayed: " + seen[0]);
                await harness.SendAsync(_Initialized).ConfigureAwait(false);
                await harness.SendAsync(Request("2", "tools/list")).ConfigureAwait(false);
                await harness.ReadResponseAsync("2", seen).ConfigureAwait(false);
                await harness.SendAsync(Call("3", "echo", "hi")).ConfigureAwait(false);
                await harness.ReadResponseAsync("3", seen).ConfigureAwait(false);
                Assert.True(seen.Last().Contains("echo: hi", StringComparison.Ordinal), "tool result: " + seen.Last());
                int code = await harness.StopAsync().ConfigureAwait(false);
                Assert.Equal(0, code, "exit code; stderr: " + harness.Stderr);
                foreach (string line in harness.Lines) AssertProtocolLine(line);

                List<FakeHugoRequest> posts = McpPosts(server);
                Assert.Equal(4, posts.Count, "posts");
                FakeHugoRequest initPost = posts.First(p => p.RpcMethod == "initialize");
                Assert.True(initPost.Header("MCP-Session-Id") == null, "initialize has no session");
                foreach (FakeHugoRequest post in posts)
                {
                    Assert.True((post.Header("Authorization") ?? String.Empty).StartsWith("Bearer tok-", StringComparison.Ordinal), "bearer on " + post.RpcMethod);
                    string accept = post.Header("Accept") ?? String.Empty;
                    Assert.True(accept.Contains("application/json", StringComparison.Ordinal) && accept.Contains("text/event-stream", StringComparison.Ordinal), "accept on " + post.RpcMethod + ": " + accept);
                    Assert.True((post.Header("Content-Type") ?? String.Empty).StartsWith("application/json", StringComparison.Ordinal), "content type");
                }
                foreach (FakeHugoRequest post in posts.Where(p => p.RpcMethod != "initialize"))
                {
                    Assert.Equal("sess-1", post.Header("MCP-Session-Id"), "session on " + post.RpcMethod);
                    Assert.Equal("2025-06-18", post.Header("MCP-Protocol-Version"), "version on " + post.RpcMethod);
                }
                Assert.Equal(202, posts.First(p => p.RpcMethod == "notifications/initialized").Status, "notification accepted");
                List<FakeHugoRequest> deletes = server.Where(r => r.Method == "DELETE" && r.RawPath == "/mcp");
                Assert.Equal(1, deletes.Count, "session deleted on exit");
                Assert.Equal("sess-1", deletes[0].Header("MCP-Session-Id"), "deleted session");
            }
        }

        private static async Task SseStream(CancellationToken token)
        {
            using (FakeHugoServer server = new FakeHugoServer())
            using (CliTestEnvironment env = new CliTestEnvironment())
            using (RelayHarness harness = StartWithToken(env, server, token))
            {
                await HandshakeAsync(harness).ConfigureAwait(false);
                List<string> seen = new List<string>();
                await harness.SendAsync(Call("5", "stream_echo", "abc")).ConfigureAwait(false);
                await harness.ReadResponseAsync("5", seen).ConfigureAwait(false);
                Assert.Equal(3, seen.Count, "progress, log message, response: " + String.Join(" | ", seen));
                Assert.True(seen[0].Contains("notifications/progress", StringComparison.Ordinal) && seen[0].Contains("\"progressToken\":\"p1\"", StringComparison.Ordinal), "progress first: " + seen[0]);
                Assert.True(seen[1].Contains("notifications/message", StringComparison.Ordinal) && seen[1].Contains("line one\\nline two", StringComparison.Ordinal), "log message with escaped newline: " + seen[1]);
                Assert.True(seen[2].Contains("streamed: abc", StringComparison.Ordinal), "response: " + seen[2]);
                foreach (string line in seen) AssertProtocolLine(line);
                Assert.Equal(0, await harness.StopAsync().ConfigureAwait(false), "exit code");
            }
        }

        private static async Task NotificationsSilent(CancellationToken token)
        {
            using (FakeHugoServer server = new FakeHugoServer())
            using (CliTestEnvironment env = new CliTestEnvironment())
            using (RelayHarness harness = StartWithToken(env, server, token))
            {
                await HandshakeAsync(harness).ConfigureAwait(false);
                await harness.SendAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/cancelled\",\"params\":{\"requestId\":99,\"reason\":\"test\"}}").ConfigureAwait(false);
                await harness.SendAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/roots/list_changed\"}").ConfigureAwait(false);
                await harness.SendAsync(Request("9", "ping")).ConfigureAwait(false);
                await harness.ReadResponseAsync("9", new List<string>()).ConfigureAwait(false);
                Assert.Equal(0, await harness.StopAsync().ConfigureAwait(false), "exit code");
                Assert.Equal(2, harness.Lines.Count, "only the initialize and ping responses: " + String.Join(" | ", harness.Lines));
                List<FakeHugoRequest> notes = McpPosts(server).Where(p => p.RpcMethod != null && p.RpcMethod.StartsWith("notifications/", StringComparison.Ordinal)).ToList();
                Assert.Equal(3, notes.Count, "notifications forwarded");
                Assert.True(notes.All(n => n.Status == 202), "notifications accepted");
            }
        }

        private static async Task RenewOn401(CancellationToken token)
        {
            using (FakeHugoServer server = new FakeHugoServer())
            using (CliTestEnvironment env = new CliTestEnvironment())
            using (RelayHarness harness = RelayHarness.Start(env, token, "--server", server.BaseAddress, "--access-key", server.AccessKey, "--secret-key", server.SecretKey))
            {
                await HandshakeAsync(harness).ConfigureAwait(false);
                Assert.Equal(1, server.TokensIssued, "keys exchanged for a token on first use");
                server.RejectNextMcpRequests = 1;
                await harness.SendAsync(Request("2", "tools/list")).ConfigureAwait(false);
                JsonRpcMessage response = await harness.ReadResponseAsync("2", new List<string>()).ConfigureAwait(false);
                Assert.True(response.Result != null, "tools/list succeeded after renewal");
                Assert.Equal(2, server.TokensIssued, "one renewal");
                List<FakeHugoRequest> lists = McpPosts(server).Where(p => p.RpcMethod == "tools/list" || (p.Status == 401 && p.RpcMethod == null)).ToList();
                Assert.Equal(2, lists.Count, "rejected attempt and retry");
                Assert.True(lists[0].Header("Authorization") != lists[1].Header("Authorization"), "retry used the new token");
                Assert.True(harness.Stderr.Contains("Renewed the session token", StringComparison.Ordinal), "renewal logged to stderr: " + harness.Stderr);
                Assert.Equal(0, await harness.StopAsync().ConfigureAwait(false), "exit code");
            }
        }

        private static async Task RenewUpdatesCache(CancellationToken token)
        {
            using (FakeHugoServer server = new FakeHugoServer())
            using (CliTestEnvironment env = new CliTestEnvironment())
            {
                CredentialStore store = new CredentialStore(env.CredentialsPath);
                await store.UpdateAsync(file =>
                {
                    file.Upsert(new ServerCredential { Server = server.BaseAddress, Token = "stale-token", AccessKey = server.AccessKey, SecretKey = server.SecretKey });
                    return true;
                }, token).ConfigureAwait(false);
                using (RelayHarness harness = RelayHarness.Start(env, token))
                {
                    await HandshakeAsync(harness).ConfigureAwait(false);
                    Assert.Equal(0, await harness.StopAsync().ConfigureAwait(false), "exit code");
                }
                CredentialFile after = await store.LoadAsync(token).ConfigureAwait(false);
                string? saved = after.Find(server.BaseAddress)?.Token;
                Assert.True(saved != null && saved != "stale-token", "token replaced in cache");
                Assert.True(server.IsValid(saved), "cached token is the renewed one");
                Assert.Equal(server.AccessKey, after.Find(server.BaseAddress)?.AccessKey, "keys kept");
            }
        }

        private static async Task RejectedWithoutKeys(CancellationToken token)
        {
            using (FakeHugoServer server = new FakeHugoServer())
            using (CliTestEnvironment env = new CliTestEnvironment())
            using (RelayHarness harness = RelayHarness.Start(env, token, "--server", server.BaseAddress, "--token", "not-a-valid-token"))
            {
                await harness.SendAsync(_Initialize).ConfigureAwait(false);
                List<string> seen = new List<string>();
                JsonRpcMessage response = await harness.ReadResponseAsync("1", seen).ConfigureAwait(false);
                Assert.True(response.Error != null, "error response");
                Assert.True(seen[0].Contains("\"code\":-32603", StringComparison.Ordinal) && seen[0].Contains("\"httpStatus\":401", StringComparison.Ordinal), "error shape: " + seen[0]);
                Assert.True(harness.Stderr.Contains("hugo login", StringComparison.Ordinal), "stderr explains: " + harness.Stderr);
                Assert.Equal(0, server.TokensIssued, "no sign-in attempted");
                Assert.Equal(0, await harness.StopAsync().ConfigureAwait(false), "exit code");
            }
        }

        private static async Task SessionExpired(CancellationToken token)
        {
            using (FakeHugoServer server = new FakeHugoServer())
            using (CliTestEnvironment env = new CliTestEnvironment())
            using (RelayHarness harness = StartWithToken(env, server, token))
            {
                await HandshakeAsync(harness).ConfigureAwait(false);
                server.ExpireSessionOnNextRequest = true;
                await harness.SendAsync(Request("2", "tools/list")).ConfigureAwait(false);
                JsonRpcMessage response = await harness.ReadResponseAsync("2", new List<string>()).ConfigureAwait(false);
                Assert.True(response.Result != null, "tools/list answered on the new session");
                Assert.Equal(2, server.SessionsCreated, "a new session was created");
                FakeHugoRequest last = McpPosts(server).Last(p => p.RpcMethod == "tools/list");
                Assert.Equal("sess-2", last.Header("MCP-Session-Id"), "retry on the new session");
                Assert.True(McpPosts(server).Count(p => p.RpcMethod == "notifications/initialized") == 2, "initialized sent again");
                Assert.Equal(0, await harness.StopAsync().ConfigureAwait(false), "exit code");
                Assert.Equal(2, harness.Lines.Count, "the replayed initialize response is not written: " + String.Join(" | ", harness.Lines));
            }
        }

        private static async Task StatelessHeaders(CancellationToken token)
        {
            using (FakeHugoServer server = new FakeHugoServer())
            using (CliTestEnvironment env = new CliTestEnvironment())
            using (RelayHarness harness = StartWithToken(env, server, token))
            {
                string meta = "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientInfo\":{\"name\":\"t\",\"version\":\"1\"}}";
                await harness.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":\"s1\",\"method\":\"tools/call\",\"params\":{\"name\":\"echo\",\"arguments\":{\"text\":\"x\"}," + meta + "}}").ConfigureAwait(false);
                await harness.ReadResponseAsync("\"s1\"", new List<string>()).ConfigureAwait(false);
                await harness.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":\"s2\",\"method\":\"tools/list\",\"params\":{" + meta + "}}").ConfigureAwait(false);
                await harness.ReadResponseAsync("\"s2\"", new List<string>()).ConfigureAwait(false);
                await harness.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":\"s3\",\"method\":\"tools/call\",\"params\":{\"name\":\"region_echo\",\"arguments\":{\"region\":\"eu-west-1\",\"text\":\"y\"}," + meta + "}}").ConfigureAwait(false);
                await harness.ReadResponseAsync("\"s3\"", new List<string>()).ConfigureAwait(false);
                await harness.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":\"s4\",\"method\":\"tools/call\",\"params\":{\"name\":\"café\",\"arguments\":{}," + meta + "}}").ConfigureAwait(false);
                await harness.ReadResponseAsync("\"s4\"", new List<string>()).ConfigureAwait(false);
                Assert.Equal(0, await harness.StopAsync().ConfigureAwait(false), "exit code");

                List<FakeHugoRequest> posts = McpPosts(server);
                Assert.Equal(4, posts.Count, "posts");
                foreach (FakeHugoRequest post in posts)
                {
                    Assert.Equal("2026-07-28", post.Header("MCP-Protocol-Version"), "version header");
                    Assert.Equal(post.RpcMethod, post.Header("Mcp-Method"), "method header");
                    Assert.True(post.Header("MCP-Session-Id") == null, "no session header");
                }
                Assert.Equal("echo", posts[0].Header("Mcp-Name"), "name header");
                Assert.True(posts[1].Header("Mcp-Name") == null, "no name header on tools/list");
                Assert.Equal("eu-west-1", posts[2].Header("Mcp-Param-Region"), "x-mcp-header parameter mirrored");
                Assert.Equal("=?base64?" + Convert.ToBase64String(Encoding.UTF8.GetBytes("café")) + "?=", posts[3].Header("Mcp-Name"), "non-ASCII name encoded");
                Assert.Equal(0, server.Where(r => r.Method == "DELETE").Count, "no session to delete");
            }
        }

        private static async Task ServerUnreachable(CancellationToken token)
        {
            TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            using (CliTestEnvironment env = new CliTestEnvironment())
            using (RelayHarness harness = RelayHarness.Start(env, token, "--server", "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture), "--token", "t"))
            {
                await harness.SendAsync(Request("4", "ping")).ConfigureAwait(false);
                List<string> seen = new List<string>();
                JsonRpcMessage response = await harness.ReadResponseAsync("4", seen).ConfigureAwait(false);
                Assert.True(response.Error != null, "error response");
                Assert.True(seen[0].Contains("\"httpStatus\":0", StringComparison.Ordinal), "no HTTP status: " + seen[0]);
                await harness.SendAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}").ConfigureAwait(false);
                Assert.Equal(0, await harness.StopAsync().ConfigureAwait(false), "exit code");
                Assert.Equal(1, harness.Lines.Count, "a failed notification writes nothing");
                Assert.True(harness.Stderr.Contains("Cannot reach", StringComparison.Ordinal), "stderr: " + harness.Stderr);
            }
        }

        private static async Task ParseError(CancellationToken token)
        {
            using (FakeHugoServer server = new FakeHugoServer())
            using (CliTestEnvironment env = new CliTestEnvironment())
            using (RelayHarness harness = StartWithToken(env, server, token))
            {
                await harness.SendAsync("this is not json").ConfigureAwait(false);
                string? line = await harness.ReadAsync().ConfigureAwait(false);
                Assert.True(line != null && line.Contains("-32700", StringComparison.Ordinal) && line.Contains("\"id\":null", StringComparison.Ordinal), "parse error: " + line);
                await HandshakeAsync(harness).ConfigureAwait(false);
                Assert.Equal(0, await harness.StopAsync().ConfigureAwait(false), "exit code");
            }
        }

        private static async Task StandaloneStream(CancellationToken token)
        {
            using (FakeHugoServer server = new FakeHugoServer())
            using (CliTestEnvironment env = new CliTestEnvironment())
            {
                server.StandaloneStreamEnabled = true;
                using (RelayHarness harness = StartWithToken(env, server, token))
                {
                    await HandshakeAsync(harness).ConfigureAwait(false);
                    string? line = await harness.ReadAsync(10000).ConfigureAwait(false);
                    Assert.True(line != null && line.Contains("notifications/tools/list_changed", StringComparison.Ordinal), "server notification relayed: " + line + " stderr: " + harness.Stderr);
                    Assert.Equal(0, await harness.StopAsync().ConfigureAwait(false), "exit code");
                    FakeHugoRequest get = server.Where(r => r.Method == "GET" && r.RawPath == "/mcp").First();
                    Assert.Equal("sess-1", get.Header("MCP-Session-Id"), "GET carries the session");
                    Assert.True((get.Header("Accept") ?? String.Empty).Contains("text/event-stream", StringComparison.Ordinal), "GET accepts event streams");
                }
            }
        }

        private static async Task ResumeStream(CancellationToken token)
        {
            using (FakeHugoServer server = new FakeHugoServer())
            using (CliTestEnvironment env = new CliTestEnvironment())
            using (RelayHarness harness = StartWithToken(env, server, token, "--no-listen"))
            {
                await HandshakeAsync(harness).ConfigureAwait(false);
                List<string> seen = new List<string>();
                await harness.SendAsync(Call("7", "broken_stream", "zz")).ConfigureAwait(false);
                await harness.ReadResponseAsync("7", seen).ConfigureAwait(false);
                Assert.True(seen.Last().Contains("resumed: zz", StringComparison.Ordinal), "resumed response: " + seen.Last());
                Assert.True(seen.First().Contains("notifications/progress", StringComparison.Ordinal), "progress before the break");
                FakeHugoRequest resume = server.Where(r => r.Method == "GET" && r.Header("Last-Event-ID") != null).First();
                Assert.True(resume.Header("Last-Event-ID")!.StartsWith("b-", StringComparison.Ordinal), "Last-Event-ID");
                Assert.Equal("sess-1", resume.Header("MCP-Session-Id"), "resume carries the session");
                Assert.Equal(0, await harness.StopAsync().ConfigureAwait(false), "exit code");
            }
        }

        private static async Task ServerRequest(CancellationToken token)
        {
            using (FakeHugoServer server = new FakeHugoServer())
            using (CliTestEnvironment env = new CliTestEnvironment())
            using (RelayHarness harness = StartWithToken(env, server, token))
            {
                await HandshakeAsync(harness).ConfigureAwait(false);
                await harness.SendAsync(Call("8", "ask_client", "q")).ConfigureAwait(false);
                string? serverRequest = await harness.ReadAsync().ConfigureAwait(false);
                JsonRpcMessage? parsed = JsonRpcMessage.TryParse(serverRequest);
                Assert.True(parsed != null && parsed.IsRequest && parsed.Method == "roots/list", "server request relayed: " + serverRequest);

                await harness.SendAsync(Request("10", "ping")).ConfigureAwait(false);
                await harness.ReadResponseAsync("10", new List<string>()).ConfigureAwait(false);

                await harness.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":" + parsed!.IdKey + ",\"result\":{\"roots\":[]}}").ConfigureAwait(false);
                List<string> seen = new List<string>();
                await harness.ReadResponseAsync("8", seen).ConfigureAwait(false);
                Assert.True(seen.Last().Contains("client answered", StringComparison.Ordinal), "the call finished with the client's answer: " + seen.Last());
                FakeHugoRequest answer = McpPosts(server).First(p => p.RpcMethod == null && p.Body.Contains("roots", StringComparison.Ordinal));
                Assert.Equal(202, answer.Status, "the client's response was accepted");
                Assert.Equal("sess-1", answer.Header("MCP-Session-Id"), "the response carries the session");
                Assert.Equal(0, await harness.StopAsync().ConfigureAwait(false), "exit code");
            }
        }

        private static async Task ProfilePath(CancellationToken token)
        {
            using (FakeHugoServer server = new FakeHugoServer())
            using (CliTestEnvironment env = new CliTestEnvironment())
            using (RelayHarness harness = StartWithToken(env, server, token, "--profile", "Ops Team"))
            {
                await HandshakeAsync(harness).ConfigureAwait(false);
                Assert.Equal(0, await harness.StopAsync().ConfigureAwait(false), "exit code");
                Assert.True(McpPosts(server).All(p => p.RawPath == "/mcp/Ops%20Team"), "path: " + String.Join(",", McpPosts(server).Select(p => p.RawPath)));
            }
        }

        private static async Task Batch(CancellationToken token)
        {
            using (FakeHugoServer server = new FakeHugoServer())
            using (CliTestEnvironment env = new CliTestEnvironment())
            using (RelayHarness harness = StartWithToken(env, server, token))
            {
                await HandshakeAsync(harness).ConfigureAwait(false);
                await harness.SendAsync("[{\"jsonrpc\":\"2.0\",\"id\":\"b1\",\"method\":\"ping\"}]").ConfigureAwait(false);
                string? line = await harness.ReadAsync().ConfigureAwait(false);
                Assert.Equal("[{\"jsonrpc\":\"2.0\",\"id\":\"b1\",\"result\":{}}]", line, "batch response");
                Assert.Equal(0, await harness.StopAsync().ConfigureAwait(false), "exit code");
            }
        }

        private static async Task StdoutProtocolOnly(CancellationToken token)
        {
            using (FakeHugoServer server = new FakeHugoServer())
            using (CliTestEnvironment env = new CliTestEnvironment())
            {
                StringBuilder stdin = new StringBuilder();
                stdin.Append(_Initialize).Append('\n');
                stdin.Append(_Initialized).Append('\n');
                stdin.Append('\n');
                stdin.Append(Call("2", "stream_echo", "s")).Append("\r\n");
                stdin.Append(Request("3", "tools/list")).Append('\n');
                stdin.Append("garbage\n");
                stdin.Append(Call("4", "no_such_tool", "x"));
                env.Environment[CliEnvironmentVariables.Token] = server.IssueToken();
                env.Environment[CliEnvironmentVariables.Server] = server.BaseAddress;
                CliRunResult result = await env.RunAsync(stdin.ToString(), token, "mcp", "stdio", "--verbose", "--no-listen").ConfigureAwait(false);
                Assert.Equal(0, result.ExitCode, "exit code " + result);
                Assert.True(result.Stderr.Length > 0, "verbose diagnostics on stderr");
                Assert.True(result.Stderr.Contains("Credentials from the environment", StringComparison.Ordinal), "credential source on stderr");
                List<string> lines = result.StdoutLines;
                foreach (string line in lines) AssertProtocolLine(line);
                Assert.True(result.Stdout.EndsWith("\n", StringComparison.Ordinal), "every message ends with a newline");
                Assert.Equal(7, lines.Count, "initialize, progress, log, stream response, tools/list, parse error, unknown tool: " + result.Stdout);
                foreach (string id in new[] { "1", "2", "3", "4" })
                    Assert.True(lines.Any(l => JsonRpcMessage.TryParse(l)?.IdKey == id), "response " + id);
            }
        }

        #endregion
    }
}
