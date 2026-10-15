namespace Test.Shared.Integration.Mcp
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;
    using Hugo.Core.Responses;
    using Hugo.Core.Serialization;
    using Hugo.Server.Protocols;
    using Hugo.Server.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using Voltaic.Core;
    using Voltaic.Mcp;

    /// <summary>
    /// MCP surface suites (P7.11) with a real Voltaic MCP client against the in-process server on 127.0.0.1, on every
    /// configured provider: authentication and the bearer challenge, initialize instructions, per-caller tool lists for
    /// two profiles, ReadOnly hiding, calls through the echo connector, errors, the approval pending flow with progress
    /// notifications and hugo_await_approval, foreign tenant isolation, the profile lock, fail-closed unmapped tools,
    /// management tool gating, prompts and resources (including full guidance for a compact profile), the stateless
    /// 2026-07-28 revision, and ask_agent_smith.
    /// </summary>
    public static class McpSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the suites.
        /// </summary>
        /// <returns>Suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            List<TestSuiteDescriptor> suites = new List<TestSuiteDescriptor>();
            foreach (DatabaseTypeEnum provider in TestEnvironment.Providers())
            {
                string id = "mcp-" + provider.ToString().ToLowerInvariant();
                List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
                cases.Add(new TestCaseDescriptor(id, "auth-initialize", "401 with a bearer challenge, initialize instructions (Agent Smith, Mr. Anderson, resources), session handshake (" + provider + ")", ct => HandshakeAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "tools-per-caller", "Per-caller tools/list for two profiles with resource enums, ReadOnly hiding, model hiding, every listed tool mapped (" + provider + ")", ct => ToolsAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "calls", "Echo tool calls, structured content, isError results with stable codes, meta tools (" + provider + ")", ct => CallsAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "approval-pending", "Approval pending result with progress notifications, approve over REST, hugo_await_approval returns the result (" + provider + ")", ct => ApprovalAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "isolation-lock", "Foreign tenant isolation and the credential profile lock (" + provider + ")", ct => IsolationAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "registry-management", "Unmapped tools fail closed; management tools gated by expose_management_tools and an Admin-capable principal (" + provider + ")", ct => ManagementAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "prompts-resources", "Prompts and resources; full connector instructions for a compact profile (" + provider + ")", ct => PromptsAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "stateless-delegation", "Stateless 2026-07-28 client (server/discover instructions, tools) and ask_agent_smith listing and calls (" + provider + ")", ct => StatelessAsync(provider, ct)));
                suites.Add(new TestSuiteDescriptor(id, "MCP surface (" + provider + ")", cases));
            }
            return suites;
        }

        #endregion

        #region Private-Methods

        private static async Task HandshakeAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (McpFixture fx = await McpFixture.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                McpRawResponse anonymous = await fx.RawAsync("/mcp", new Dictionary<string, string>(), McpFixture.InitializeBody(), token).ConfigureAwait(false);
                Assert.Equal(401, anonymous.StatusCode, "anonymous initialize");
                Assert.True(anonymous.Challenge != null && anonymous.Challenge.StartsWith("Bearer", StringComparison.Ordinal), "bearer challenge: " + anonymous.Challenge);
                McpRawResponse bad = await fx.RawAsync("/mcp", new Dictionary<string, string> { { "Authorization", "Bearer nope" } }, McpFixture.InitializeBody(), token).ConfigureAwait(false);
                Assert.Equal(401, bad.StatusCode, "bad token");
                Assert.True(bad.Challenge != null && bad.Challenge.Contains("invalid_token", StringComparison.Ordinal), "invalid_token challenge: " + bad.Challenge);

                McpRawResponse init = await fx.RawAsync("/mcp", fx.AndersonHeaders(), McpFixture.InitializeBody(), token).ConfigureAwait(false);
                Assert.Equal(200, init.StatusCode, "initialize status: " + init.Body);
                Assert.True(!String.IsNullOrEmpty(init.SessionId), "session id issued");
                McpTestInitializeResult result = Result<McpTestInitializeResult>(init.Body);
                Assert.Equal("2025-06-18", result.ProtocolVersion, "negotiated revision");
                Assert.True(init.Body.Contains("\"serverInfo\":{\"name\":\"Hugo\"", StringComparison.Ordinal), "server name: " + init.Body);
                string instructions = result.Instructions ?? String.Empty;
                foreach (string expected in new[] { "Agent Smith", "Mr. Anderson", "echo-ro", "echo-rw", "echo-rwa" }) Assert.True(instructions.Contains(expected, StringComparison.Ordinal), "instructions mention " + expected + ": " + instructions);
                Assert.True(!instructions.Contains("echo-ops", StringComparison.Ordinal), "instructions omit another profile's resources");

                // The same instructions through the credential bound to Hugo, and Ops's through /mcp/Ops.
                McpRawResponse viaCredential = await fx.RawAsync("/mcp", McpFixture.Headers(fx.HugoCredential), McpFixture.InitializeBody(), token).ConfigureAwait(false);
                Assert.True((Result<McpTestInitializeResult>(viaCredential.Body).Instructions ?? String.Empty).Contains("echo-rwa", StringComparison.Ordinal), "credential profile instructions");
                McpRawResponse ops = await fx.RawAsync("/mcp/Ops", fx.AndersonHeaders(), McpFixture.InitializeBody(), token).ConfigureAwait(false);
                string opsInstructions = Result<McpTestInitializeResult>(ops.Body).Instructions ?? String.Empty;
                Assert.True(opsInstructions.Contains("echo-ops", StringComparison.Ordinal) && !opsInstructions.Contains("echo-rwa", StringComparison.Ordinal), "Ops instructions: " + opsInstructions);
                McpRawResponse header = await fx.RawAsync("/mcp", WithHeader(fx.AndersonHeaders(), "x-hugo-profile", "Ops"), McpFixture.InitializeBody(), token).ConfigureAwait(false);
                Assert.True((Result<McpTestInitializeResult>(header.Body).Instructions ?? String.Empty).Contains("echo-ops", StringComparison.Ordinal), "x-hugo-profile selects Ops");
                McpRawResponse missing = await fx.RawAsync("/mcp/Nobody", fx.AndersonHeaders(), McpFixture.InitializeBody(), token).ConfigureAwait(false);
                Assert.Equal(404, missing.StatusCode, "unknown profile");
                Assert.True(missing.Body.Contains("ProfileNotFound", StringComparison.Ordinal), "ProfileNotFound: " + missing.Body);

                // expose_mcp is honored.
                await HttpTestHelpers.SendAsync<AgentProfile>(fx.Rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + fx.Ops.Id, new { exposeMcp = false }, 200, token).ConfigureAwait(false);
                McpRawResponse hidden = await fx.RawAsync("/mcp/Ops", fx.AndersonHeaders(), McpFixture.InitializeBody(), token).ConfigureAwait(false);
                Assert.Equal(403, hidden.StatusCode, "expose_mcp false");
                Assert.True(hidden.Body.Contains("McpNotExposed", StringComparison.Ordinal), "McpNotExposed: " + hidden.Body);

                // A real session-based client completes the handshake and lists tools.
                using (McpTestClient client = await fx.ConnectAsync(fx.AndersonHeaders(), token).ConfigureAwait(false))
                {
                    Assert.True(!String.IsNullOrEmpty(client.Client.SessionId), "client session");
                    McpTestToolList tools = await client.ListToolsAsync(token).ConfigureAwait(false);
                    Assert.True(tools.Find("echo_read") != null && tools.Find("hugo_whoami") != null, "tools listed: " + String.Join(", ", tools.Names));
                    await client.Client.PingAsync(0, token).ConfigureAwait(false);
                }
            }
        }

        private static async Task ToolsAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (McpFixture fx = await McpFixture.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                HugoMcpServer server = fx.Rig.Context.Get<HugoMcpServer>();
                using (McpTestClient hugo = await fx.ConnectAsync(McpFixture.Headers(fx.HugoCredential), token).ConfigureAwait(false))
                using (McpTestClient ops = await fx.ConnectAsync(McpFixture.Headers(fx.OpsCredential), token).ConfigureAwait(false))
                {
                    McpTestToolList hugoTools = await hugo.ListToolsAsync(token).ConfigureAwait(false);
                    McpTestToolList opsTools = await ops.ListToolsAsync(token).ConfigureAwait(false);

                    McpTestTool read = hugoTools.Find("echo_read") ?? throw new TestAssertionException("echo_read missing for Hugo.");
                    Assert.Equal("echo-ro,echo-rw,echo-rwa", String.Join(",", read.ResourceEnum), "Hugo echo_read resource enum");
                    Assert.True(read.InputSchema.Required != null && read.InputSchema.Required.Contains("resource"), "resource is required");
                    McpTestTool write = hugoTools.Find("echo_write") ?? throw new TestAssertionException("echo_write missing for Hugo.");
                    Assert.Equal("echo-rw,echo-rwa", String.Join(",", write.ResourceEnum), "write tools exclude the ReadOnly binding");
                    Assert.True(hugoTools.Find("echo_model") == null, "RequiresModel tools are hidden without an inference endpoint");

                    McpTestTool opsRead = opsTools.Find("echo_read") ?? throw new TestAssertionException("echo_read missing for Ops.");
                    Assert.Equal("echo-ops", String.Join(",", opsRead.ResourceEnum), "Ops echo_read resource enum");
                    Assert.True(opsTools.Find("echo_write") == null && opsTools.Find("echo_delete") == null, "ReadOnly profile hides Write and Destructive tools: " + String.Join(", ", opsTools.Names));
                    Assert.True(opsTools.Find("echo_classify") != null, "per-call classified tools stay listed on ReadOnly");

                    foreach (string meta in new[] { "hugo_whoami", "hugo_get_instructions", "hugo_list_resources", "hugo_describe_resource", "hugo_list_approvals", "hugo_await_approval" })
                    {
                        Assert.True(hugoTools.Find(meta) != null && opsTools.Find(meta) != null, meta + " listed");
                    }
                    Assert.True(!hugoTools.Names.Any(n => n.StartsWith("hugo_admin_", StringComparison.Ordinal)), "management tools hidden by default");
                    Assert.True(hugoTools.Find("ask_agent_smith") == null, "ask_agent_smith hidden without a delegation service and endpoint");
                    Assert.Equal("echo-ro,echo-rw,echo-rwa", String.Join(",", (hugoTools.Find("hugo_describe_resource") ?? new McpTestTool()).ResourceEnum), "describe_resource resource enum");

                    // Every listed tool is mapped in the authorization registry.
                    foreach (string name in hugoTools.Names.Concat(opsTools.Names)) Assert.True(server.Registry.IsMapped(name), name + " is listed but not mapped");
                    foreach (McpToolAuthorization entry in server.Registry.All()) Assert.True(fx.Rig.Context.Scopes.FindProtocolOperation("MCP", entry.Name) != null, entry.Name + " has no operation scope");
                }
            }
        }

        private static async Task CallsAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (McpFixture fx = await McpFixture.StartAsync(provider, null, token).ConfigureAwait(false))
            using (McpTestClient client = await fx.ConnectAsync(McpFixture.Headers(fx.HugoCredential), token).ConfigureAwait(false))
            {
                McpTestToolResult read = await client.CallToolAsync("echo_read", new { resource = "echo-ro", text = "hello mcp" }, token).ConfigureAwait(false);
                Assert.True(read.IsError != true, "read succeeded: " + read.Text);
                Assert.True(read.Text.StartsWith("hello mcp", StringComparison.Ordinal), "echo text: " + read.Text);

                McpTestToolResult denied = await client.CallToolAsync("echo_write", new { resource = "echo-ro", text = "x" }, token).ConfigureAwait(false);
                Assert.True(denied.IsError == true && denied.Text.StartsWith("readonly_binding:", StringComparison.Ordinal), "write on ReadOnly: " + denied.Text);
                Assert.True(denied.StructuredJson.Contains("\"error\":\"readonly_binding\"", StringComparison.Ordinal), "structured error code: " + denied.StructuredJson);

                McpTestToolResult invalid = await client.CallToolAsync("echo_read", new { resource = "echo-ro", nope = 1 }, token).ConfigureAwait(false);
                Assert.True(invalid.IsError == true && invalid.Text.StartsWith("invalid_argument:", StringComparison.Ordinal), "invalid arguments: " + invalid.Text);
                McpTestToolResult unbound = await client.CallToolAsync("echo_read", new { resource = "echo-ops", text = "x" }, token).ConfigureAwait(false);
                Assert.True(unbound.IsError == true && unbound.Text.StartsWith("binding_not_found:", StringComparison.Ordinal), "another profile's resource: " + unbound.Text);

                McpTestToolResult written = await client.CallToolAsync("echo_write", new { resource = "echo-rw", text = "stored" }, token).ConfigureAwait(false);
                Assert.True(written.IsError != true && written.Text.Contains("stored", StringComparison.Ordinal), "write on ReadWrite: " + written.Text);

                McpTestToolResult who = await client.CallToolAsync("hugo_whoami", null, token).ConfigureAwait(false);
                Assert.True(who.StructuredJson.Contains("\"personaName\":\"Agent Smith\"", StringComparison.Ordinal) && who.StructuredJson.Contains("\"profileSource\":\"Credential\"", StringComparison.Ordinal), "whoami: " + who.StructuredJson);
                McpTestToolResult instructions = await client.CallToolAsync("hugo_get_instructions", null, token).ConfigureAwait(false);
                Assert.True(instructions.Text.Contains("Mr. Anderson", StringComparison.Ordinal), "get_instructions");
                McpTestToolResult resources = await client.CallToolAsync("hugo_list_resources", null, token).ConfigureAwait(false);
                Assert.True(resources.StructuredJson.Contains("\"alias\":\"echo-rwa\"", StringComparison.Ordinal) && resources.StructuredJson.Contains("ReadWriteWithApproval", StringComparison.Ordinal), "list_resources: " + resources.StructuredJson);
                McpTestToolResult described = await client.CallToolAsync("hugo_describe_resource", new { resource = "echo-ro" }, token).ConfigureAwait(false);
                Assert.True(described.Text.Contains("Echo resources repeat what you send", StringComparison.Ordinal) && described.Text.Contains("echo_read", StringComparison.Ordinal), "describe_resource: " + described.Text);
                McpTestToolResult unknown = await client.CallToolAsync("hugo_describe_resource", new { resource = "echo-ops" }, token).ConfigureAwait(false);
                Assert.True(unknown.IsError == true && unknown.Text.StartsWith("not_found:", StringComparison.Ordinal), "describe unknown alias: " + unknown.Text);

                JsonRpcResponse missing = await client.RawAsync("tools/call", new Dictionary<string, object?> { { "name", "no_such_tool" }, { "arguments", new Dictionary<string, object?>() } }, token).ConfigureAwait(false);
                Assert.True(missing.Error != null, "an unknown tool name is a protocol error");

                List<ToolInvocation> recorded = (await fx.Rig.Context.Database.ToolInvocations.EnumerateAsync(fx.Rig.TenantId, new Hugo.Core.Enumeration.EnumerationQuery { MaxResults = 100 }, new ToolInvocationFilter { Surface = InvocationSurfaceEnum.Mcp }, token).ConfigureAwait(false)).Objects;
                Assert.True(recorded.Count >= 3, "MCP calls are recorded with surface Mcp: " + recorded.Count);
            }
        }

        private static async Task ApprovalAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (McpFixture fx = await McpFixture.StartAsync(provider, s =>
            {
                s.Approvals.McpWaitSeconds = 3;
                s.Mcp.ProgressIntervalSeconds = 1;
            }, token).ConfigureAwait(false))
            using (McpTestClient client = await fx.ConnectAsync(McpFixture.Headers(fx.HugoCredential), token).ConfigureAwait(false))
            {
                McpTestToolResult pending = await client.CallToolAsync("echo_write", new { resource = "echo-rwa", text = "approved text" }, token, "progress-1").ConfigureAwait(false);
                Assert.True(pending.IsError != true, "pending is not an error: " + pending.Text);
                McpTestPending structured = Serializer.Deserialize<McpTestPending>(pending.StructuredJson) ?? new McpTestPending();
                Assert.Equal("approval_pending", structured.Status, "pending status: " + pending.StructuredJson);
                Assert.True(!String.IsNullOrEmpty(structured.ApprovalId) && structured.ApprovalId!.StartsWith("apr_", StringComparison.Ordinal), "approval id");
                Assert.True(structured.Next != null && structured.Next.Contains("hugo_await_approval", StringComparison.Ordinal), "next step: " + structured.Next);
                int progress = client.Notifications.Count(n => n.Method == "notifications/progress");
                Assert.True(progress >= 2, "progress notifications while waiting: " + progress);

                McpTestToolResult listed = await client.CallToolAsync("hugo_list_approvals", new { status = "Pending" }, token).ConfigureAwait(false);
                Assert.True(listed.StructuredJson.Contains(structured.ApprovalId!, StringComparison.Ordinal), "list_approvals shows the pending approval");
                McpTestListing pendingResource = await client.CallAsync<McpTestListing>("resources/read", new Dictionary<string, object?> { { "uri", "hugo://approvals/pending" } }, token).ConfigureAwait(false);
                Assert.True(pendingResource.Contents != null && pendingResource.Contents.Any(c => (c.Text ?? String.Empty).Contains(structured.ApprovalId!, StringComparison.Ordinal)), "hugo://approvals/pending");

                // Still pending: hugo_await_approval returns approval_pending again after its wait.
                McpTestToolResult still = await client.CallToolAsync("hugo_await_approval", new { approvalId = structured.ApprovalId, waitSeconds = 1 }, token).ConfigureAwait(false);
                Assert.True(still.IsError != true && still.StructuredJson.Contains("approval_pending", StringComparison.Ordinal), "still pending: " + still.Text);

                await fx.Rig.DecideAsync(structured.ApprovalId!, "approve", token).ConfigureAwait(false);
                McpTestToolResult final = await client.CallToolAsync("hugo_await_approval", new { approvalId = structured.ApprovalId }, token).ConfigureAwait(false);
                Assert.True(final.IsError != true && final.Text.Contains("approved text", StringComparison.Ordinal), "approved result: " + final.Text);

                // A rejected call comes back as an isError result.
                McpTestToolResult second = await client.CallToolAsync("echo_write", new { resource = "echo-rwa", text = "rejected text" }, token).ConfigureAwait(false);
                McpTestPending secondPending = Serializer.Deserialize<McpTestPending>(second.StructuredJson) ?? new McpTestPending();
                await fx.Rig.DecideAsync(secondPending.ApprovalId!, "reject", token).ConfigureAwait(false);
                McpTestToolResult rejected = await client.CallToolAsync("hugo_await_approval", new { approvalId = secondPending.ApprovalId }, token).ConfigureAwait(false);
                Assert.True(rejected.IsError == true && rejected.Text.StartsWith("rejected:", StringComparison.Ordinal), "rejected: " + rejected.Text);
            }
        }

        private static async Task IsolationAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (McpFixture fx = await McpFixture.StartAsync(provider, s => s.Approvals.McpWaitSeconds = 0, token).ConfigureAwait(false))
            {
                // A pending approval in the default tenant.
                string approvalId;
                using (McpTestClient owner = await fx.ConnectAsync(McpFixture.Headers(fx.HugoCredential), token).ConfigureAwait(false))
                {
                    McpTestToolResult pending = await owner.CallToolAsync("echo_write", new { resource = "echo-rwa", text = "x" }, token).ConfigureAwait(false);
                    approvalId = (Serializer.Deserialize<McpTestPending>(pending.StructuredJson) ?? new McpTestPending()).ApprovalId ?? throw new TestAssertionException("no approval: " + pending.Text);
                }

                // A user of another tenant.
                Tenant other = await HttpTestHelpers.SendAsync<Tenant>(fx.Rig.Host.AdminClient, HttpMethod.Post, "/v1.0/api/tenants", new TenantRequest { Name = "Foreign" }, 201, token).ConfigureAwait(false);
                await HttpTestHelpers.SendAsync<User>(fx.Rig.Host.AdminClient, HttpMethod.Post, "/v1.0/api/tenants/" + other.Id + "/users", new UserRequest { Email = "foreign@mcp.local", Password = "password123", IsTenantAdmin = true }, 201, token).ConfigureAwait(false);
                TokenResponse session = await fx.Rig.Host.LoginAsync("foreign@mcp.local", "password123", other.Id, token).ConfigureAwait(false);
                Dictionary<string, string> foreign = new Dictionary<string, string> { { "Authorization", "Bearer " + session.Token } };

                McpRawResponse byId = await fx.RawAsync("/mcp/" + fx.Rig.Hugo.Id, foreign, McpFixture.InitializeBody(), token).ConfigureAwait(false);
                Assert.Equal(404, byId.StatusCode, "another tenant's profile id");
                McpRawResponse ops = await fx.RawAsync("/mcp/Ops", foreign, McpFixture.InitializeBody(), token).ConfigureAwait(false);
                Assert.Equal(404, ops.StatusCode, "another tenant's profile name");
                using (McpTestClient client = await fx.ConnectAsync(foreign, token).ConfigureAwait(false))
                {
                    McpTestToolList tools = await client.ListToolsAsync(token).ConfigureAwait(false);
                    Assert.True(tools.Tools.All(t => !t.ResourceEnum.Any(a => a.StartsWith("echo-", StringComparison.Ordinal))), "no foreign aliases");
                    McpTestToolResult read = await client.CallToolAsync("echo_read", new { resource = "echo-ro", text = "x" }, token).ConfigureAwait(false);
                    Assert.True(read.IsError == true && read.Text.StartsWith("binding_not_found:", StringComparison.Ordinal), "foreign resource call: " + read.Text);
                    McpTestToolResult awaited = await client.CallToolAsync("hugo_await_approval", new { approvalId = approvalId, waitSeconds = 0 }, token).ConfigureAwait(false);
                    Assert.True(awaited.IsError == true && awaited.Text.StartsWith("not_found:", StringComparison.Ordinal), "foreign approval: " + awaited.Text);
                    McpTestToolResult list = await client.CallToolAsync("hugo_list_approvals", null, token).ConfigureAwait(false);
                    Assert.True(!list.StructuredJson.Contains(approvalId, StringComparison.Ordinal), "foreign approvals are not listed");
                }

                // The profile lock: a credential locked to Hugo cannot act as Ops by path or header.
                McpRawResponse locked = await fx.RawAsync("/mcp/Ops", McpFixture.Headers(fx.HugoCredential), McpFixture.InitializeBody(), token).ConfigureAwait(false);
                Assert.Equal(403, locked.StatusCode, "locked credential on /mcp/Ops");
                Assert.True(locked.Body.Contains("ProfileLocked", StringComparison.Ordinal), "ProfileLocked: " + locked.Body);
                McpRawResponse header = await fx.RawAsync("/mcp", WithHeader(McpFixture.Headers(fx.HugoCredential), "x-hugo-profile", "Ops"), McpFixture.InitializeBody(), token).ConfigureAwait(false);
                Assert.Equal(403, header.StatusCode, "locked credential with x-hugo-profile");
                McpRawResponse own = await fx.RawAsync("/mcp/Hugo", McpFixture.Headers(fx.HugoCredential), McpFixture.InitializeBody(), token).ConfigureAwait(false);
                Assert.Equal(200, own.StatusCode, "locked credential on its own profile");

                // A session opened for one profile cannot be reused on another.
                using (McpTestClient anderson = await fx.ConnectAsync(fx.AndersonHeaders(), token, "/mcp/Hugo").ConfigureAwait(false))
                {
                    McpRawResponse reuse = await fx.RawAsync("/mcp/Ops", WithHeader(fx.AndersonHeaders(), "Mcp-Session-Id", anderson.Client.SessionId!), new Dictionary<string, object?> { { "jsonrpc", "2.0" }, { "id", 7 }, { "method", "tools/list" } }, token).ConfigureAwait(false);
                    Assert.Equal(404, reuse.StatusCode, "session of profile Hugo used on Ops");
                }
            }
        }

        private static async Task ManagementAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (McpFixture fx = await McpFixture.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                HugoMcpServer server = fx.Rig.Context.Get<HugoMcpServer>();
                Assert.True(server.Registry.Find("test_unmapped_tool") == null && server.Registry.Find("echo_read") != null, "registry lookups");
                fx.Rig.Context.Get<ToolCatalogService>().AddMetaToolProvider(new UnmappedMetaToolProvider());
                using (McpTestClient client = await fx.ConnectAsync(fx.AndersonHeaders(), token).ConfigureAwait(false))
                {
                    McpTestToolList tools = await client.ListToolsAsync(token).ConfigureAwait(false);
                    Assert.True(tools.Find(UnmappedMetaToolProvider.ToolName) == null, "an unmapped tool is never listed");
                    McpTestToolResult refused = await client.CallToolAsync(UnmappedMetaToolProvider.ToolName, null, token).ConfigureAwait(false);
                    Assert.True(refused.IsError == true && refused.Text.StartsWith("unmapped_tool:", StringComparison.Ordinal), "unmapped tool refused: " + refused.Text);

                    Assert.True(!tools.Names.Any(n => n.StartsWith("hugo_admin_", StringComparison.Ordinal)), "management hidden without expose_management_tools");
                    McpTestToolResult gated = await client.CallToolAsync("hugo_admin_list_resources", null, token).ConfigureAwait(false);
                    Assert.True(gated.IsError == true && gated.Text.StartsWith("forbidden:", StringComparison.Ordinal), "management call without exposure: " + gated.Text);
                }

                await HttpTestHelpers.SendAsync<AgentProfile>(fx.Rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + fx.Rig.Hugo.Id, new { exposeManagementTools = true }, 200, token).ConfigureAwait(false);
                User operatorUser = await HttpTestHelpers.SendAsync<User>(fx.Rig.Host.AdminClient, HttpMethod.Post, "/v1.0/api/tenants/" + fx.Rig.TenantId + "/users", new UserRequest { Email = "operator@mcp.local", Password = "password123" }, 201, token).ConfigureAwait(false);
                await HttpTestHelpers.SendAsync<UserRoleAssignment>(fx.Rig.Host.AdminClient, HttpMethod.Post, "/v1.0/api/tenants/" + fx.Rig.TenantId + "/user-role-assignments", new AssignmentRequest { UserId = operatorUser.Id, RoleName = "Operator" }, 201, token).ConfigureAwait(false);
                TokenResponse operatorSession = await fx.Rig.Host.LoginAsync("operator@mcp.local", "password123", fx.Rig.TenantId, token).ConfigureAwait(false);
                using (McpTestClient admin = await fx.ConnectAsync(fx.AndersonHeaders(), token).ConfigureAwait(false))
                using (McpTestClient op = await fx.ConnectAsync(new Dictionary<string, string> { { "Authorization", "Bearer " + operatorSession.Token } }, token, "/mcp/Hugo").ConfigureAwait(false))
                {
                    McpTestToolList adminTools = await admin.ListToolsAsync(token).ConfigureAwait(false);
                    foreach (string name in new[] { "hugo_admin_list_profiles", "hugo_admin_describe_profile", "hugo_admin_list_resources", "hugo_admin_describe_resource", "hugo_admin_list_bindings", "hugo_admin_create_resource", "hugo_admin_update_binding" })
                    {
                        Assert.True(adminTools.Find(name) != null, name + " listed for an Admin-capable principal");
                    }
                    McpTestToolList opTools = await op.ListToolsAsync(token).ConfigureAwait(false);
                    Assert.True(!opTools.Names.Any(n => n.StartsWith("hugo_admin_", StringComparison.Ordinal)), "management hidden from an Operator credential");
                    CredentialCreateResponse operatorCredential = await McpFixture.CredentialAsync(fx.Rig, "operator-mcp", fx.Rig.Hugo.Id, "Operator", token).ConfigureAwait(false);
                    using (McpTestClient scoped = await fx.ConnectAsync(McpFixture.Headers(operatorCredential), token).ConfigureAwait(false))
                    {
                        McpTestToolList scopedTools = await scoped.ListToolsAsync(token).ConfigureAwait(false);
                        Assert.True(!scopedTools.Names.Any(n => n.StartsWith("hugo_admin_", StringComparison.Ordinal)), "management hidden from a credential scoped to Operator: " + String.Join(", ", scopedTools.Names));
                    }
                    McpTestToolResult opCall = await op.CallToolAsync("hugo_admin_list_profiles", null, token).ConfigureAwait(false);
                    Assert.True(opCall.IsError == true && opCall.Text.StartsWith("forbidden:", StringComparison.Ordinal), "Operator management call: " + opCall.Text);

                    McpTestToolResult profiles = await admin.CallToolAsync("hugo_admin_list_profiles", null, token).ConfigureAwait(false);
                    Assert.True(profiles.StructuredJson.Contains("\"name\":\"Ops\"", StringComparison.Ordinal), "list_profiles: " + profiles.Text);
                    McpTestToolResult profile = await admin.CallToolAsync("hugo_admin_describe_profile", new { profile = "Hugo" }, token).ConfigureAwait(false);
                    Assert.True(profile.StructuredJson.Contains("\"alias\":\"echo-rwa\"", StringComparison.Ordinal), "describe_profile with bindings: " + profile.Text);
                    McpTestToolResult created = await admin.CallToolAsync("hugo_admin_create_resource", new { name = "echo-new", connectorType = "echo", environment = "test", settings = new { greeting = "hi" } }, token).ConfigureAwait(false);
                    Assert.True(created.IsError != true && created.Text.Contains("echo-new", StringComparison.Ordinal), "create_resource: " + created.Text);
                    McpTestToolResult invalid = await admin.CallToolAsync("hugo_admin_create_resource", new { name = "bad", connectorType = "echo", settings = new { mode = "invalid" } }, token).ConfigureAwait(false);
                    Assert.True(invalid.IsError == true, "invalid settings are a tool error: " + invalid.Text);
                    McpTestToolResult resource = await admin.CallToolAsync("hugo_admin_describe_resource", new { resource = "echo-ro" }, token).ConfigureAwait(false);
                    Assert.True(resource.StructuredJson.Contains("\"referrers\"", StringComparison.Ordinal), "describe_resource (admin): " + resource.Text);
                    McpTestToolResult resources = await admin.CallToolAsync("hugo_admin_list_resources", new { connectorType = "echo" }, token).ConfigureAwait(false);
                    Assert.True(resources.StructuredJson.Contains("echo-new", StringComparison.Ordinal), "list_resources (admin)");
                    McpTestToolResult bindings = await admin.CallToolAsync("hugo_admin_list_bindings", new { profile = "Ops" }, token).ConfigureAwait(false);
                    Assert.True(bindings.StructuredJson.Contains("echo-ops", StringComparison.Ordinal), "list_bindings");
                    McpTestToolResult updated = await admin.CallToolAsync("hugo_admin_update_binding", new { profile = "Ops", binding = "echo-ops", accessMode = "ReadWrite" }, token).ConfigureAwait(false);
                    Assert.True(updated.IsError != true && updated.Text.Contains("ReadWrite", StringComparison.Ordinal), "update_binding: " + updated.Text);
                    List<Binding> opsBindings = await HttpTestHelpers.SendAsync<List<Binding>>(fx.Rig.Anderson, HttpMethod.Get, "/v1.0/api/agent-profiles/" + fx.Ops.Id + "/bindings", null, 200, token).ConfigureAwait(false);
                    Assert.Equal(AccessModeEnum.ReadWrite, opsBindings.Single().AccessMode, "binding updated through MCP");
                }
            }
        }

        private static async Task PromptsAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (McpFixture fx = await McpFixture.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                string database = Path.Combine(Path.GetTempPath(), "hugo-mcp-" + Guid.NewGuid().ToString("N") + ".db");
                try
                {
                    using (Microsoft.Data.Sqlite.SqliteConnection connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + database))
                    {
                        await connection.OpenAsync(token).ConfigureAwait(false);
                        using (Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand())
                        {
                            command.CommandText = "CREATE TABLE orders (id INTEGER PRIMARY KEY, total REAL); INSERT INTO orders (total) VALUES (1.5), (2.5);";
                            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                        }
                    }
                    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                    Resource shop = await fx.Rig.ResourceAsync("shop-db", token, "sqlite", new Dictionary<string, object> { { "filename", database } }).ConfigureAwait(false);
                    await fx.Rig.BindAsync(fx.Rig.Hugo.Id, shop.Id, AccessModeEnum.ReadOnly, token).ConfigureAwait(false);
                    await HttpTestHelpers.SendAsync<AgentProfile>(fx.Rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + fx.Rig.Hugo.Id, new { instructionDetail = "Compact" }, 200, token).ConfigureAwait(false);

                    using (McpTestClient client = await fx.ConnectAsync(McpFixture.Headers(fx.HugoCredential), token).ConfigureAwait(false))
                    {
                        string fullOnly = "Hugo does more than run SQL here";
                        McpTestToolResult instructions = await client.CallToolAsync("hugo_get_instructions", null, token).ConfigureAwait(false);
                        Assert.True(!instructions.Text.Contains(fullOnly, StringComparison.Ordinal), "compact instructions omit the full SQLite text");
                        Assert.True(instructions.Text.Contains("hugo_describe_resource", StringComparison.Ordinal), "compact instructions point at hugo_describe_resource");
                        McpTestToolResult described = await client.CallToolAsync("hugo_describe_resource", new { resource = "shop-db" }, token).ConfigureAwait(false);
                        Assert.True(described.Text.Contains(fullOnly, StringComparison.Ordinal), "describe_resource returns the full SQLite instructions: " + described.Text.Substring(0, Math.Min(400, described.Text.Length)));
                        Assert.True(described.Text.Contains("sql_list_tables", StringComparison.Ordinal), "describe_resource lists usable tools");
                        McpTestListing read = await client.CallAsync<McpTestListing>("resources/read", new Dictionary<string, object?> { { "uri", "hugo://resources/shop-db" } }, token).ConfigureAwait(false);
                        Assert.True(read.Contents != null && read.Contents.Any(c => (c.Text ?? String.Empty).Contains(fullOnly, StringComparison.Ordinal)), "hugo://resources/shop-db returns the full instructions");

                        McpTestToolResult tables = await client.CallToolAsync("sql_list_tables", new { resource = "shop-db" }, token).ConfigureAwait(false);
                        Assert.True(tables.IsError != true && tables.Text.Contains("orders", StringComparison.Ordinal), "sql_list_tables: " + tables.Text);

                        McpTestListing prompts = await client.CallAsync<McpTestListing>("prompts/list", null, token).ConfigureAwait(false);
                        Assert.Equal(3, (prompts.Prompts ?? new List<McpTestContent>()).Count, "three prompts listed");
                        McpTestListing briefing = await client.CallAsync<McpTestListing>("prompts/get", new Dictionary<string, object?> { { "name", "hugo_briefing" } }, token).ConfigureAwait(false);
                        Assert.True(briefing.Messages != null && briefing.Messages.Any(m => (m.Content?.Text ?? String.Empty).Contains("Agent Smith", StringComparison.Ordinal)), "hugo_briefing");
                        McpTestListing investigate = await client.CallAsync<McpTestListing>("prompts/get", new Dictionary<string, object?> { { "name", "hugo_investigate" }, { "arguments", new Dictionary<string, string> { { "symptom", "checkout is slow" } } } }, token).ConfigureAwait(false);
                        Assert.True(investigate.Messages != null && investigate.Messages.Any(m => (m.Content?.Text ?? String.Empty).Contains("Investigate checkout is slow", StringComparison.Ordinal)), "hugo_investigate");
                        McpTestListing sweep = await client.CallAsync<McpTestListing>("prompts/get", new Dictionary<string, object?> { { "name", "hugo_health_sweep" } }, token).ConfigureAwait(false);
                        Assert.True(sweep.Messages != null && sweep.Messages.Any(m => (m.Content?.Text ?? String.Empty).Contains("shop-db", StringComparison.Ordinal)), "hugo_health_sweep lists resources");

                        McpTestListing resources = await client.CallAsync<McpTestListing>("resources/list", null, token).ConfigureAwait(false);
                        List<string> uris = (resources.Resources ?? new List<McpTestContent>()).Select(r => r.Uri ?? String.Empty).ToList();
                        foreach (string uri in new[] { "hugo://profile/instructions", "hugo://approvals/pending", "hugo://resources/shop-db", "hugo://resources/echo-ro" }) Assert.True(uris.Contains(uri), "resources/list includes " + uri + ": " + String.Join(", ", uris));
                        McpTestListing profileInstructions = await client.CallAsync<McpTestListing>("resources/read", new Dictionary<string, object?> { { "uri", "hugo://profile/instructions" } }, token).ConfigureAwait(false);
                        Assert.True(profileInstructions.Contents != null && profileInstructions.Contents.Any(c => (c.Text ?? String.Empty).Contains("Mr. Anderson", StringComparison.Ordinal)), "hugo://profile/instructions");
                        JsonRpcResponse unknown = await client.RawAsync("resources/read", new Dictionary<string, object?> { { "uri", "hugo://resources/echo-ops" } }, token).ConfigureAwait(false);
                        Assert.True(unknown.Error != null, "another profile's resource is not readable");
                    }
                }
                finally
                {
                    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                    try
                    {
                        File.Delete(database);
                    }
                    catch (IOException)
                    {
                    }
                }
            }
        }

        private static async Task StatelessAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (McpFixture fx = await McpFixture.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                using (McpTestClient client = await McpTestClient.ConnectStatelessAsync(fx.BaseUrl, "/mcp", McpFixture.Headers(fx.HugoCredential), token).ConfigureAwait(false))
                {
                    Assert.True(McpProtocol.GetEra(client.Client.ProtocolVersion) == McpProtocolEra.Stateless, "stateless revision negotiated: " + client.Client.ProtocolVersion);
                    McpDiscoverResult discover = await client.Client.DiscoverAsync(token).ConfigureAwait(false);
                    Assert.True((discover.Instructions ?? String.Empty).Contains("Agent Smith", StringComparison.Ordinal) && (discover.Instructions ?? String.Empty).Contains("echo-rwa", StringComparison.Ordinal), "server/discover instructions");
                    McpTestToolList tools = await client.ListToolsAsync(token).ConfigureAwait(false);
                    Assert.Equal("echo-ro,echo-rw,echo-rwa", String.Join(",", (tools.Find("echo_read") ?? new McpTestTool()).ResourceEnum), "stateless tools/list is per caller");
                    McpTestToolResult read = await client.CallToolAsync("echo_read", new { resource = "echo-rw", text = "stateless" }, token).ConfigureAwait(false);
                    Assert.True(read.IsError != true && read.Text.StartsWith("stateless", StringComparison.Ordinal), "stateless call: " + read.Text);
                    Assert.True(String.IsNullOrEmpty(client.Client.SessionId), "no session in the stateless revision");
                }

                // ask_agent_smith: hidden without a delegation service, listed and callable with one and an endpoint.
                StubDelegationService stub = new StubDelegationService();
                fx.Rig.Context.AgentDelegation = stub;
                using (McpTestClient client = await fx.ConnectAsync(McpFixture.Headers(fx.HugoCredential), token).ConfigureAwait(false))
                {
                    Assert.True((await client.ListToolsAsync(token).ConfigureAwait(false)).Find("ask_agent_smith") == null, "ask_agent_smith hidden without an inference endpoint");
                    ModelEndpoint endpoint = await HttpTestHelpers.SendAsync<ModelEndpoint>(fx.Rig.Anderson, HttpMethod.Post, "/v1.0/api/model-endpoints", new { name = "stub", provider = "OpenAICompatible", baseUrl = "http://127.0.0.1:9/v1", model = "m" }, 201, token).ConfigureAwait(false);
                    await HttpTestHelpers.SendAsync<AgentProfile>(fx.Rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + fx.Rig.Hugo.Id, new { inferenceEndpointId = endpoint.Id }, 200, token).ConfigureAwait(false);
                    McpTestToolList tools = await client.ListToolsAsync(token).ConfigureAwait(false);
                    Assert.True(tools.Find("ask_agent_smith") != null, "ask_agent_smith listed");
                    Assert.True(tools.Find("echo_model") != null, "RequiresModel tools listed once the profile has an endpoint");
                    McpTestToolResult answer = await client.CallToolAsync("ask_agent_smith", new { message = "what is the answer" }, token).ConfigureAwait(false);
                    Assert.True(answer.IsError != true && answer.Text.Contains("42", StringComparison.Ordinal) && answer.Text.Contains("Tool trace", StringComparison.Ordinal), "ask_agent_smith: " + answer.Text);
                    Assert.True(answer.StructuredJson.Contains("\"conversationId\":\"cnv_stub\"", StringComparison.Ordinal), "conversation id: " + answer.StructuredJson);
                    Assert.Equal("what is the answer", stub.LastMessage, "message delegated");
                }
                fx.Rig.Context.AgentDelegation = null;
            }
        }

        private static T Result<T>(string body) where T : class
        {
            McpTestEnvelope envelope = Serializer.Deserialize<McpTestEnvelope>(body) ?? throw new TestAssertionException("Not a JSON-RPC message: " + body);
            if (envelope.Error != null) throw new TestAssertionException("JSON-RPC error " + envelope.Error.Code + ": " + envelope.Error.Message);
            if (!envelope.Result.HasValue) throw new TestAssertionException("No result: " + body);
            return Serializer.Deserialize<T>(envelope.Result.Value.GetRawText()) ?? throw new TestAssertionException("Unreadable result: " + body);
        }

        private static Dictionary<string, string> WithHeader(Dictionary<string, string> headers, string name, string value)
        {
            Dictionary<string, string> copy = new Dictionary<string, string>(headers);
            copy[name] = value;
            return copy;
        }

        #endregion
    }
}
