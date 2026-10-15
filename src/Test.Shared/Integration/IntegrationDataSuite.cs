namespace Test.Shared.Integration
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Database;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Inference;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;
    using Hugo.Core.Responses;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;

    /// <summary>
    /// Data access contract for migrations 3 to 6 (P3.4, P3.5, P3.12, P3.25, P5.1, P5.2, P6.1, P6.2) on every configured
    /// provider: typed setting columns, child rows, atomic transitions, version history, reference clean-up, and the
    /// tenant and resource cascades.
    /// </summary>
    public static class IntegrationDataSuite
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
                string id = "integration-data-" + provider.ToString().ToLowerInvariant();
                List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
                cases.Add(new TestCaseDescriptor(id, "resources", "Resources with typed settings and secret slots, learned settings, secret referrers, and the resource cascade (" + provider + ")", ct => ResourcesAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "profiles-bindings", "Profiles, bindings with rules and deny patterns, default profile, and the profile cascade (" + provider + ")", ct => ProfilesAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "invocations-approvals", "Tool invocations, atomic status claims, approvals decided once, expiry listing, summaries (" + provider + ")", ct => InvocationsAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "knowledge-endpoints-plugins", "Knowledge versions, model endpoints, plugins, schema cache, and the tenant cascade (" + provider + ")", ct => KnowledgeAsync(provider, ct)));
                suites.Add(new TestSuiteDescriptor(id, "Integration data access (" + provider + ")", cases));
            }
            return suites;
        }

        #endregion

        #region Private-Methods

        private static async Task<Tenant> TenantAsync(DatabaseDriverBase db, CancellationToken token)
        {
            Tenant tenant = new Tenant();
            tenant.Name = "t-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            await db.Tenants.CreateAsync(tenant, token).ConfigureAwait(false);
            return tenant;
        }

        private static Resource NewResource(string tenantId, string name)
        {
            Resource resource = new Resource();
            resource.TenantId = tenantId;
            resource.Name = name;
            resource.ConnectorType = "echo";
            resource.DefaultAccessModeCeiling = AccessModeEnum.ReadWriteWithApproval;
            resource.Settings.Add(new ResourceSetting { Key = "port", ValueKind = SettingValueKindEnum.Integer, Value = "2222" });
            resource.Settings.Add(new ResourceSetting { Key = "tls", ValueKind = SettingValueKindEnum.Boolean, Value = "true" });
            resource.Settings.Add(new ResourceSetting { Key = "hosts", ValueKind = SettingValueKindEnum.StringList, Value = "a\nb" });
            return resource;
        }

        private static async Task ResourcesAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (DatabaseDriverBase db = await TestEnvironment.CreateDriverAsync(provider, token).ConfigureAwait(false))
            {
                Assert.True(db.SchemaVersion >= 6, "migrations 3 to 6 applied");
                Tenant tenant = await TenantAsync(db, token).ConfigureAwait(false);
                Resource resource = NewResource(tenant.Id, "r1");
                resource.SecretBindings.Add(new ResourceSecretBinding { SlotName = "token", SecretId = "sec_x" });
                await db.Resources.CreateAsync(resource, token).ConfigureAwait(false);
                Resource read = (await db.Resources.ReadAsync(tenant.Id, resource.Id, token).ConfigureAwait(false))!;
                Dictionary<string, string> values = read.SettingValues();
                Assert.True(values["port"] == "2222" && values["tls"] == "true" && values["hosts"] == "a\nb", "typed settings round trip");
                Assert.Equal(AccessModeEnum.ReadWriteWithApproval, read.DefaultAccessModeCeiling, "ceiling");
                Assert.True(read.SecretBindings.Count == 1 && read.SecretBindings[0].SecretId == "sec_x", "slot rows");
                Assert.True(await db.Resources.ReadAsync("other-tenant", resource.Id, token).ConfigureAwait(false) == null, "tenant scoped read");
                Assert.True((await db.Resources.ReadByNameAsync(tenant.Id, "r1", token).ConfigureAwait(false)) != null, "read by name");
                Assert.Equal(1, (await db.Resources.ListBySecretAsync(tenant.Id, "sec_x", token).ConfigureAwait(false)).Count, "referrers by secret");

                read.Settings = new List<ResourceSetting> { new ResourceSetting { Key = "port", ValueKind = SettingValueKindEnum.Integer, Value = "22" } };
                read.SecretBindings = new List<ResourceSecretBinding>();
                await db.Resources.UpdateAsync(read, token).ConfigureAwait(false);
                Resource updated = (await db.Resources.ReadAsync(tenant.Id, resource.Id, token).ConfigureAwait(false))!;
                Assert.True(updated.Settings.Count == 1 && updated.SecretBindings.Count == 0, "children replaced");
                await db.Resources.UpsertSettingAsync(tenant.Id, resource.Id, "hostKeyFingerprint", SettingValueKindEnum.String, "SHA256:x", token).ConfigureAwait(false);
                await db.Resources.UpsertSettingAsync(tenant.Id, resource.Id, "hostKeyFingerprint", SettingValueKindEnum.String, "SHA256:y", token).ConfigureAwait(false);
                Assert.Equal("SHA256:y", (await db.Resources.ReadAsync(tenant.Id, resource.Id, token).ConfigureAwait(false))!.SettingValues()["hostKeyFingerprint"], "upsert setting");
                await db.Resources.UpdateTestResultAsync(tenant.Id, resource.Id, false, "nope", token).ConfigureAwait(false);
                Resource tested = (await db.Resources.ReadAsync(tenant.Id, resource.Id, token).ConfigureAwait(false))!;
                Assert.True(tested.LastTestResult == false && tested.LastTestMessage == "nope" && tested.LastTestUtc.HasValue, "test result");

                EnumerationQuery query = new EnumerationQuery();
                query.Search = "r";
                await db.Resources.CreateAsync(NewResource(tenant.Id, "r2"), token).ConfigureAwait(false);
                EnumerationResult<Resource> page = await db.Resources.EnumerateAsync(tenant.Id, query, "echo", token).ConfigureAwait(false);
                Assert.True(page.TotalRecords == 2 && page.Objects.All(r => r.Settings.Count > 0), "enumerate with children");

                AgentProfile profile = new AgentProfile { TenantId = tenant.Id, Name = "p" };
                await db.AgentProfiles.CreateAsync(profile, token).ConfigureAwait(false);
                Binding binding = new Binding { TenantId = tenant.Id, AgentProfileId = profile.Id, ResourceId = resource.Id, Alias = "r1" };
                binding.ToolRules.Add(new BindingToolRule { ToolName = "echo_read", Rule = ToolRuleEnum.Deny });
                binding.DenyPatterns.Add("x");
                await db.Bindings.CreateAsync(binding, token).ConfigureAwait(false);
                await db.ResourceKnowledge.UpsertAsync(new ResourceKnowledge { TenantId = tenant.Id, ResourceId = resource.Id, Scope = "s", Key = "", Content = "c" }, token).ConfigureAwait(false);
                await db.SchemaCache.PutAsync(new ConnectorSchemaCacheEntry { TenantId = tenant.Id, ResourceId = resource.Id, CacheKind = "schema", Content = "x", ExpiresUtc = DateTime.UtcNow.AddMinutes(5) }, token).ConfigureAwait(false);
                Assert.True(await db.Resources.DeleteCascadeAsync(tenant.Id, resource.Id, token).ConfigureAwait(false), "delete");
                Assert.Equal(0, (await db.Bindings.ListByResourceAsync(tenant.Id, resource.Id, token).ConfigureAwait(false)).Count, "bindings cascade");
                Assert.Equal(0, (await db.ResourceKnowledge.ListAsync(tenant.Id, resource.Id, null, token).ConfigureAwait(false)).Count, "knowledge cascade");
                Assert.True(await db.SchemaCache.ReadAsync(tenant.Id, resource.Id, "schema", token).ConfigureAwait(false) == null, "schema cache cascade");
                Assert.True(!await db.Resources.DeleteCascadeAsync(tenant.Id, resource.Id, token).ConfigureAwait(false), "second delete reports false");
            }
        }

        private static async Task ProfilesAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (DatabaseDriverBase db = await TestEnvironment.CreateDriverAsync(provider, token).ConfigureAwait(false))
            {
                Tenant tenant = await TenantAsync(db, token).ConfigureAwait(false);
                Resource resource = NewResource(tenant.Id, "r1");
                await db.Resources.CreateAsync(resource, token).ConfigureAwait(false);
                AgentProfile a = new AgentProfile { TenantId = tenant.Id, Name = "Alpha", Temperature = 0.2, InstructionsAppend = "x" };
                AgentProfile b = new AgentProfile { TenantId = tenant.Id, Name = "Beta" };
                await db.AgentProfiles.CreateAsync(a, token).ConfigureAwait(false);
                await db.AgentProfiles.CreateAsync(b, token).ConfigureAwait(false);
                AgentProfile readA = (await db.AgentProfiles.ReadByNameAsync(tenant.Id, "alpha", token).ConfigureAwait(false))!;
                Assert.True(readA.Id == a.Id && readA.Temperature == 0.2 && readA.PersonaName == "Agent Smith" && readA.UserAddress == "Mr. Anderson", "profile round trip and case-insensitive name");
                await db.AgentProfiles.SetDefaultAsync(tenant.Id, a.Id, token).ConfigureAwait(false);
                await db.AgentProfiles.SetDefaultAsync(tenant.Id, b.Id, token).ConfigureAwait(false);
                List<AgentProfile> all = await db.AgentProfiles.ListAllAsync(tenant.Id, token).ConfigureAwait(false);
                Assert.True(all.Count(p => p.IsDefault) == 1 && all.First(p => p.IsDefault).Id == b.Id, "one default");
                Assert.Equal(b.Id, (await db.Tenants.ReadAsync(tenant.Id, token).ConfigureAwait(false))!.DefaultAgentProfileId, "tenant default");

                Binding binding = new Binding { TenantId = tenant.Id, AgentProfileId = b.Id, ResourceId = resource.Id, Alias = "db", AccessMode = AccessModeEnum.ReadWrite, MaxOutputBytes = 4096, InstructionMode = InstructionModeEnum.Append, InstructionText = "t" };
                binding.ToolRules.Add(new BindingToolRule { ToolName = "echo_read", Rule = ToolRuleEnum.RequireApproval });
                binding.DenyPatterns.AddRange(new[] { "first", "second" });
                await db.Bindings.CreateAsync(binding, token).ConfigureAwait(false);
                Binding readBinding = (await db.Bindings.ReadAsync(tenant.Id, binding.Id, token).ConfigureAwait(false))!;
                Assert.True(readBinding.ResourceName == "r1" && readBinding.ConnectorType == "echo", "joined resource fields");
                Assert.True(readBinding.MaxOutputBytes == 4096 && readBinding.MaxRows == null && readBinding.InstructionMode == InstructionModeEnum.Append, "binding columns");
                Assert.True(readBinding.ToolRules.Single().Rule == ToolRuleEnum.RequireApproval && String.Join(",", readBinding.DenyPatterns) == "first,second", "children in order");
                readBinding.DenyPatterns = new List<string> { "only" };
                readBinding.ToolRules.Clear();
                readBinding.Alias = "db2";
                await db.Bindings.UpdateAsync(readBinding, token).ConfigureAwait(false);
                Binding afterUpdate = (await db.Bindings.ReadAsync(tenant.Id, binding.Id, token).ConfigureAwait(false))!;
                Assert.True(afterUpdate.Alias == "db2" && afterUpdate.ToolRules.Count == 0 && afterUpdate.DenyPatterns.Single() == "only", "children replaced");

                User user = new User { TenantId = tenant.Id, Email = "u@x", PasswordHash = "h", DefaultAgentProfileId = b.Id };
                await db.Users.CreateAsync(user, token).ConfigureAwait(false);
                Credential credential = new Credential { TenantId = tenant.Id, UserId = user.Id, Name = "c", AccessKey = "ak_" + Guid.NewGuid().ToString("N"), SecretKeyEncrypted = "x", AgentProfileId = b.Id, LockToProfile = true };
                await db.Credentials.CreateAsync(credential, token).ConfigureAwait(false);
                Assert.True(await db.AgentProfiles.DeleteCascadeAsync(tenant.Id, b.Id, token).ConfigureAwait(false), "delete profile");
                Assert.Equal(0, (await db.Bindings.ListByProfileAsync(tenant.Id, b.Id, token).ConfigureAwait(false)).Count, "bindings cascade");
                Assert.True((await db.Users.ReadAsync(tenant.Id, user.Id, token).ConfigureAwait(false))!.DefaultAgentProfileId == null, "user default cleared");
                Credential clearedCredential = (await db.Credentials.ReadAsync(tenant.Id, credential.Id, token).ConfigureAwait(false))!;
                Assert.True(clearedCredential.AgentProfileId == null && !clearedCredential.LockToProfile, "credential binding cleared");
                Assert.True((await db.Tenants.ReadAsync(tenant.Id, token).ConfigureAwait(false))!.DefaultAgentProfileId == null, "tenant default cleared");
                Assert.True(await db.Resources.ReadAsync(tenant.Id, resource.Id, token).ConfigureAwait(false) != null, "resource kept");
            }
        }

        private static async Task InvocationsAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (DatabaseDriverBase db = await TestEnvironment.CreateDriverAsync(provider, token).ConfigureAwait(false))
            {
                Tenant tenant = await TenantAsync(db, token).ConfigureAwait(false);
                ToolInvocation pending = new ToolInvocation { TenantId = tenant.Id, ToolName = "echo_write", Decision = InvocationDecisionEnum.ApprovalRequired, Status = InvocationStatusEnum.Pending, Effect = EffectEnum.Write, AccessMode = AccessModeEnum.ReadWriteWithApproval, Surface = InvocationSurfaceEnum.Mcp, ArgumentsRedacted = "{\"text\":\"x\"}" };
                await db.ToolInvocations.CreateAsync(pending, token).ConfigureAwait(false);
                ToolInvocation denied = new ToolInvocation { TenantId = tenant.Id, ToolName = "echo_read", Decision = InvocationDecisionEnum.Denied, Status = InvocationStatusEnum.NotExecuted, DecisionCode = "readonly_binding" };
                await db.ToolInvocations.CreateAsync(denied, token).ConfigureAwait(false);
                ToolInvocation readBack = (await db.ToolInvocations.ReadAsync(tenant.Id, pending.Id, token).ConfigureAwait(false))!;
                Assert.True(readBack.Effect == EffectEnum.Write && readBack.Surface == InvocationSurfaceEnum.Mcp && readBack.ArgumentsRedacted == "{\"text\":\"x\"}", "invocation round trip");
                Assert.True(await db.ToolInvocations.TryTransitionAsync(tenant.Id, pending.Id, InvocationStatusEnum.Pending, InvocationStatusEnum.Running, token).ConfigureAwait(false), "first claim wins");
                Assert.True(!await db.ToolInvocations.TryTransitionAsync(tenant.Id, pending.Id, InvocationStatusEnum.Pending, InvocationStatusEnum.Running, token).ConfigureAwait(false), "second claim loses");
                readBack.Status = InvocationStatusEnum.Succeeded;
                readBack.OutputBytes = 42;
                readBack.OutputPreview = "out";
                await db.ToolInvocations.UpdateAsync(readBack, token).ConfigureAwait(false);
                EnumerationResult<ToolInvocation> filtered = await db.ToolInvocations.EnumerateAsync(tenant.Id, new EnumerationQuery(), new ToolInvocationFilter { Decision = InvocationDecisionEnum.Denied }, token).ConfigureAwait(false);
                Assert.True(filtered.TotalRecords == 1 && filtered.Objects[0].Id == denied.Id, "filter");
                ToolInvocationSummary summary = await db.ToolInvocations.SummaryAsync(tenant.Id, null, token).ConfigureAwait(false);
                Assert.True(summary.Total == 2 && summary.ByTool.Count == 2 && summary.ByStatus.Any(s => s.Status == "Succeeded"), "summary");
                Assert.Equal(0L, (await db.ToolInvocations.EnumerateAsync("other", new EnumerationQuery(), null, token).ConfigureAwait(false)).TotalRecords, "tenant scoped list");

                Approval approval = new Approval { TenantId = tenant.Id, ToolInvocationId = pending.Id, ToolName = "echo_write", Summary = "do it", ExpiresUtc = DateTime.UtcNow.AddMinutes(10) };
                await db.Approvals.CreateAsync(approval, token).ConfigureAwait(false);
                Approval expiring = new Approval { TenantId = tenant.Id, ToolInvocationId = denied.Id, ToolName = "echo_read", Summary = "old", ExpiresUtc = DateTime.UtcNow.AddMinutes(-1) };
                await db.Approvals.CreateAsync(expiring, token).ConfigureAwait(false);
                Assert.Equal(2, (await db.Approvals.ListPendingAsync(tenant.Id, token).ConfigureAwait(false)).Count, "two pending");
                List<Approval> due = await db.Approvals.ListExpiredAsync(DateTime.UtcNow, 100, token).ConfigureAwait(false);
                Assert.True(due.Any(a => a.Id == expiring.Id) && !due.Any(a => a.Id == approval.Id), "expired listing");
                Assert.True(await db.Approvals.TryDecideAsync(tenant.Id, approval.Id, ApprovalStatusEnum.Approved, "usr_1", "Anderson", "ok", token).ConfigureAwait(false), "first decision wins");
                Assert.True(!await db.Approvals.TryDecideAsync(tenant.Id, approval.Id, ApprovalStatusEnum.Rejected, "usr_2", "Smith", null, token).ConfigureAwait(false), "second decision loses");
                Approval decided = (await db.Approvals.ReadAsync(tenant.Id, approval.Id, token).ConfigureAwait(false))!;
                Assert.True(decided.Status == ApprovalStatusEnum.Approved && decided.DecidedByName == "Anderson" && decided.DecidedUtc.HasValue && decided.DecisionNote == "ok", "decision stored");
                EnumerationResult<Approval> pendingPage = await db.Approvals.EnumerateAsync(tenant.Id, new EnumerationQuery(), new ToolInvocationFilter { ApprovalStatus = ApprovalStatusEnum.Pending }, token).ConfigureAwait(false);
                Assert.True(pendingPage.TotalRecords == 1 && pendingPage.Objects[0].Id == expiring.Id, "filter by status");
            }
        }

        private static async Task KnowledgeAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (DatabaseDriverBase db = await TestEnvironment.CreateDriverAsync(provider, token).ConfigureAwait(false))
            {
                Tenant tenant = await TenantAsync(db, token).ConfigureAwait(false);
                Resource resource = NewResource(tenant.Id, "r1");
                await db.Resources.CreateAsync(resource, token).ConfigureAwait(false);
                ResourceKnowledge v1 = await db.ResourceKnowledge.UpsertAsync(new ResourceKnowledge { TenantId = tenant.Id, ResourceId = resource.Id, Scope = "database", Key = "", Content = "one", Source = KnowledgeSourceEnum.Generated, UpdatedBy = "model" }, token).ConfigureAwait(false);
                ResourceKnowledge v2 = await db.ResourceKnowledge.UpsertAsync(new ResourceKnowledge { TenantId = tenant.Id, ResourceId = resource.Id, Scope = "database", Key = "", Content = "two", Source = KnowledgeSourceEnum.Operator }, token).ConfigureAwait(false);
                Assert.True(v1.Version == 1 && v2.Version == 2 && v2.Id == v1.Id, "versions increment on one entry");
                await db.ResourceKnowledge.UpsertAsync(new ResourceKnowledge { TenantId = tenant.Id, ResourceId = resource.Id, Scope = "table", Key = "public.orders", Content = "t" }, token).ConfigureAwait(false);
                Assert.Equal(2, (await db.ResourceKnowledge.ListAsync(tenant.Id, resource.Id, null, token).ConfigureAwait(false)).Count, "two entries");
                Assert.Equal(1, (await db.ResourceKnowledge.ListAsync(tenant.Id, resource.Id, "table", token).ConfigureAwait(false)).Count, "scope filter");
                ResourceKnowledge root = (await db.ResourceKnowledge.ReadAsync(tenant.Id, resource.Id, "database", "", token).ConfigureAwait(false))!;
                Assert.True(root.Content == "two" && root.Source == KnowledgeSourceEnum.Operator, "root entry with an empty key");
                Assert.True(await db.ResourceKnowledge.DeleteAsync(tenant.Id, resource.Id, "database", "", "op", token).ConfigureAwait(false), "delete");
                ResourceKnowledge again = await db.ResourceKnowledge.UpsertAsync(new ResourceKnowledge { TenantId = tenant.Id, ResourceId = resource.Id, Scope = "database", Key = "", Content = "three" }, token).ConfigureAwait(false);
                Assert.Equal(4, again.Version, "history continues after a delete");
                List<ResourceKnowledgeVersion> versions = await db.ResourceKnowledge.ListVersionsAsync(tenant.Id, resource.Id, "database", "", token).ConfigureAwait(false);
                Assert.True(versions.Count == 4 && versions[0].Version == 4 && versions.Any(v => v.Deleted), "version rows including the deletion");

                ModelEndpoint endpoint = new ModelEndpoint { TenantId = tenant.Id, Name = "e1", Provider = ModelProviderEnum.Ollama, Model = "llama", ApiKeySecretId = "sec_k", EmbeddingDimensions = 768 };
                await db.ModelEndpoints.CreateAsync(endpoint, token).ConfigureAwait(false);
                ModelEndpoint readEndpoint = (await db.ModelEndpoints.ReadByNameAsync(tenant.Id, "e1", token).ConfigureAwait(false))!;
                Assert.True(readEndpoint.Provider == ModelProviderEnum.Ollama && readEndpoint.EmbeddingDimensions == 768 && readEndpoint.HealthStatus == ModelHealthStatusEnum.Unknown, "endpoint round trip");
                await db.ModelEndpoints.UpdateHealthAsync(tenant.Id, endpoint.Id, ModelHealthStatusEnum.Healthy, "ok", token).ConfigureAwait(false);
                Assert.Equal(ModelHealthStatusEnum.Healthy, (await db.ModelEndpoints.ReadAsync(tenant.Id, endpoint.Id, token).ConfigureAwait(false))!.HealthStatus, "health");
                Assert.True((await db.ModelEndpoints.ListActiveAsync(token).ConfigureAwait(false)).Any(e => e.Id == endpoint.Id), "active listing");
                Assert.Equal(1, (await db.ModelEndpoints.ListBySecretAsync(tenant.Id, "sec_k", token).ConfigureAwait(false)).Count, "by secret");
                AgentProfile profile = new AgentProfile { TenantId = tenant.Id, Name = "p", InferenceEndpointId = endpoint.Id, EmbeddingEndpointId = endpoint.Id };
                await db.AgentProfiles.CreateAsync(profile, token).ConfigureAwait(false);
                Assert.Equal(1, (await db.AgentProfiles.ListByEndpointAsync(tenant.Id, endpoint.Id, token).ConfigureAwait(false)).Count, "profiles by endpoint");
                Assert.True(await db.ModelEndpoints.DeleteAsync(tenant.Id, endpoint.Id, token).ConfigureAwait(false), "delete endpoint");
                AgentProfile cleared = (await db.AgentProfiles.ReadAsync(tenant.Id, profile.Id, token).ConfigureAwait(false))!;
                Assert.True(cleared.InferenceEndpointId == null && cleared.EmbeddingEndpointId == null, "profile references cleared");

                string pluginName = "plug-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                PluginRecord plugin = await db.Plugins.UpsertAsync(new PluginRecord { Name = pluginName, AssemblyPath = "/p/x.dll", Loaded = true, ConnectorTypes = new List<string> { "a", "b" } }, token).ConfigureAwait(false);
                PluginRecord replaced = await db.Plugins.UpsertAsync(new PluginRecord { Name = pluginName, AssemblyPath = "/p/x.dll", Loaded = false, LoadError = "gone", ConnectorTypes = new List<string> { "c" } }, token).ConfigureAwait(false);
                PluginRecord readPlugin = (await db.Plugins.ReadByNameAsync(pluginName, token).ConfigureAwait(false))!;
                Assert.True(replaced.Id == plugin.Id && !readPlugin.Loaded && readPlugin.LoadError == "gone" && readPlugin.ConnectorTypes.SequenceEqual(new[] { "c" }), "plugin upsert by name");

                await db.SchemaCache.PutAsync(new ConnectorSchemaCacheEntry { TenantId = tenant.Id, ResourceId = resource.Id, CacheKind = "schema", Content = "fresh", ExpiresUtc = DateTime.UtcNow.AddMinutes(5) }, token).ConfigureAwait(false);
                Assert.Equal("fresh", (await db.SchemaCache.ReadAsync(tenant.Id, resource.Id, "schema", token).ConfigureAwait(false))!.Content, "cache read");
                await db.SchemaCache.PutAsync(new ConnectorSchemaCacheEntry { TenantId = tenant.Id, ResourceId = resource.Id, CacheKind = "schema", Content = "stale", ExpiresUtc = DateTime.UtcNow.AddMinutes(-5) }, token).ConfigureAwait(false);
                Assert.True(await db.SchemaCache.ReadAsync(tenant.Id, resource.Id, "schema", token).ConfigureAwait(false) == null, "expired entries are not returned");

                ToolInvocation invocation = new ToolInvocation { TenantId = tenant.Id, ToolName = "echo_read" };
                await db.ToolInvocations.CreateAsync(invocation, token).ConfigureAwait(false);
                await db.Approvals.CreateAsync(new Approval { TenantId = tenant.Id, ToolInvocationId = invocation.Id, ToolName = "echo_read", Summary = "x", ExpiresUtc = DateTime.UtcNow }, token).ConfigureAwait(false);
                await db.Bindings.CreateAsync(new Binding { TenantId = tenant.Id, AgentProfileId = profile.Id, ResourceId = resource.Id, Alias = "r1" }, token).ConfigureAwait(false);
                int deleted = await db.Tenants.DeleteCascadeAsync(tenant.Id, token).ConfigureAwait(false);
                Assert.True(deleted > 8, "tenant cascade removes rows across the new tables");
                Assert.True(await db.Resources.ReadAsync(tenant.Id, resource.Id, token).ConfigureAwait(false) == null && (await db.AgentProfiles.ListAllAsync(tenant.Id, token).ConfigureAwait(false)).Count == 0, "tenant rows gone");
                Assert.True(await db.Plugins.ReadByNameAsync(pluginName, token).ConfigureAwait(false) != null, "global plugin rows survive a tenant cascade");
            }
        }

        #endregion
    }
}
