namespace Test.Shared.Connectors.Agents
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Agents.Mcp;
    using Touchstone.Core;

    /// <summary>
    /// <c>mcp</c> connector cases against <see cref="TestMcpServer"/> (an in-process Voltaic MCP server on 127.0.0.1):
    /// discovery, filters, annotation-based classification, pass-through calls, remote errors, timeouts, cancellation,
    /// authentication headers, instruction fencing, resources, caching and refresh, Stdio gates, and secret hygiene.
    /// </summary>
    public static class McpServerCases
    {
        #region Private-Members

        private static readonly string Token = "mcp-test-token-5e4d3c";

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
            cases.Add(new TestCaseDescriptor(suiteId, "test-connection", "TestConnectionAsync connects, discovers tools, and seeds classification", TestConnection));
            cases.Add(new TestCaseDescriptor(suiteId, "list-tools", "mcp_list_tools lists tools with effects, hints, and schemas, and fences the server instructions", ListTools));
            cases.Add(new TestCaseDescriptor(suiteId, "list-tools-options", "filter, includeSchemas=false, and the row limit shape the listing", ListToolsOptions));
            cases.Add(new TestCaseDescriptor(suiteId, "include-exclude", "includeTools and excludeTools hide tools from the listing and refuse calls", IncludeExclude));
            cases.Add(new TestCaseDescriptor(suiteId, "classification", "After discovery each remote tool classifies from its annotations", Classification));
            cases.Add(new TestCaseDescriptor(suiteId, "call-passthrough", "mcp_call_tool passes arguments through and returns text and structuredContent", CallPassthrough));
            cases.Add(new TestCaseDescriptor(suiteId, "call-errors", "Remote isError, unknown tools, and bad arguments become stable error codes", CallErrors));
            cases.Add(new TestCaseDescriptor(suiteId, "call-binary", "Image content is summarized, never included", CallBinary));
            cases.Add(new TestCaseDescriptor(suiteId, "timeout", "A slow remote tool times out at timeoutSeconds and the session stays usable", TimeoutCase));
            cases.Add(new TestCaseDescriptor(suiteId, "cancellation", "Caller cancellation propagates as OperationCanceledException", Cancellation));
            cases.Add(new TestCaseDescriptor(suiteId, "auth-headers", "The authorizationHeader token is sent as Bearer, schemes are kept, and extra headers are sent", AuthHeaders));
            cases.Add(new TestCaseDescriptor(suiteId, "auth-rejected", "A rejected token is auth_failed for tools and Test Connection and never echoed", AuthRejected));
            cases.Add(new TestCaseDescriptor(suiteId, "connection-refused", "A closed port is connection_failed", ConnectionRefused));
            cases.Add(new TestCaseDescriptor(suiteId, "resources", "mcp_list_resources and mcp_read_resource list and read remote resources", Resources));
            cases.Add(new TestCaseDescriptor(suiteId, "refresh", "The tool list is cached until refresh=true", Refresh));
            cases.Add(new TestCaseDescriptor(suiteId, "stdio-gates", "Stdio needs the server gate and the resource opt-in; a missing command is connection_failed", StdioGates));
            return cases;
        }

        #endregion

        #region Private-Methods

        private static ConnectorResourceContext Res(TestMcpServer server, NoPoolSessionPool pool, Dictionary<string, Dictionary<string, string>>? secrets = null, params string[] extra)
        {
            List<string> pairs = new List<string> { "url", server.Url };
            pairs.AddRange(extra);
            return AgentsTestHelper.Resource("mcp", AgentsTestHelper.Settings(pairs.ToArray()), secrets, pool);
        }

        private static async Task TestConnection(CancellationToken token)
        {
            McpConnector c = new McpConnector();
            using (TestMcpServer server = await TestMcpServer.StartAsync(token).ConfigureAwait(false))
            using (NoPoolSessionPool pool = new NoPoolSessionPool())
            {
                ConnectorResourceContext r = Res(server, pool, null, "excludeTools", "internal_*");
                ConnectorHealthResult health = await c.TestConnectionAsync(r, token).ConfigureAwait(false);
                AgentsTestHelper.Assert(health.Success, "test connection: " + health.ErrorCode + " " + health.Message);
                AgentsTestHelper.Assert(health.Details["tools"] == "9", "tools discovered: " + health.Details["tools"]);
                AgentsTestHelper.Assert(health.Details["allowedTools"] == "8", "allowed tools: " + health.Details["allowedTools"]);
                AgentsTestHelper.Assert(health.Details["hasInstructions"] == "true", "instructions reported");
                AgentsTestHelper.Assert(health.LatencyMs > 0, "latency");

                EffectClassification read = AgentsTestHelper.Classify(c, "mcp_call_tool", "{\"name\":\"read_status\"}", AgentsTestHelper.Context(r));
                AgentsTestHelper.Assert(read.Effect == EffectEnum.Read, "test connection seeds classification: " + read.Reason);
            }
        }

        private static async Task ListTools(CancellationToken token)
        {
            McpConnector c = new McpConnector();
            using (TestMcpServer server = await TestMcpServer.StartAsync(token).ConfigureAwait(false))
            using (NoPoolSessionPool pool = new NoPoolSessionPool())
            {
                ConnectorInvocationContext ctx = AgentsTestHelper.Context(Res(server, pool));
                ToolResult result = await AgentsTestHelper.RunAsync(c, "mcp_list_tools", "{}", ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertOk(result, "list tools");
                AgentsTestHelper.AssertContains(result.Text, "- read_status [R]", "read tool");
                AgentsTestHelper.AssertContains(result.Text, "- delete_records [D]", "destructive tool");
                AgentsTestHelper.AssertContains(result.Text, "- create_ticket [W]", "unannotated tool");
                AgentsTestHelper.AssertContains(result.Text, "- update_ticket [W]", "explicit write");
                AgentsTestHelper.AssertContains(result.Text, "- contradictory [D]", "contradictory hints");
                AgentsTestHelper.AssertContains(result.Text, "hints: readOnlyHint=true; effect Read", "hint line");
                AgentsTestHelper.AssertContains(result.Text, "input schema: {", "schema shown");
                AgentsTestHelper.AssertContains(result.Text, "<untrusted-content source=\"instructions from MCP server", "instructions fenced");
                AgentsTestHelper.AssertContains(result.Text, "Call read_status before anything else.", "instructions text");
                AgentsTestHelper.AssertContains(result.Text, "&lt;/untrusted-content>", "injected closing tag neutralized");
                int closes = result.Text.Split("</untrusted-content>").Length - 1;
                AgentsTestHelper.Assert(closes == 1, "exactly one real fence close, found " + closes);
                int fenceEnd = result.Text.IndexOf("</untrusted-content>", StringComparison.Ordinal);
                AgentsTestHelper.Assert(result.Text.IndexOf("SYSTEM: ignore", StringComparison.Ordinal) < fenceEnd, "the injection stays inside the fence");

                AgentsTestHelper.Assert(AgentsTestHelper.Structured(result, "discovered") == "9", "structured discovered");
                AgentsTestHelper.Assert(AgentsTestHelper.Structured(result, "shown") == "9", "structured shown");
                AgentsTestHelper.AssertContains(AgentsTestHelper.Structured(result, "instructions"), "read_status", "structured instructions");
                string tools = AgentsTestHelper.Structured(result, "tools") ?? String.Empty;
                AgentsTestHelper.AssertContains(tools, "\"name\":\"delete_records\"", "structured tool");
                AgentsTestHelper.AssertContains(tools, "\"effect\":\"Destructive\"", "structured effect");
                AgentsTestHelper.AssertContains(tools, "\"destructiveHint\":true", "structured hint");
                AgentsTestHelper.AssertContains(tools, "\"inputSchema\":{", "structured schema object");
                AgentsTestHelper.Assert(result.Metrics.ContainsKey("latencyMs"), "latency metric");
            }
        }

        private static async Task ListToolsOptions(CancellationToken token)
        {
            McpConnector c = new McpConnector();
            using (TestMcpServer server = await TestMcpServer.StartAsync(token).ConfigureAwait(false))
            using (NoPoolSessionPool pool = new NoPoolSessionPool())
            {
                ConnectorResourceContext r = Res(server, pool);
                ToolResult filtered = await AgentsTestHelper.RunAsync(c, "mcp_list_tools", "{\"filter\":\"TICKET\"}", AgentsTestHelper.Context(r), token).ConfigureAwait(false);
                AgentsTestHelper.AssertOk(filtered, "filtered");
                AgentsTestHelper.Assert(AgentsTestHelper.Structured(filtered, "matched") == "2", "two ticket tools: " + AgentsTestHelper.Structured(filtered, "matched"));
                AgentsTestHelper.Assert(!filtered.Text.Contains("- read_status", StringComparison.Ordinal), "filter hides other tools");

                ToolResult compact = await AgentsTestHelper.RunAsync(c, "mcp_list_tools", "{\"includeSchemas\":false}", AgentsTestHelper.Context(r), token).ConfigureAwait(false);
                AgentsTestHelper.AssertOk(compact, "compact");
                AgentsTestHelper.Assert(!compact.Text.Contains("input schema:", StringComparison.Ordinal), "schemas omitted");
                AgentsTestHelper.AssertContains(AgentsTestHelper.Structured(compact, "tools"), "\"schemaOmitted\":true", "structured omission flag");

                ToolResult bounded = await AgentsTestHelper.RunAsync(c, "mcp_list_tools", "{}", AgentsTestHelper.Context(r, 30, new OutputLimits { MaxRows = 2 }), token).ConfigureAwait(false);
                AgentsTestHelper.AssertOk(bounded, "bounded");
                AgentsTestHelper.Assert(AgentsTestHelper.Structured(bounded, "shown") == "2", "row limit applies");
                AgentsTestHelper.AssertContains(bounded.Text, "Only the first 2 are shown", "row limit note");

                ToolResult small = await AgentsTestHelper.RunAsync(c, "mcp_list_tools", "{}", AgentsTestHelper.Context(r, 30, new OutputLimits { MaxBytes = 1024 }), token).ConfigureAwait(false);
                AgentsTestHelper.Assert(small.Truncated && small.OriginalBytes > 1024, "byte limit truncates");
            }
        }

        private static async Task IncludeExclude(CancellationToken token)
        {
            McpConnector c = new McpConnector();
            using (TestMcpServer server = await TestMcpServer.StartAsync(token).ConfigureAwait(false))
            using (NoPoolSessionPool pool = new NoPoolSessionPool())
            {
                ConnectorInvocationContext ctx = AgentsTestHelper.Context(Res(server, pool, null, "includeTools", "read_*\ncreate_ticket\ninternal_*", "excludeTools", "internal_debug"));
                ToolResult list = await AgentsTestHelper.RunAsync(c, "mcp_list_tools", "{}", ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertOk(list, "list");
                AgentsTestHelper.Assert(AgentsTestHelper.Structured(list, "allowed") == "2", "two allowed: " + AgentsTestHelper.Structured(list, "allowed"));
                AgentsTestHelper.AssertContains(list.Text, "7 hidden by includeTools or excludeTools", "hidden count");
                AgentsTestHelper.Assert(!list.Text.Contains("- delete_records", StringComparison.Ordinal), "not included tool hidden");
                AgentsTestHelper.Assert(!list.Text.Contains("internal_debug", StringComparison.Ordinal), "excluded tool hidden");

                EffectClassification excluded = AgentsTestHelper.Classify(c, "mcp_call_tool", "{\"name\":\"internal_debug\"}", ctx);
                AgentsTestHelper.Assert(excluded.Rejected, "excluded call rejected at classification");
                ToolResult call = await AgentsTestHelper.RunAsync(c, "mcp_call_tool", "{\"name\":\"delete_records\"}", ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertError(call, ConnectorErrorCodes.PermissionDenied, "not included call");
                ToolResult ok = await AgentsTestHelper.RunAsync(c, "mcp_call_tool", "{\"name\":\"read_status\"}", ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertOk(ok, "included call");
            }
        }

        private static async Task Classification(CancellationToken token)
        {
            McpConnector c = new McpConnector();
            using (TestMcpServer server = await TestMcpServer.StartAsync(token).ConfigureAwait(false))
            using (NoPoolSessionPool pool = new NoPoolSessionPool())
            {
                ConnectorInvocationContext ctx = AgentsTestHelper.Context(Res(server, pool));
                AgentsTestHelper.AssertOk(await AgentsTestHelper.RunAsync(c, "mcp_list_tools", "{}", ctx, token).ConfigureAwait(false), "discovery");
                Dictionary<string, EffectEnum> expected = new Dictionary<string, EffectEnum>
                {
                    { "read_status", EffectEnum.Read },
                    { "image_tool", EffectEnum.Read },
                    { "delete_records", EffectEnum.Destructive },
                    { "contradictory", EffectEnum.Destructive },
                    { "create_ticket", EffectEnum.Write },
                    { "update_ticket", EffectEnum.Write },
                    { "never_listed", EffectEnum.Destructive }
                };
                foreach (KeyValuePair<string, EffectEnum> row in expected)
                {
                    EffectClassification e = AgentsTestHelper.Classify(c, "mcp_call_tool", "{\"name\":\"" + row.Key + "\",\"arguments\":{\"title\":\"x\"}}", ctx);
                    AgentsTestHelper.Assert(e.Effect == row.Value && !e.Rejected, row.Key + " expected " + row.Value + " got " + e.Effect + " (" + e.Reason + ")");
                    AgentsTestHelper.Assert(!String.IsNullOrEmpty(e.Reason), row.Key + " has a reason");
                    AgentsTestHelper.AssertContains(e.ApprovalSummary, "\"" + row.Key + "\"", row.Key + " summary names the tool");
                    AgentsTestHelper.AssertContains(e.ApprovalSummary, "{\"title\":\"x\"}", row.Key + " summary shows arguments");
                }
                ToolResult unknown = await AgentsTestHelper.RunAsync(c, "mcp_call_tool", "{\"name\":\"never_listed\"}", ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertError(unknown, ConnectorErrorCodes.NotFound, "unlisted tool");
            }
        }

        private static async Task CallPassthrough(CancellationToken token)
        {
            McpConnector c = new McpConnector();
            using (TestMcpServer server = await TestMcpServer.StartAsync(token).ConfigureAwait(false))
            using (NoPoolSessionPool pool = new NoPoolSessionPool())
            {
                ConnectorInvocationContext ctx = AgentsTestHelper.Context(Res(server, pool));
                string args = "{\"name\":\"create_ticket\",\"arguments\":{\"title\":\"Disk full on web-01\",\"priority\":2,\"tags\":[\"disk\",\"prod\"]}}";
                ToolResult result = await AgentsTestHelper.RunAsync(c, "mcp_call_tool", args, ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertOk(result, "create_ticket");
                AgentsTestHelper.Assert(server.CreateTicketArguments.TryDequeue(out string? received), "server received the call");
                AgentsTestHelper.AssertContains(received, "\"title\":\"Disk full on web-01\"", "title passed through");
                AgentsTestHelper.AssertContains(received, "\"priority\":2", "number passed through");
                AgentsTestHelper.AssertContains(received, "\"tags\":[\"disk\",\"prod\"]", "array passed through");
                AgentsTestHelper.AssertContains(result.Text, "Remote tool create_ticket on", "header");
                AgentsTestHelper.AssertContains(result.Text, "third-party content", "marked as third-party");
                AgentsTestHelper.AssertContains(result.Text, "created with", "remote text");
                AgentsTestHelper.AssertContains(AgentsTestHelper.Structured(result, "structuredContent"), "\"id\":42", "structuredContent passed through");
                AgentsTestHelper.Assert(AgentsTestHelper.Structured(result, "effect") == "Write", "effect recorded");

                ToolResult noArgs = await AgentsTestHelper.RunAsync(c, "mcp_call_tool", "{\"name\":\"read_status\"}", ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertOk(noArgs, "call without arguments");
                AgentsTestHelper.AssertContains(noArgs.Text, "status: green", "read_status text");
            }
        }

        private static async Task CallErrors(CancellationToken token)
        {
            McpConnector c = new McpConnector();
            using (TestMcpServer server = await TestMcpServer.StartAsync(token).ConfigureAwait(false))
            using (NoPoolSessionPool pool = new NoPoolSessionPool())
            {
                ConnectorInvocationContext ctx = AgentsTestHelper.Context(Res(server, pool));
                ToolResult fail = await AgentsTestHelper.RunAsync(c, "mcp_call_tool", "{\"name\":\"fail_tool\"}", ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertError(fail, ConnectorErrorCodes.UpstreamError, "remote isError");
                AgentsTestHelper.AssertContains(fail.Text, "boom: backend unavailable", "remote error text");
                AgentsTestHelper.AssertContains(fail.Text, "reported an error", "error header");

                ToolResult missing = await AgentsTestHelper.RunAsync(c, "mcp_call_tool", "{\"name\":\"no_such_tool\"}", ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertError(missing, ConnectorErrorCodes.NotFound, "unknown tool");
                AgentsTestHelper.AssertContains(missing.Text, "mcp_list_tools", "hint to list");

                ToolResult badArgs = await AgentsTestHelper.RunAsync(c, "mcp_call_tool", "{\"name\":\"read_status\",\"arguments\":\"not an object\"}", ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertError(badArgs, ConnectorErrorCodes.InvalidArgument, "non-object arguments");

                ToolResult noName = await AgentsTestHelper.RunAsync(c, "mcp_call_tool", "{}", ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertError(noName, ConnectorErrorCodes.InvalidArgument, "missing name");

                ToolResult after = await AgentsTestHelper.RunAsync(c, "mcp_call_tool", "{\"name\":\"read_status\"}", ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertOk(after, "session still usable after errors");
            }
        }

        private static async Task CallBinary(CancellationToken token)
        {
            McpConnector c = new McpConnector();
            using (TestMcpServer server = await TestMcpServer.StartAsync(token).ConfigureAwait(false))
            using (NoPoolSessionPool pool = new NoPoolSessionPool())
            {
                ConnectorInvocationContext ctx = AgentsTestHelper.Context(Res(server, pool));
                ToolResult image = await AgentsTestHelper.RunAsync(c, "mcp_call_tool", "{\"name\":\"image_tool\"}", ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertOk(image, "image tool");
                AgentsTestHelper.AssertContains(image.Text, "[image image/png, about", "image summarized");
                AgentsTestHelper.Assert(!image.Text.Contains("iVBORw0KGgo", StringComparison.Ordinal) && !(image.StructuredJson ?? String.Empty).Contains("iVBORw0KGgo", StringComparison.Ordinal), "image data not included");
            }
        }

        private static async Task TimeoutCase(CancellationToken token)
        {
            McpConnector c = new McpConnector();
            using (TestMcpServer server = await TestMcpServer.StartAsync(token).ConfigureAwait(false))
            using (NoPoolSessionPool pool = new NoPoolSessionPool())
            {
                ConnectorInvocationContext ctx = AgentsTestHelper.Context(Res(server, pool, null, "timeoutSeconds", "1"));
                AgentsTestHelper.AssertOk(await AgentsTestHelper.RunAsync(c, "mcp_list_tools", "{}", ctx, token).ConfigureAwait(false), "warm up");
                Stopwatch sw = Stopwatch.StartNew();
                ToolResult slow = await AgentsTestHelper.RunAsync(c, "mcp_call_tool", "{\"name\":\"slow_tool\"}", ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertError(slow, ConnectorErrorCodes.Timeout, "slow tool");
                AgentsTestHelper.Assert(sw.Elapsed < TimeSpan.FromSeconds(4), "timed out early: " + sw.Elapsed);
                ToolResult after = await AgentsTestHelper.RunAsync(c, "mcp_call_tool", "{\"name\":\"read_status\"}", ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertOk(after, "session usable after a timeout");

                ConnectorInvocationContext shortCall = AgentsTestHelper.Context(Res(server, pool), 1);
                ToolResult bindingTimeout = await AgentsTestHelper.RunAsync(c, "mcp_call_tool", "{\"name\":\"slow_tool\"}", shortCall, token).ConfigureAwait(false);
                AgentsTestHelper.AssertError(bindingTimeout, ConnectorErrorCodes.Timeout, "call timeout shorter than resource timeout");
            }
        }

        private static async Task Cancellation(CancellationToken token)
        {
            McpConnector c = new McpConnector();
            using (TestMcpServer server = await TestMcpServer.StartAsync(token).ConfigureAwait(false))
            using (NoPoolSessionPool pool = new NoPoolSessionPool())
            using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                ConnectorInvocationContext ctx = AgentsTestHelper.Context(Res(server, pool));
                AgentsTestHelper.AssertOk(await AgentsTestHelper.RunAsync(c, "mcp_list_tools", "{}", ctx, token).ConfigureAwait(false), "warm up");
                cts.CancelAfter(300);
                bool cancelled = false;
                try
                {
                    await AgentsTestHelper.RunAsync(c, "mcp_call_tool", "{\"name\":\"slow_tool\"}", ctx, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }
                AgentsTestHelper.Assert(cancelled, "cancellation propagates");
            }
        }

        private static async Task AuthHeaders(CancellationToken token)
        {
            McpConnector c = new McpConnector();
            using (TestMcpServer server = await TestMcpServer.StartAsync(token).ConfigureAwait(false))
            {
                server.RequiredAuthorization = "Bearer " + Token;
                using (NoPoolSessionPool pool = new NoPoolSessionPool())
                {
                    Dictionary<string, Dictionary<string, string>> secrets = new Dictionary<string, Dictionary<string, string>>
                    {
                        { "authorizationHeader", new Dictionary<string, string> { { "token", Token } } },
                        { "extraHeaders", new Dictionary<string, string> { { "names", "X-Tenant" }, { "X-Tenant", "blue-team" } } }
                    };
                    ConnectorInvocationContext ctx = AgentsTestHelper.Context(Res(server, pool, secrets));
                    ToolResult list = await AgentsTestHelper.RunAsync(c, "mcp_list_tools", "{}", ctx, token).ConfigureAwait(false);
                    AgentsTestHelper.AssertOk(list, "authorized listing");
                    AgentsTestHelper.Assert(server.AuthorizationHeaders.All(h => h == "Bearer " + Token), "every request carried the bearer token");
                    AgentsTestHelper.Assert(server.TenantHeaders.Any() && server.TenantHeaders.All(h => h == "blue-team"), "extra header sent");
                    AgentsTestHelper.AssertNoSecret(list, Token, "listing");
                }

                server.RequiredAuthorization = "Basic dXNlcjpwYXNz";
                using (NoPoolSessionPool pool = new NoPoolSessionPool())
                {
                    Dictionary<string, Dictionary<string, string>> basic = new Dictionary<string, Dictionary<string, string>> { { "authorizationHeader", new Dictionary<string, string> { { "token", "Basic dXNlcjpwYXNz" } } } };
                    ToolResult list = await AgentsTestHelper.RunAsync(c, "mcp_list_tools", "{}", AgentsTestHelper.Context(Res(server, pool, basic)), token).ConfigureAwait(false);
                    AgentsTestHelper.AssertOk(list, "scheme kept as-is");
                }
            }
        }

        private static async Task AuthRejected(CancellationToken token)
        {
            McpConnector c = new McpConnector();
            using (TestMcpServer server = await TestMcpServer.StartAsync(token).ConfigureAwait(false))
            using (NoPoolSessionPool pool = new NoPoolSessionPool())
            {
                server.RequiredAuthorization = "Bearer the-right-token";
                Dictionary<string, Dictionary<string, string>> wrong = new Dictionary<string, Dictionary<string, string>> { { "authorizationHeader", new Dictionary<string, string> { { "token", Token } } } };
                ConnectorResourceContext r = Res(server, pool, wrong);
                ToolResult list = await AgentsTestHelper.RunAsync(c, "mcp_list_tools", "{}", AgentsTestHelper.Context(r), token).ConfigureAwait(false);
                AgentsTestHelper.AssertError(list, ConnectorErrorCodes.AuthFailed, "wrong token");
                AgentsTestHelper.AssertNoSecret(list, Token, "auth error");
                ConnectorHealthResult health = await c.TestConnectionAsync(r, token).ConfigureAwait(false);
                AgentsTestHelper.Assert(!health.Success && health.ErrorCode == ConnectorErrorCodes.AuthFailed, "test connection auth_failed: " + health.ErrorCode + " " + health.Message);
                AgentsTestHelper.Assert(!health.Message.Contains(Token, StringComparison.Ordinal), "no token in health message");

                ConnectorResourceContext none = Res(server, pool);
                ToolResult anonymous = await AgentsTestHelper.RunAsync(c, "mcp_list_tools", "{}", AgentsTestHelper.Context(none), token).ConfigureAwait(false);
                AgentsTestHelper.AssertError(anonymous, ConnectorErrorCodes.AuthFailed, "missing token");
            }
        }

        private static async Task ConnectionRefused(CancellationToken token)
        {
            McpConnector c = new McpConnector();
            using (NoPoolSessionPool pool = new NoPoolSessionPool())
            {
                int port = AgentsTestHelper.FreePort();
                ConnectorResourceContext r = AgentsTestHelper.Resource("mcp", AgentsTestHelper.Settings("url", "http://127.0.0.1:" + port + "/mcp", "timeoutSeconds", "5"), null, pool);
                ToolResult list = await AgentsTestHelper.RunAsync(c, "mcp_list_tools", "{}", AgentsTestHelper.Context(r), token).ConfigureAwait(false);
                AgentsTestHelper.AssertError(list, ConnectorErrorCodes.ConnectionFailed, "closed port");
                ConnectorHealthResult health = await c.TestConnectionAsync(r, token).ConfigureAwait(false);
                AgentsTestHelper.Assert(!health.Success && health.ErrorCode == ConnectorErrorCodes.ConnectionFailed, "health connection_failed: " + health.ErrorCode + " " + health.Message);

                ConnectorResourceContext noUrl = AgentsTestHelper.Resource("mcp", AgentsTestHelper.Settings(), null, pool);
                ToolResult invalid = await AgentsTestHelper.RunAsync(c, "mcp_list_tools", "{}", AgentsTestHelper.Context(noUrl), token).ConfigureAwait(false);
                AgentsTestHelper.AssertError(invalid, ConnectorErrorCodes.InvalidSetting, "missing url");
            }
        }

        private static async Task Resources(CancellationToken token)
        {
            McpConnector c = new McpConnector();
            using (TestMcpServer server = await TestMcpServer.StartAsync(token).ConfigureAwait(false))
            using (NoPoolSessionPool pool = new NoPoolSessionPool())
            {
                ConnectorInvocationContext ctx = AgentsTestHelper.Context(Res(server, pool));
                ToolResult list = await AgentsTestHelper.RunAsync(c, "mcp_list_resources", "{}", ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertOk(list, "list resources");
                AgentsTestHelper.AssertContains(list.Text, "memo://readme", "resource listed");
                AgentsTestHelper.Assert(AgentsTestHelper.Structured(list, "discovered") == "1", "one resource");

                ToolResult read = await AgentsTestHelper.RunAsync(c, "mcp_read_resource", "{\"uri\":\"memo://readme\"}", ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertOk(read, "read resource");
                AgentsTestHelper.AssertContains(read.Text, "Hello from the memo.", "resource text");
                AgentsTestHelper.AssertContains(AgentsTestHelper.Structured(read, "contents"), "\"text\":\"Hello from the memo.\"", "structured contents");

                ToolResult missing = await AgentsTestHelper.RunAsync(c, "mcp_read_resource", "{\"uri\":\"memo://nothing\"}", ctx, token).ConfigureAwait(false);
                AgentsTestHelper.Assert(missing.IsError && (missing.ErrorCode == ConnectorErrorCodes.NotFound || missing.ErrorCode == ConnectorErrorCodes.InvalidArgument || missing.ErrorCode == ConnectorErrorCodes.UpstreamError), "missing resource is an error: " + missing.ErrorCode + " " + missing.Text);
            }
        }

        private static async Task Refresh(CancellationToken token)
        {
            McpConnector c = new McpConnector();
            using (TestMcpServer server = await TestMcpServer.StartAsync(token).ConfigureAwait(false))
            using (NoPoolSessionPool pool = new NoPoolSessionPool())
            {
                ConnectorInvocationContext ctx = AgentsTestHelper.Context(Res(server, pool));
                ToolResult first = await AgentsTestHelper.RunAsync(c, "mcp_list_tools", "{}", ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertOk(first, "first listing");
                server.AddReadOnlyTool("late_tool");
                ToolResult cached = await AgentsTestHelper.RunAsync(c, "mcp_list_tools", "{}", ctx, token).ConfigureAwait(false);
                AgentsTestHelper.Assert(!cached.Text.Contains("late_tool", StringComparison.Ordinal), "cached list is reused");
                AgentsTestHelper.Assert(AgentsTestHelper.Structured(cached, "fetchedUtc") == AgentsTestHelper.Structured(first, "fetchedUtc"), "same fetch time");
                EffectClassification before = AgentsTestHelper.Classify(c, "mcp_call_tool", "{\"name\":\"late_tool\"}", ctx);
                AgentsTestHelper.Assert(before.Effect == EffectEnum.Destructive, "unknown until refreshed");

                ToolResult refreshed = await AgentsTestHelper.RunAsync(c, "mcp_list_tools", "{\"refresh\":true}", ctx, token).ConfigureAwait(false);
                AgentsTestHelper.AssertContains(refreshed.Text, "- late_tool [R]", "refresh discovers the new tool");
                EffectClassification after = AgentsTestHelper.Classify(c, "mcp_call_tool", "{\"name\":\"late_tool\"}", ctx);
                AgentsTestHelper.Assert(after.Effect == EffectEnum.Read, "classified after refresh");
            }
        }

        private static async Task StdioGates(CancellationToken token)
        {
            using (NoPoolSessionPool pool = new NoPoolSessionPool())
            {
                McpConnector closed = new McpConnector();
                ConnectorResourceContext r = AgentsTestHelper.Resource("mcp", AgentsTestHelper.Settings("transport", "Stdio", "command", "hugo-no-such-mcp-server", "allowStdio", "true"), null, pool);
                ToolResult gated = await AgentsTestHelper.RunAsync(closed, "mcp_list_tools", "{}", AgentsTestHelper.Context(r), token).ConfigureAwait(false);
                AgentsTestHelper.AssertError(gated, ConnectorErrorCodes.PermissionDenied, "server gate closed");
                AgentsTestHelper.AssertContains(gated.Text, "Connectors.Mcp.AllowStdio", "server gate named");
                ConnectorHealthResult health = await closed.TestConnectionAsync(r, token).ConfigureAwait(false);
                AgentsTestHelper.Assert(!health.Success && health.ErrorCode == ConnectorErrorCodes.PermissionDenied, "health respects the server gate");

                McpConnector open = new McpConnector(new McpConnectorOptions { AllowStdio = true });
                ConnectorResourceContext optOut = AgentsTestHelper.Resource("mcp", AgentsTestHelper.Settings("transport", "Stdio", "command", "hugo-no-such-mcp-server"), null, pool);
                ToolResult resourceGate = await AgentsTestHelper.RunAsync(open, "mcp_list_tools", "{}", AgentsTestHelper.Context(optOut), token).ConfigureAwait(false);
                AgentsTestHelper.AssertError(resourceGate, ConnectorErrorCodes.PermissionDenied, "resource gate closed");
                AgentsTestHelper.AssertContains(resourceGate.Text, "allowStdio", "resource gate named");

                ToolResult missing = await AgentsTestHelper.RunAsync(open, "mcp_list_tools", "{}", AgentsTestHelper.Context(r), token).ConfigureAwait(false);
                AgentsTestHelper.AssertError(missing, ConnectorErrorCodes.ConnectionFailed, "missing executable");
            }
        }

        #endregion
    }
}
