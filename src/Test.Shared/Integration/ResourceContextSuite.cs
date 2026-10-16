namespace Test.Shared.Integration
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Agent;
    using Hugo.Core.Database;
    using Hugo.Core.Enums;
    using Hugo.Core.Instructions;
    using Hugo.Core.Models;
    using Hugo.Core.Responses;
    using Hugo.Core.Security;
    using Test.Shared.Agent.StubLlm;
    using Test.Shared.Infrastructure;
    using Test.Shared.Integration.Mcp;
    using Touchstone.Core;

    /// <summary>
    /// Durable resource learning over the real HTTP, MCP, and internal agent pipelines. Targets are never contacted;
    /// SQLite connector interoperability uses an in-memory local database, and model requests use an HTTP test server.
    /// </summary>
    public static class ResourceContextSuite
    {
        /// <summary>
        /// Build the resource-context suites for every configured persistence provider.
        /// </summary>
        /// <returns>Provider suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            List<TestSuiteDescriptor> suites = new List<TestSuiteDescriptor>();
            foreach (DatabaseTypeEnum provider in TestEnvironment.Providers())
            {
                string id = "resource-context-" + provider.ToString().ToLowerInvariant();
                List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(id, "ssh-version-cache", "SSH paths and service facts persist with versions, operator preservation, audit, and compiler invalidation", ct => SshVersionCacheAsync(provider, ct)),
                    new TestCaseDescriptor(id, "sql-shared-store", "Generic and SQL context tools read and append to the same durable store", ct => SqlSharedStoreAsync(provider, ct)),
                    new TestCaseDescriptor(id, "isolation-permissions", "Resource, tenant, credential, binding, and tool-rule boundaries still apply to learning", ct => IsolationAsync(provider, ct)),
                    new TestCaseDescriptor(id, "secret-bounds", "Credential-shaped, oversized, and row-dump context is refused without corrupting operator notes", ct => BoundsAsync(provider, ct)),
                    new TestCaseDescriptor(id, "restart-new-profile", "A new profile and conversation recover saved SSH context after the server restarts", ct => RestartAsync(provider, ct)),
                    new TestCaseDescriptor(id, "mcp-plugin-discovery", "MCP exposes ordinary resource-context tools for SSH and plugin connectors, respecting disabled updates", ct => McpAsync(provider, ct)),
                    new TestCaseDescriptor(id, "same-turn-fresh-context", "The next model planning request sees newly saved context before selecting an operational tool", ct => SameTurnAsync(provider, ct)),
                    new TestCaseDescriptor(id, "nested-model-fresh-context", "Direct connector model calls receive current resource context before every nested completion", ct => NestedModelAsync(provider, ct)),
                    new TestCaseDescriptor(id, "concurrent-cas-history", "Concurrent versioned writes and deletes have exactly one winner and no duplicate history", ct => ConcurrentCasAsync(provider, ct))
                };
                suites.Add(new TestSuiteDescriptor(id, "General resource context (" + provider + ")", cases));
            }
            return suites;
        }

        private static async Task<Resource> SshAsync(IntegrationRig rig, string name, CancellationToken token)
        {
            Secret password = await rig.SecretAsync(name + "-password", SecretKindEnum.Password, new Dictionary<string, string> { { "password", "context-fixture-credential-63H8s" } }, token).ConfigureAwait(false);
            return await HttpTestHelpers.SendAsync<Resource>(rig.Anderson, HttpMethod.Post, "/v1.0/api/resources", new
            {
                name,
                connectorType = "ssh",
                notes = "Operator inventory: this host belongs to the checkout service.",
                settings = new { host = "127.0.0.1", port = 9, username = "context-fixture", authMethod = "Password" },
                secretBindings = new[] { new { slotName = "password", secretId = password.Id } }
            }, 201, token).ConfigureAwait(false);
        }

        private static async Task<CompiledInstructions> CompiledAsync(HttpClient client, string profile, CancellationToken token)
        {
            return await HttpTestHelpers.SendAsync<CompiledInstructions>(client, HttpMethod.Get, "/v1.0/api/agent-profiles/" + profile + "/compiled-instructions?surface=Chat", null, 200, token).ConfigureAwait(false);
        }

        private static void Succeeded(ToolInvocationOutcome outcome, string purpose)
        {
            Assert.Equal(InvocationStatusEnum.Succeeded, outcome.Status, purpose + ": " + outcome.Result?.Text);
            Assert.True(outcome.Result != null && !outcome.Result.IsError, purpose + " returned a successful result");
        }

        private static async Task SshVersionCacheAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                Resource resource = await SshAsync(rig, "web-learning", token).ConfigureAwait(false);
                await rig.BindAsync(rig.Hugo.Id, resource.Id, AccessModeEnum.ReadOnly, token).ConfigureAwait(false);
                ResourceKnowledge authored = await HttpTestHelpers.SendAsync<ResourceKnowledge>(rig.Anderson, HttpMethod.Put, "/v1.0/api/resources/" + resource.Id + "/knowledge/host/routes", new { content = "Operator route: retain the blue deployment." }, 200, token).ConfigureAwait(false);
                Assert.Equal(KnowledgeSourceEnum.Operator, authored.Source, "operator provenance");
                CompiledInstructions before = await CompiledAsync(rig.Anderson, rig.Hugo.Id, token).ConfigureAwait(false);
                Assert.True(before.Text.Contains("retain the blue deployment", StringComparison.Ordinal), "the operator-only compiler snapshot is cached before agent learning");
                Succeeded(await rig.InvokeAsync("resource_update_context", resource.Name, new { scope = "host", key = "routes", content = "Checkout lives at /srv/checkout/current; nginx serves port 8080." }, token).ConfigureAwait(false), "ReadOnly metadata append");
                ResourceKnowledge? appended = await rig.Context.Database.ResourceKnowledge.ReadAsync(rig.TenantId, resource.Id, "host", "routes", token).ConfigureAwait(false);
                Assert.True(appended != null && appended.Content.Contains("retain the blue deployment", StringComparison.Ordinal) && appended.Content.Contains("/srv/checkout/current", StringComparison.Ordinal), "append keeps operator content and adds the path");
                Assert.Equal(KnowledgeSourceEnum.Operator, appended!.Source, "appending cannot downgrade operator provenance");
                Assert.Equal(2, appended.Version, "operator plus append versions");
                ToolInvocationOutcome refused = await rig.InvokeAsync("resource_update_context", resource.Name, new { scope = "host", key = "routes", mode = "replace", content = "Erase operator notes." }, token).ConfigureAwait(false);
                Assert.True(refused.Result != null && refused.Result.IsError && refused.Result.ErrorCode == ConnectorErrorCodes.PermissionDenied, "an agent cannot replace operator-authored context");
                Succeeded(await rig.InvokeAsync("resource_update_context", resource.Name, new { scope = "service", key = "nginx", mode = "replace", content = "nginx configuration is /etc/nginx/nginx.conf", expectedVersion = 0 }, token).ConfigureAwait(false), "new keyed agent fact");
                Succeeded(await rig.InvokeAsync("resource_update_context", resource.Name, new { scope = "service", key = "nginx", mode = "replace", content = "nginx site is /etc/nginx/sites-enabled/checkout", expectedVersion = 1 }, token).ConfigureAwait(false), "versioned agent replace");
                ToolInvocationOutcome stale = await rig.InvokeAsync("resource_update_context", resource.Name, new { scope = "service", key = "nginx", content = "stale overwrite", expectedVersion = 1 }, token).ConfigureAwait(false);
                Assert.True(stale.Result != null && stale.Result.IsError && stale.Result.ErrorCode == "version_conflict", "stale writes must not silently overwrite a concurrent fact");
                Succeeded(await rig.InvokeAsync("resource_update_context", resource.Name, new { content = "Root host summary: checkout is deployed here." }, token).ConfigureAwait(false), "default root fact");
                ToolInvocationOutcome read = await rig.InvokeAsync("resource_get_context", resource.Name, new { scope = "host", key = "routes" }, token).ConfigureAwait(false);
                Succeeded(read, "read keyed SSH fact without an SSH connection");
                Assert.True(read.Result!.Text.Contains("/srv/checkout/current", StringComparison.Ordinal), "read returns the saved path");
                List<ResourceKnowledgeVersion> versions = await rig.Context.Database.ResourceKnowledge.ListVersionsAsync(rig.TenantId, resource.Id, "service", "nginx", token).ConfigureAwait(false);
                Assert.Equal(2, versions.Count, "successful replacements have version history; a stale update adds no version");
                CompiledInstructions after = await CompiledAsync(rig.Anderson, rig.Hugo.Id, token).ConfigureAwait(false);
                Assert.True(!before.Text.Contains("/srv/checkout/current", StringComparison.Ordinal) && after.Text.Contains("/srv/checkout/current", StringComparison.Ordinal), "learning invalidates previously cached instructions");
                Assert.True(after.Text.Contains("sites-enabled/checkout", StringComparison.Ordinal) && after.Text.Contains("Root host summary", StringComparison.Ordinal), "keyed service and root entries both compile");
                Assert.True(after.Text.Contains("Operator inventory", StringComparison.Ordinal) && !after.Text.Contains("context-fixture-credential-63H8s", StringComparison.Ordinal), "resource notes survive and secrets never enter instructions");
                Assert.True((await rig.AuditAsync(AuditEventTypeEnum.KnowledgeChanged, token).ConfigureAwait(false)).Count >= 5, "successful operator and agent updates are audited");
                Resource stored = await HttpTestHelpers.SendAsync<Resource>(rig.Anderson, HttpMethod.Get, "/v1.0/api/resources/" + resource.Id, null, 200, token).ConfigureAwait(false);
                Assert.True(stored.LastTestUtc == null, "metadata operations never probe the target");
                ToolInvocationOutcome operational = await rig.InvokeAsync("ssh_service_control", resource.Name, new { unit = "nginx", action = "restart" }, token, 200, null, null, true).ConfigureAwait(false);
                Assert.Equal(InvocationDecisionEnum.Denied, operational.Decision, "learning does not grant operational writes on ReadOnly");
            }
        }

        private static async Task SqlSharedStoreAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                Resource resource = await rig.ResourceAsync("sql-learning", token, "sqlite", new Dictionary<string, object> { { "filename", ":memory:" } }).ConfigureAwait(false);
                await rig.BindAsync(rig.Hugo.Id, resource.Id, AccessModeEnum.ReadOnly, token).ConfigureAwait(false);
                Succeeded(await rig.InvokeAsync("resource_update_context", resource.Name, new { scope = "database", content = "Checkout totals use minor currency units." }, token).ConfigureAwait(false), "generic SQL context write");
                ToolInvocationOutcome sqlRead = await rig.InvokeAsync("sql_get_context", resource.Name, new { }, token).ConfigureAwait(false);
                Succeeded(sqlRead, "existing SQL context read");
                Assert.True(sqlRead.Result!.Text.Contains("minor currency units", StringComparison.Ordinal), "SQL reads the generic entry rather than a second store");
                Succeeded(await rig.InvokeAsync("sql_update_context", resource.Name, new { content = "Refunds are recorded separately.", mode = "append" }, token).ConfigureAwait(false), "existing SQL context append");
                ToolInvocationOutcome genericRead = await rig.InvokeAsync("resource_get_context", resource.Name, new { scope = "database" }, token).ConfigureAwait(false);
                Succeeded(genericRead, "generic read after SQL write");
                Assert.True(genericRead.Result!.Text.Contains("minor currency units", StringComparison.Ordinal) && genericRead.Result.Text.Contains("Refunds", StringComparison.Ordinal), "both tools share accumulated content");
                List<ResourceKnowledge> entries = await rig.Context.Database.ResourceKnowledge.ListAsync(rig.TenantId, resource.Id, "database", token).ConfigureAwait(false);
                Assert.Equal(1, entries.Count, "one normalized root entry");
                Assert.Equal(2, entries[0].Version, "one version sequence across generic and SQL tools");
                await HttpTestHelpers.SendAsync<ResourceKnowledge>(rig.Anderson, HttpMethod.Put, "/v1.0/api/resources/" + resource.Id + "/knowledge/table/main.orders", new { content = "Existing SQL table notes about checkout order totals." }, 200, token).ConfigureAwait(false);
                ToolInvocationOutcome all = await rig.InvokeAsync("resource_get_context", resource.Name, new { }, token).ConfigureAwait(false);
                Succeeded(all, "default read lists existing SQL context scopes");
                Assert.True(all.Result!.Text.Contains("Refunds", StringComparison.Ordinal) && all.Result.Text.Contains("Existing SQL table notes", StringComparison.Ordinal), "an argument-free generic read includes existing SQL database and table facts");
            }
        }

        private static async Task IsolationAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                Resource first = await SshAsync(rig, "first-host", token).ConfigureAwait(false);
                Resource second = await SshAsync(rig, "second-host", token).ConfigureAwait(false);
                Binding binding = await rig.BindAsync(rig.Hugo.Id, first.Id, AccessModeEnum.ReadOnly, token).ConfigureAwait(false);
                Binding secondBinding = await rig.BindAsync(rig.Hugo.Id, second.Id, AccessModeEnum.ReadOnly, token).ConfigureAwait(false);
                Succeeded(await rig.InvokeAsync("resource_update_context", first.Name, new { content = "Only first-host owns the private checkout path." }, token).ConfigureAwait(false), "first host learning");
                ToolInvocationOutcome different = await rig.InvokeAsync("resource_get_context", second.Name, new { }, token).ConfigureAwait(false);
                Assert.True(different.Result != null && !different.Result.IsError && !different.Result.Text.Contains("private checkout path", StringComparison.Ordinal), "another resource cannot retrieve the first host's context");
                Assert.True(await rig.Context.Database.ResourceKnowledge.ReadAsync("tenant-other", first.Id, "context", "_root", token).ConfigureAwait(false) == null, "tenant filtering applies to knowledge reads");
                Tenant tenant = await rig.Context.Database.Tenants.CreateAsync(new Tenant { Name = "isolated-context-tenant" }, token).ConfigureAwait(false);
                Resource foreign = await rig.Context.Database.Resources.CreateAsync(new Resource { TenantId = tenant.Id, Name = "foreign-host", ConnectorType = "ssh" }, token).ConfigureAwait(false);
                await rig.Context.Database.ResourceKnowledge.UpsertAsync(new ResourceKnowledge { TenantId = tenant.Id, ResourceId = foreign.Id, Scope = "context", Content = "foreign-only-fact" }, token).ConfigureAwait(false);
                TestResponse hidden = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Get, "/v1.0/api/resources/" + foreign.Id + "/knowledge/context", null, token).ConfigureAwait(false);
                Assert.Equal(404, hidden.StatusCode, "foreign resource routes hide its existence");
                CompiledInstructions instructions = await CompiledAsync(rig.Anderson, rig.Hugo.Id, token).ConfigureAwait(false);
                Assert.True(!instructions.Text.Contains("foreign-only-fact", StringComparison.Ordinal), "compiler cannot import another tenant's context");
                CredentialCreateResponse unprivileged = await McpFixture.CredentialAsync(rig, "no-context-permissions", rig.Hugo.Id, null, token).ConfigureAwait(false);
                using (HttpClient caller = rig.Host.CredentialClient(unprivileged.AccessKey, unprivileged.SecretKey))
                {
                    ToolInvocationOutcome denied = await HttpTestHelpers.SendAsync<ToolInvocationOutcome>(caller, HttpMethod.Post, "/v1.0/api/tool-invocations", new { toolName = "resource_update_context", resource = first.Name, arguments = new { content = "unauthorized-fact" } }, 200, token).ConfigureAwait(false);
                    Assert.Equal(InvocationDecisionEnum.Denied, denied.Decision, "credential owner admin status cannot bypass tool authorization");
                    Assert.Equal(InvocationStatusEnum.NotExecuted, denied.Status, "unauthorized metadata writes never execute");
                    Assert.Equal("forbidden", denied.DecisionCode, "the normal pipeline reports a permission denial");
                }
                await rig.UpdateBindingAsync(binding, r => r.AllowKnowledgeUpdates = false, token).ConfigureAwait(false);
                ToolInvocationOutcome disabled = await rig.InvokeAsync("resource_update_context", first.Name, new { content = "disabled-fact" }, token).ConfigureAwait(false);
                Assert.True(disabled.Status != InvocationStatusEnum.Succeeded, "AllowKnowledgeUpdates blocks writes despite tenant administrator bypass");
                Assert.Equal("permission_denied", disabled.DecisionCode, "disabled metadata writes use the ordinary pipeline denial");
                Succeeded(await rig.InvokeAsync("resource_get_context", first.Name, new { }, token).ConfigureAwait(false), "disabling learning still permits reads");
                await rig.UpdateBindingAsync(binding, r => { r.AllowKnowledgeUpdates = true; r.ToolRules = new List<BindingToolRule> { new BindingToolRule { ToolName = "resource_update_context", Rule = ToolRuleEnum.Deny } }; }, token).ConfigureAwait(false);
                ToolInvocationOutcome rule = await rig.InvokeAsync("resource_update_context", first.Name, new { content = "rule-denied-fact" }, token).ConfigureAwait(false);
                Assert.Equal(InvocationDecisionEnum.Denied, rule.Decision, "ordinary tool deny rules also cover metadata tools");
                ResourceKnowledge? stored = await rig.Context.Database.ResourceKnowledge.ReadAsync(rig.TenantId, first.Id, "context", "_root", token).ConfigureAwait(false);
                Assert.True(stored != null && stored.Version == 1 && !stored.Content.Contains("unauthorized", StringComparison.Ordinal) && !stored.Content.Contains("disabled", StringComparison.Ordinal), "denied writes leave the original entry unchanged");
                await rig.UpdateBindingAsync(secondBinding, request => request.ToolRules = new List<BindingToolRule> { new BindingToolRule { ToolName = "resource_update_context", Rule = ToolRuleEnum.RequireApproval } }, token).ConfigureAwait(false);
                ToolInvocationOutcome pending = await rig.InvokeAsync("resource_update_context", second.Name, new { content = "This delayed update must not bypass a changed binding." }, token, 202).ConfigureAwait(false);
                Assert.True(pending.ApprovalId != null, "explicit tool rule can hold a metadata update for approval");
                await rig.UpdateBindingAsync(secondBinding, request => request.AllowKnowledgeUpdates = false, token).ConfigureAwait(false);
                await rig.DecideAsync(pending.ApprovalId!, "approve", token).ConfigureAwait(false);
                ResourceKnowledge? afterApproval = await rig.Context.Database.ResourceKnowledge.ReadAsync(rig.TenantId, second.Id, "context", "_root", token).ConfigureAwait(false);
                Assert.True(afterApproval == null, "approving a stale request cannot bypass newly disabled learning");
                await rig.UpdateBindingAsync(secondBinding, request => request.AllowKnowledgeUpdates = true, token).ConfigureAwait(false);
                await HttpTestHelpers.SendAsync<LockdownState>(rig.Anderson, HttpMethod.Post, "/v1.0/api/tenant/lockdown", new { reason = "context safety boundary" }, 200, token).ConfigureAwait(false);
                ToolInvocationOutcome locked = await rig.InvokeAsync("resource_update_context", second.Name, new { content = "lockdown must refuse this saved context" }, token).ConfigureAwait(false);
                Assert.Equal(InvocationDecisionEnum.Denied, locked.Decision, "tenant lockdown covers metadata writes even though target effect is Read");
                Assert.Equal("tenant_lockdown", locked.DecisionCode, "lockdown is enforced by the normal pipeline");
                Succeeded(await rig.InvokeAsync("resource_get_context", first.Name, new { }, token).ConfigureAwait(false), "lockdown leaves saved context readable");
            }
        }

        private static async Task BoundsAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                Resource resource = await SshAsync(rig, "bounded-host", token).ConfigureAwait(false);
                await rig.BindAsync(rig.Hugo.Id, resource.Id, AccessModeEnum.ReadOnly, token).ConfigureAwait(false);
                ResourceKnowledge authored = await HttpTestHelpers.SendAsync<ResourceKnowledge>(rig.Anderson, HttpMethod.Put, "/v1.0/api/resources/" + resource.Id + "/knowledge/context", new { content = "Operator says retain this service inventory." }, 200, token).ConfigureAwait(false);
                foreach (string content in new[] { "password=context-fixture-credential-63H8s", "Authorization: Bearer context-token-9Qv6dB14", new string('x', 4001), "[{\"account\":\"one\",\"amount\":12},{\"account\":\"two\",\"amount\":24}]" })
                {
                    ToolInvocationOutcome outcome = await rig.InvokeAsync("resource_update_context", resource.Name, new { content }, token).ConfigureAwait(false);
                    Assert.True(outcome.Status != InvocationStatusEnum.Succeeded, "unsafe or oversized content must be refused");
                }
                foreach (object arguments in new object[]
                {
                    new { scope = "context-fixture-credential-63H8s", content = "Confirmed service location." },
                    new { key = "context-fixture-credential-63H8s", content = "Confirmed service location." }
                })
                {
                    ToolInvocationOutcome outcome = await rig.InvokeAsync("resource_update_context", resource.Name, arguments, token).ConfigureAwait(false);
                    Assert.True(outcome.Status != InvocationStatusEnum.Succeeded, "credentials in context metadata must be refused");
                }
                ResourceKnowledge? unchanged = await rig.Context.Database.ResourceKnowledge.ReadAsync(rig.TenantId, resource.Id, "context", "_root", token).ConfigureAwait(false);
                Assert.True(unchanged != null && unchanged.Content == authored.Content && unchanged.Version == 1, "refused input neither leaks nor changes operator notes");
                CompiledInstructions compiled = await CompiledAsync(rig.Anderson, rig.Hugo.Id, token).ConfigureAwait(false);
                Assert.True(!compiled.Text.Contains("63H8s", StringComparison.Ordinal) && !compiled.Text.Contains("9Qv6dB14", StringComparison.Ordinal), "rejected credential material cannot reach prompts");
                await HttpTestHelpers.SendAsync<ResourceKnowledge>(rig.Anderson, HttpMethod.Put, "/v1.0/api/resources/" + resource.Id + "/knowledge/context/long-note", new { content = new string('x', 14000) + "tail-marker" }, 200, token).ConfigureAwait(false);
                ToolInvocationOutcome tail = await rig.InvokeAsync("resource_get_context", resource.Name, new { key = "long-note", contentOffset = 14000 }, token).ConfigureAwait(false);
                Succeeded(tail, "explicit-key pagination into a large operator-authored entry");
                Assert.True(tail.Result!.Text.Contains("tail-marker", StringComparison.Ordinal), "a bounded context read can reach content beyond the first formatting window");
            }
        }

        private static async Task RestartAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            DatabaseSettings? database = null;
            MasterKeyOptions? keys = null;
            string resourceId = String.Empty;
            string marker = "Persisted deploy path /srv/checkout/releases/current and service checkout-nginx.";
            using (StubLlmServer stub = new StubLlmServer())
            {
                await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
                {
                    Resource resource = await SshAsync(rig, "restart-host", token).ConfigureAwait(false);
                    resourceId = resource.Id;
                    await rig.BindAsync(rig.Hugo.Id, resource.Id, AccessModeEnum.ReadOnly, token).ConfigureAwait(false);
                    Succeeded(await rig.InvokeAsync("resource_update_context", resource.Name, new { scope = "host", key = "deployment", content = marker }, token).ConfigureAwait(false), "learning before restart");
                    database = rig.Host.Settings.Database;
                    keys = rig.Host.KeyOptions;
                }
                Assert.True(database != null && keys != null, "restart settings captured");
                await using (TestServerHost restarted = await TestServerHost.StartAsync(provider, settings => { settings.Database = database!; settings.Agent.ToolRetrievalThreshold = 1000; }, token, keys).ConfigureAwait(false))
                using (HttpClient operatorClient = await restarted.UserClientAsync("anderson@hugo.local", TestServerHost.ActivatedPassword, null, token).ConfigureAwait(false))
                {
                    ModelEndpoint endpoint = await HttpTestHelpers.SendAsync<ModelEndpoint>(operatorClient, HttpMethod.Post, "/v1.0/api/model-endpoints", new { name = "context-after-restart", provider = "OpenAICompatible", baseUrl = stub.BaseUrl, model = stub.ModelName, maxRetries = 0 }, 201, token).ConfigureAwait(false);
                    AgentProfile profile = await HttpTestHelpers.SendAsync<AgentProfile>(operatorClient, HttpMethod.Post, "/v1.0/api/agent-profiles", new { name = "AfterRestart", inferenceEndpointId = endpoint.Id }, 201, token).ConfigureAwait(false);
                    await HttpTestHelpers.SendAsync<Binding>(operatorClient, HttpMethod.Post, "/v1.0/api/agent-profiles/" + profile.Id + "/bindings", new { resourceId, alias = "rebound-host", accessMode = "ReadOnly" }, 201, token).ConfigureAwait(false);
                    CompiledInstructions compiled = await CompiledAsync(operatorClient, profile.Id, token).ConfigureAwait(false);
                    Assert.True(compiled.Text.Contains(marker, StringComparison.Ordinal), "a new profile's cold compiler sees durable keyed context");
                    int checkedRequests = 0;
                    stub.Script.Responder = request =>
                    {
                        if (request.Messages.Any(m => m.Content != null && m.Content.Contains("Write a short title", StringComparison.Ordinal))) return StubLlmResponse.Message("Remembered deployment");
                        Assert.True(request.Messages.Any(m => m.Role == "system" && m.Content != null && m.Content.Contains(marker, StringComparison.Ordinal)), "the new conversation gets saved context before its first model request");
                        Assert.True(request.ToolNames.Contains("resource_get_context") && request.ToolNames.Contains("resource_update_context"), "internal runtime exposes the ordinary context tools after restart");
                        Interlocked.Increment(ref checkedRequests);
                        return StubLlmResponse.Message("Remembered the persistent deployment path.");
                    };
                    Conversation conversation = await HttpTestHelpers.SendAsync<Conversation>(operatorClient, HttpMethod.Post, "/v1.0/api/conversations", new { agentProfileId = profile.Id, title = "Already titled context fixture" }, 201, token).ConfigureAwait(false);
                    AgentTurnResult turn = await HttpTestHelpers.SendAsync<AgentTurnResult>(operatorClient, HttpMethod.Post, "/v1.0/api/conversations/" + conversation.Id + "/messages", new { message = "Use the saved deployment facts." }, 200, token).ConfigureAwait(false);
                    Assert.True(turn.FinalText.Contains("persistent deployment", StringComparison.Ordinal) && checkedRequests > 0, "the new conversation actually ran with recovered context");
                    ResourceKnowledge? entry = await restarted.Server.Context.Database.ResourceKnowledge.ReadAsync(restarted.TenantId, resourceId, "host", "deployment", token).ConfigureAwait(false);
                    Assert.True(entry != null && entry.Version == 1 && entry.Content == marker, "restart did not rewrite the entry or its provenance");
                }
            }
            if (provider == DatabaseTypeEnum.Sqlite && database != null)
            {
                foreach (string suffix in new[] { String.Empty, "-wal", "-shm", "-journal" }) File.Delete(database.Filename + suffix);
            }
        }

        private static async Task McpAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (McpFixture fixture = await McpFixture.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                Resource ssh = await SshAsync(fixture.Rig, "mcp-host", token).ConfigureAwait(false);
                Binding binding = await fixture.Rig.BindAsync(fixture.Rig.Hugo.Id, ssh.Id, AccessModeEnum.ReadOnly, token).ConfigureAwait(false);
                using (McpTestClient client = await fixture.ConnectAsync(McpFixture.Headers(fixture.HugoCredential), token).ConfigureAwait(false))
                {
                    McpTestToolList tools = await client.ListToolsAsync(token).ConfigureAwait(false);
                    Assert.True(tools.Find("resource_get_context") != null && tools.Find("resource_update_context") != null, "ordinary context tools are advertised by MCP");
                    Assert.True(tools.Find("resource_update_context")!.ResourceEnum.Contains("mcp-host") && tools.Find("resource_update_context")!.ResourceEnum.Contains("echo-ro"), "both SSH and registered plugin resources offer learning");
                    McpTestToolResult written = await client.CallToolAsync("resource_update_context", new { resource = "mcp-host", scope = "service", key = "checkout", content = "MCP learned the checkout unit path." }, token).ConfigureAwait(false);
                    Assert.True(written.IsError != true && written.Text.Contains("checkout unit", StringComparison.Ordinal), "credential-authorized MCP learning executes through the pipeline");
                    McpTestToolResult plugin = await client.CallToolAsync("resource_update_context", new { resource = "echo-ro", content = "Plugin resources share generic context." }, token).ConfigureAwait(false);
                    Assert.True(plugin.IsError != true, "a plugin needs no connector-specific implementation for learning");
                    McpTestToolResult read = await client.CallToolAsync("resource_get_context", new { resource = "mcp-host" }, token).ConfigureAwait(false);
                    Assert.True(read.IsError != true && read.Text.Contains("checkout unit", StringComparison.Ordinal), "no-argument get lists keyed facts outside the default context scope");
                    await fixture.Rig.UpdateBindingAsync(binding, request => request.AllowKnowledgeUpdates = false, token).ConfigureAwait(false);
                    McpTestToolList disabled = await client.ListToolsAsync(token).ConfigureAwait(false);
                    Assert.True(disabled.Find("resource_get_context")!.ResourceEnum.Contains("mcp-host") && !disabled.Find("resource_update_context")!.ResourceEnum.Contains("mcp-host"), "disabled writes disappear from resource choices while reads remain available");
                }
            }
        }

        private static async Task SameTurnAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (StubLlmServer stub = new StubLlmServer())
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, settings => settings.Agent.ToolRetrievalThreshold = 1000, token).ConfigureAwait(false))
            {
                Resource ssh = await SshAsync(rig, "planning-host", token).ConfigureAwait(false);
                Resource worker = await rig.ResourceAsync("planning-worker", token).ConfigureAwait(false);
                await rig.BindAsync(rig.Hugo.Id, ssh.Id, AccessModeEnum.ReadOnly, token).ConfigureAwait(false);
                await rig.BindAsync(rig.Hugo.Id, worker.Id, AccessModeEnum.ReadOnly, token).ConfigureAwait(false);
                await HttpTestHelpers.SendAsync<ResourceKnowledge>(rig.Anderson, HttpMethod.Put, "/v1.0/api/resources/" + ssh.Id + "/knowledge/context", new { content = "Operator says do not change the production service." }, 200, token).ConfigureAwait(false);
                ModelEndpoint endpoint = await HttpTestHelpers.SendAsync<ModelEndpoint>(rig.Anderson, HttpMethod.Post, "/v1.0/api/model-endpoints", new { name = "context-planner", provider = "OpenAICompatible", baseUrl = stub.BaseUrl, model = stub.ModelName, maxRetries = 0 }, 201, token).ConfigureAwait(false);
                await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + rig.Hugo.Id, new { inferenceEndpointId = endpoint.Id, maxIterations = 4 }, 200, token).ConfigureAwait(false);
                string finding = "Discovered checkout path /srv/checkout/release-47 and nginx unit checkout-nginx.";
                int planning = 0;
                stub.Script.Responder = request =>
                {
                    if (request.Messages.Any(m => m.Content != null && m.Content.Contains("Write a short title", StringComparison.Ordinal))) return StubLlmResponse.Message("Durable learning");
                    int step = Interlocked.Increment(ref planning);
                    Assert.True(request.ToolNames.Contains("resource_get_context") && request.ToolNames.Contains("resource_update_context"), "context tools are normal tools available to the internal model");
                    List<string> snapshots = request.Messages.Where(m => m.Role == "system" && m.Content != null && m.Content.Contains("Fresh saved resource context", StringComparison.Ordinal)).Select(m => m.Content!).ToList();
                    Assert.Equal(1, snapshots.Count, "each model call receives exactly one fresh snapshot, not accumulated stale copies");
                    Assert.True(snapshots[0].Contains("Operator says do not change", StringComparison.Ordinal), "operator notes remain visible during learning");
                    if (step == 1)
                    {
                        Assert.True(!snapshots[0].Contains(finding, StringComparison.Ordinal), "the finding does not exist before the learning call");
                        return StubLlmResponse.ToolCall("resource_update_context", "{\"resource\":\"planning-host\",\"scope\":\"host\",\"key\":\"deployment\",\"content\":\"" + finding + "\"}");
                    }
                    Assert.True(snapshots[0].Contains(finding, StringComparison.Ordinal), "new durable context is consulted before planning the next operational tool");
                    if (step == 2) return StubLlmResponse.ToolCall("echo_read", "{\"resource\":\"planning-worker\",\"text\":\"Operating with release-47 knowledge\"}");
                    return StubLlmResponse.Message("Used saved release-47 knowledge before the operation.");
                };
                Conversation conversation = await HttpTestHelpers.SendAsync<Conversation>(rig.Anderson, HttpMethod.Post, "/v1.0/api/conversations", new { agentProfileId = rig.Hugo.Id, title = "Already titled planning fixture" }, 201, token).ConfigureAwait(false);
                AgentTurnResult result = await HttpTestHelpers.SendAsync<AgentTurnResult>(rig.Anderson, HttpMethod.Post, "/v1.0/api/conversations/" + conversation.Id + "/messages", new { message = "Save the discovered deployment path, then perform a read using it." }, 200, token).ConfigureAwait(false);
                Assert.True(result.FinalText.Contains("Used saved release-47", StringComparison.Ordinal) && planning == 3, "learning, fresh-context planning, and operational completion all ran");
                List<ConversationMessage> messages = await rig.Context.Database.Messages.ListAsync(rig.TenantId, conversation.Id, false, token).ConfigureAwait(false);
                Assert.True(!messages.Any(m => m.Content.Contains("Fresh saved resource context", StringComparison.Ordinal)), "transient context snapshots are not duplicated into durable chat history");
                await rig.Context.Get<Hugo.Server.Services.ToolInvocationRecorder>().FlushAsync(token).ConfigureAwait(false);
                Hugo.Core.Enumeration.EnumerationResult<ToolInvocation> page = await rig.Context.Database.ToolInvocations.EnumerateAsync(rig.TenantId, new Hugo.Core.Enumeration.EnumerationQuery(), null, token).ConfigureAwait(false);
                List<ToolInvocation> invocations = page.Objects;
                Assert.True(invocations.Any(i => i.ToolName == "resource_update_context") && invocations.Any(i => i.ToolName == "echo_read"), "both metadata and operational tools were recorded");
                ResourceKnowledge? original = await rig.Context.Database.ResourceKnowledge.ReadAsync(rig.TenantId, ssh.Id, "context", "_root", token).ConfigureAwait(false);
                Assert.True(original != null && original.Source == KnowledgeSourceEnum.Operator && original.Content.Contains("do not change", StringComparison.Ordinal), "learning a keyed fact did not overwrite the operator's root context");
            }
        }
        private static async Task NestedModelAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (StubLlmServer stub = new StubLlmServer())
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, settings => settings.Connectors.ModelCallBudgetPerInvocation = 2, token).ConfigureAwait(false))
            {
                string secretText = "nested-model-private-credential-zZ47";
                Secret secret = await rig.SecretAsync("nested-model-secret", SecretKindEnum.ApiToken, new Dictionary<string, string> { { "token", secretText } }, token).ConfigureAwait(false);
                Resource resource = await rig.ResourceAsync("nested-model-worker", token, "echo", null, secret.Id).ConfigureAwait(false);
                await rig.BindAsync(rig.Hugo.Id, resource.Id, AccessModeEnum.ReadOnly, token).ConfigureAwait(false);
                ModelEndpoint endpoint = await HttpTestHelpers.SendAsync<ModelEndpoint>(rig.Anderson, HttpMethod.Post, "/v1.0/api/model-endpoints", new { name = "nested-context-model", provider = "OpenAICompatible", baseUrl = stub.BaseUrl, model = stub.ModelName, maxRetries = 0 }, 201, token).ConfigureAwait(false);
                await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + rig.Hugo.Id, new { inferenceEndpointId = endpoint.Id }, 200, token).ConfigureAwait(false);
                Succeeded(await rig.InvokeAsync("resource_update_context", resource.Name, new { content = "Initial deployment path /srv/release-before." }, token).ConfigureAwait(false), "initial nested context");
                Succeeded(await rig.InvokeAsync("resource_update_context", resource.Name, new { scope = "service", key = "checkout", content = "Keyed checkout service uses port 8080." }, token).ConfigureAwait(false), "keyed nested context");
                int nested = 0;
                stub.Script.Responder = request =>
                {
                    int call = Interlocked.Increment(ref nested);
                    string system = String.Join("\n", request.Messages.Where(m => m.Role == "system").Select(m => m.Content ?? String.Empty));
                    Assert.True(system.Contains("You are a test.", StringComparison.Ordinal), "the resource wrapper preserves the connector's original system prompt");
                    Assert.True(system.Contains("Keyed checkout service", StringComparison.Ordinal), "nested model context includes non-root entries");
                    Assert.True(!request.Body.Contains(secretText, StringComparison.Ordinal), "bound credentials never enter nested model prompts");
                    if (call == 1)
                    {
                        Assert.True(system.Contains("/srv/release-before", StringComparison.Ordinal), "first nested call sees existing saved facts");
                        rig.Context.Database.ResourceKnowledge.UpsertAsync(new ResourceKnowledge { TenantId = rig.TenantId, ResourceId = resource.Id, Scope = "context", Key = "_root", Content = "Updated deployment path /srv/release-after.", Source = KnowledgeSourceEnum.Agent }, token).ConfigureAwait(false).GetAwaiter().GetResult();
                    }
                    else
                    {
                        Assert.True(system.Contains("/srv/release-after", StringComparison.Ordinal) && !system.Contains("/srv/release-before", StringComparison.Ordinal), "the second completion within one tool invocation refreshes changed facts");
                    }
                    return StubLlmResponse.Message("nested context used");
                };
                ToolInvocationOutcome result = await rig.InvokeAsync("echo_model", resource.Name, new { prompt = "Read current deployment facts.", calls = 2 }, token).ConfigureAwait(false);
                Succeeded(result, "direct REST connector model execution");
                Assert.Equal(2, nested, "both nested completions used the real resource-bound model wrapper");
                Assert.True(result.Result!.Text.Contains("nested context used", StringComparison.Ordinal), "nested results reach the normal tool response");
            }
        }
        private static async Task ConcurrentCasAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                Resource resource = await rig.ResourceAsync("concurrent-context", token).ConfigureAwait(false);
                ResourceKnowledge original = await rig.Context.Database.ResourceKnowledge.UpsertAsync(new ResourceKnowledge { TenantId = rig.TenantId, ResourceId = resource.Id, Scope = "context", Content = "Original version one", Source = KnowledgeSourceEnum.Agent }, token).ConfigureAwait(false);
                Assert.Equal(1, original.Version, "seed version");
                ResourceKnowledge firstEntry = new ResourceKnowledge { TenantId = rig.TenantId, ResourceId = resource.Id, Scope = "context", Key = "_root", Content = "First writer's finding", Source = KnowledgeSourceEnum.Agent, UpdatedBy = "first-writer" };
                ResourceKnowledge secondEntry = new ResourceKnowledge { TenantId = rig.TenantId, ResourceId = resource.Id, Scope = "context", Key = String.Empty, Content = "Second writer's finding", Source = KnowledgeSourceEnum.Agent, UpdatedBy = "second-writer" };
                Task<ResourceKnowledge?> first = rig.Context.Database.ResourceKnowledge.TryUpsertAsync(firstEntry, 1, token);
                Task<ResourceKnowledge?> second = rig.Context.Database.ResourceKnowledge.TryUpsertAsync(secondEntry, 1, token);
                ResourceKnowledge?[] writes = await Task.WhenAll(first, second).ConfigureAwait(false);
                Assert.Equal(1, writes.Count(result => result != null), "exactly one writer wins the same expected version");
                Assert.Equal(1, writes.Count(result => result == null), "the losing writer reports a conflict");
                ResourceKnowledge winner = writes.Single(result => result != null)!;
                ResourceKnowledge? current = await rig.Context.Database.ResourceKnowledge.ReadAsync(rig.TenantId, resource.Id, "context", "_root", token).ConfigureAwait(false);
                Assert.True(current != null && current.Version == 2 && current.Content == winner.Content, "the committed winner is not overwritten by the losing request");
                Assert.Equal(original.Id, current!.Id, "the existing entry is updated rather than duplicated");
                List<ResourceKnowledge> entries = await rig.Context.Database.ResourceKnowledge.ListAsync(rig.TenantId, resource.Id, "context", token).ConfigureAwait(false);
                Assert.Equal(1, entries.Count, "empty and _root keys share a single persisted entry");
                List<ResourceKnowledgeVersion> history = await rig.Context.Database.ResourceKnowledge.ListVersionsAsync(rig.TenantId, resource.Id, "context", "_root", token).ConfigureAwait(false);
                Assert.Equal(2, history.Count, "the conflict creates no history row");
                Assert.True(history.Select(version => version.Version).Distinct().Count() == 2 && history.Single(version => version.Version == 2).Content == winner.Content, "history contains the original and the committed winner exactly once");
                Task<bool> firstDelete = rig.Context.Database.ResourceKnowledge.DeleteAsync(rig.TenantId, resource.Id, "context", "_root", "first-delete", token);
                Task<bool> secondDelete = rig.Context.Database.ResourceKnowledge.DeleteAsync(rig.TenantId, resource.Id, "context", String.Empty, "second-delete", token);
                bool[] deletes = await Task.WhenAll(firstDelete, secondDelete).ConfigureAwait(false);
                Assert.Equal(1, deletes.Count(result => result), "only the request that deleted the row records success");
                Assert.True(await rig.Context.Database.ResourceKnowledge.ReadAsync(rig.TenantId, resource.Id, "context", "_root", token).ConfigureAwait(false) == null, "the row was actually deleted");
                List<ResourceKnowledgeVersion> deletedHistory = await rig.Context.Database.ResourceKnowledge.ListVersionsAsync(rig.TenantId, resource.Id, "context", "_root", token).ConfigureAwait(false);
                Assert.Equal(3, deletedHistory.Count, "concurrent deletion adds one history row");
                Assert.True(deletedHistory.Count(version => version.Deleted) == 1 && deletedHistory.Select(version => version.Version).Distinct().Count() == 3, "a losing delete cannot create duplicate stale-version history");
            }
        }
    }
}
