namespace Test.Shared.Integration
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Connectors;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Instructions;
    using Hugo.Core.Models;
    using Hugo.Core.Prompts;
    using Hugo.Core.Responses;
    using Hugo.Core.Serialization;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;

    /// <summary>
    /// Integration suites for resources (P3.6, P3.7), connectors (P3.3), profiles and bindings (P5.8 to P5.10), layered
    /// templates (P5.4, P5.5, P5.7), the instruction compiler (P5.11, P5.12), profile resolution and credential binding
    /// (P5.13, P5.18), ProfileUser authorization, and tenant scoping of every new entity, on every configured provider.
    /// </summary>
    public static class ConfigurationIntegrationSuite
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
                string id = "integration-config-" + provider.ToString().ToLowerInvariant();
                List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
                cases.Add(new TestCaseDescriptor(id, "connectors", "Connector routes return descriptors with settings schemas, secret slots, and tools for every wave 1 connector (" + provider + ")", ct => ConnectorsAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "resources", "Resource validation, secret slot kinds, duplicate, referrers, tests of saved and unsaved settings, timeouts, and cascades (" + provider + ")", ct => ResourcesAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "profiles-bindings", "Default Hugo profile, profile CRUD and duplicate, tenant provisioning, bindings, aliases, and tool rules (" + provider + ")", ct => ProfilesAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "templates", "Template layers, saves, versions, resets, Global layer permissions, built-in update detection, diff, and render (" + provider + ")", ct => TemplatesAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "compiler", "Compiled instructions: persona defaults, append and replace, notes, surfaces, source map, cache invalidation (" + provider + ")", ct => CompilerAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "resolution-rbac", "Profile resolution order, credential lock_to_profile, user default profile, and ProfileUser execute rights (" + provider + ")", ct => ResolutionAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "tenant-scoping", "Resources, profiles, bindings, knowledge, endpoints, invocations, and approvals of another tenant are 404 (" + provider + ")", ct => ScopingAsync(provider, ct)));
                suites.Add(new TestSuiteDescriptor(id, "Integration configuration (" + provider + ")", cases));
            }
            return suites;
        }

        #endregion

        #region Private-Methods

        private static async Task ConnectorsAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                List<ConnectorView> connectors = await HttpTestHelpers.SendAsync<List<ConnectorView>>(rig.Anderson, HttpMethod.Get, "/v1.0/api/connectors", null, 200, token).ConfigureAwait(false);
                foreach (string type in new[] { "ssh", "s3", "azure-blob", "gcs", "sqlite", "mysql", "sqlserver", "postgresql", "solarwinds-observability", "echo" })
                {
                    ConnectorView? view = connectors.FirstOrDefault(c => c.Type == type);
                    Assert.True(view != null, "connector " + type + " is registered");
                    Assert.True(view!.Tools.Count > 0, type + " lists tools");
                    Assert.True(view.Settings.Count > 0, type + " lists settings");
                }
                Assert.True(!connectors.Any(c => c.Type == "papertrail"), "retired Papertrail is absent from API connector discovery");
                ConnectorView sqlite = await HttpTestHelpers.SendAsync<ConnectorView>(rig.Anderson, HttpMethod.Get, "/v1.0/api/connectors/sqlite", null, 200, token).ConfigureAwait(false);
                Assert.Equal("sql", sqlite.Family, "family");
                Assert.True(sqlite.SecretSlots.Any(s => s.Name == "connectionString"), "secret slots");
                Assert.True(sqlite.Tools.Any(t => t.Name == "sql_query" && t.ClassifiedPerCall), "per-call classified tool flagged");
                Assert.Equal("connector.sqlite.instructions", sqlite.DefaultInstructionsKey, "instructions key");
                TestResponse missing = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Get, "/v1.0/api/connectors/nope", null, token).ConfigureAwait(false);
                Assert.Equal(404, missing.StatusCode, "unknown connector");
            }
        }

        private static async Task ResourcesAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, s => s.Connectors.TestTimeoutSeconds = 1, token).ConfigureAwait(false))
            {
                TestResponse badEnum = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/resources", new { name = "bad", connectorType = "echo", settings = new { mode = "bogus" } }, token).ConfigureAwait(false);
                Assert.Equal(400, badEnum.StatusCode, "schema enum violation");
                Assert.True(badEnum.Body.Contains("SettingsInvalid", StringComparison.Ordinal) && badEnum.Body.Contains("mode", StringComparison.Ordinal), "field-level error: " + badEnum.Body);
                TestResponse own = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/resources", new { name = "bad", connectorType = "echo", settings = new { mode = "invalid" } }, token).ConfigureAwait(false);
                Assert.Equal(400, own.StatusCode, "connector ValidateAsync error");
                TestResponse unknownType = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/resources", new { name = "bad", connectorType = "nope" }, token).ConfigureAwait(false);
                Assert.Equal(400, unknownType.StatusCode, "unknown connector type");
                TestResponse retiredType = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/resources", new { name = "retired-logs", connectorType = "papertrail" }, token).ConfigureAwait(false);
                Assert.Equal(400, retiredType.StatusCode, "retired Papertrail resource creation is rejected");
                TestResponse unknownKey = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/resources", new { name = "bad", connectorType = "echo", settings = new { surprise = "x" } }, token).ConfigureAwait(false);
                Assert.Equal(400, unknownKey.StatusCode, "unknown setting");
                TestResponse badName = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/resources", new { name = "has space", connectorType = "echo" }, token).ConfigureAwait(false);
                Assert.Equal(400, badName.StatusCode, "invalid name");

                Secret password = await rig.SecretAsync("pw", SecretKindEnum.Password, new Dictionary<string, string> { { "password", "pw-value-123" } }, token).ConfigureAwait(false);
                Secret apiToken = await rig.SecretAsync("tok", SecretKindEnum.ApiToken, new Dictionary<string, string> { { "token", "tok-value-123" } }, token).ConfigureAwait(false);
                TestResponse wrongKind = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/resources", new { name = "kind", connectorType = "echo", secretBindings = new[] { new { slotName = "token", secretId = password.Id } } }, token).ConfigureAwait(false);
                Assert.Equal(400, wrongKind.StatusCode, "secret kind not accepted by the slot");
                Assert.True(wrongKind.Body.Contains("kind_not_accepted", StringComparison.Ordinal), "kind code: " + wrongKind.Body);
                TestResponse missingSecret = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/resources", new { name = "kind", connectorType = "echo", secretBindings = new[] { new { slotName = "token", secretId = "sec_nope" } } }, token).ConfigureAwait(false);
                Assert.Equal(400, missingSecret.StatusCode, "missing secret");
                TestResponse unknownSlot = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/resources", new { name = "kind", connectorType = "echo", secretBindings = new[] { new { slotName = "nope", secretId = apiToken.Id } } }, token).ConfigureAwait(false);
                Assert.Equal(400, unknownSlot.StatusCode, "unknown slot");

                Resource a = await rig.ResourceAsync("echo-a", token, "echo", new Dictionary<string, object> { { "greeting", "hi" } }, apiToken.Id).ConfigureAwait(false);
                Assert.Equal("hi", a.SettingValues()["greeting"], "setting stored");
                Assert.Equal("hi", a.Endpoint, "endpoint derived from the summary setting");
                Assert.Equal("tok", a.SecretBindings[0].SecretName, "secret name filled on read");
                TestResponse duplicateName = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/resources", new { name = "echo-a", connectorType = "echo" }, token).ConfigureAwait(false);
                Assert.Equal(409, duplicateName.StatusCode, "duplicate name");

                Resource updated = await HttpTestHelpers.SendAsync<Resource>(rig.Anderson, HttpMethod.Put, "/v1.0/api/resources/" + a.Id, new { notes = "Primary echo.", environment = "prod", settings = new { greeting = "hey" } }, 200, token).ConfigureAwait(false);
                Assert.Equal("hey", updated.SettingValues()["greeting"], "settings replaced");
                Assert.Equal(1, updated.SecretBindings.Count, "slots kept when omitted");
                Assert.Equal("Primary echo.", updated.Notes, "notes");
                TestResponse typeChange = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Put, "/v1.0/api/resources/" + a.Id, new { connectorType = "sqlite" }, token).ConfigureAwait(false);
                Assert.Equal(400, typeChange.StatusCode, "connector type cannot change");

                ResourceTestResponse saved = await HttpTestHelpers.SendAsync<ResourceTestResponse>(rig.Anderson, HttpMethod.Post, "/v1.0/api/resources/" + a.Id + "/test", null, 200, token).ConfigureAwait(false);
                Assert.True(saved.Success, "saved test");
                Assert.True(!saved.Message.Contains("tok-value-123", StringComparison.Ordinal), "test message scrubbed: " + saved.Message);
                ResourceTestResponse unsaved = await HttpTestHelpers.SendAsync<ResourceTestResponse>(rig.Anderson, HttpMethod.Post, "/v1.0/api/resources/test", new { name = "draft", connectorType = "echo", settings = new { greeting = "draft" } }, 200, token).ConfigureAwait(false);
                Assert.True(unsaved.Success && unsaved.Message.Contains("draft", StringComparison.Ordinal), "unsaved test");
                ResourceTestResponse unsavedInvalid = await HttpTestHelpers.SendAsync<ResourceTestResponse>(rig.Anderson, HttpMethod.Post, "/v1.0/api/resources/test", new { connectorType = "echo", settings = new { mode = "invalid" } }, 200, token).ConfigureAwait(false);
                Assert.True(!unsavedInvalid.Success && unsavedInvalid.ValidationErrors.Count > 0, "unsaved validation errors are returned");
                ResourceTestResponse slow = await HttpTestHelpers.SendAsync<ResourceTestResponse>(rig.Anderson, HttpMethod.Post, "/v1.0/api/resources/test", new { connectorType = "echo", settings = new { greeting = "slow" } }, 200, token).ConfigureAwait(false);
                Assert.Equal(ConnectorErrorCodes.Timeout, slow.ErrorCode, "the server cancels a test after Connectors.TestTimeoutSeconds");
                Assert.Equal(0, (await rig.Context.Database.Resources.ListAllAsync(rig.TenantId, token).ConfigureAwait(false)).Count(r => r.Name == "draft"), "unsaved tests store nothing");

                Binding binding = await rig.BindAsync(rig.Hugo.Id, a.Id, AccessModeEnum.ReadOnly, token).ConfigureAwait(false);
                List<ResourceReferrer> referrers = await HttpTestHelpers.SendAsync<List<ResourceReferrer>>(rig.Anderson, HttpMethod.Get, "/v1.0/api/resources/" + a.Id + "/referrers", null, 200, token).ConfigureAwait(false);
                Assert.True(referrers.Any(r => r.BindingId == binding.Id && r.AgentProfileName == "Hugo"), "referrers list the binding");
                List<SecretReferrer> secretReferrers = await HttpTestHelpers.SendAsync<List<SecretReferrer>>(rig.Anderson, HttpMethod.Get, "/v1.0/api/secrets/" + apiToken.Id + "/referrers", null, 200, token).ConfigureAwait(false);
                Assert.True(secretReferrers.Any(r => r.ReferrerType == "Resource" && r.ReferrerId == a.Id), "the secret reports the resource");
                TestResponse inUse = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Delete, "/v1.0/api/secrets/" + apiToken.Id, null, token).ConfigureAwait(false);
                Assert.Equal(409, inUse.StatusCode, "a secret in use cannot be deleted");

                Resource copy = await HttpTestHelpers.SendAsync<Resource>(rig.Anderson, HttpMethod.Post, "/v1.0/api/resources/" + a.Id + "/duplicate", new { name = "echo-copy" }, 201, token).ConfigureAwait(false);
                Assert.Equal("hey", copy.SettingValues()["greeting"], "duplicate copies settings");
                Assert.Equal(1, copy.SecretBindings.Count, "duplicate copies secret slots");

                await HttpTestHelpers.SendAsync<ResourceKnowledge>(rig.Anderson, HttpMethod.Put, "/v1.0/api/resources/" + a.Id + "/knowledge/notes", new { content = "x" }, 200, token).ConfigureAwait(false);
                TestResponse deleted = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Delete, "/v1.0/api/resources/" + a.Id, null, token).ConfigureAwait(false);
                Assert.Equal(204, deleted.StatusCode, "delete");
                Assert.Equal(0, (await rig.Context.Database.Bindings.ListByProfileAsync(rig.TenantId, rig.Hugo.Id, token).ConfigureAwait(false)).Count, "bindings cascade");
                Assert.Equal(0, (await rig.Context.Database.ResourceKnowledge.ListAsync(rig.TenantId, a.Id, null, token).ConfigureAwait(false)).Count, "knowledge cascades");
                Assert.True((await rig.AuditAsync(AuditEventTypeEnum.ResourceChanged, token).ConfigureAwait(false)).Count >= 4, "resource changes audited");
                // Simulate a saved resource from an installation predating connector retirement.
                copy.ConnectorType = "papertrail";
                await rig.Context.Database.Resources.UpdateAsync(copy, token).ConfigureAwait(false);
                Resource retired = await HttpTestHelpers.SendAsync<Resource>(rig.Anderson, HttpMethod.Get, "/v1.0/api/resources/" + copy.Id, null, 200, token).ConfigureAwait(false);
                Assert.Equal("papertrail", retired.ConnectorType, "saved retired resources remain readable without conversion");
                EnumerationResult<Resource> listed = await HttpTestHelpers.SendAsync<EnumerationResult<Resource>>(rig.Anderson, HttpMethod.Get, "/v1.0/api/resources", null, 200, token).ConfigureAwait(false);
                Assert.True(listed.Objects.Any(r => r.Id == copy.Id), "saved retired resources remain visible for cleanup");
                TestResponse retiredDuplicate = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/resources/" + copy.Id + "/duplicate", new { name = "retired-copy" }, token).ConfigureAwait(false);
                Assert.Equal(400, retiredDuplicate.StatusCode, "duplication cannot create a retired connector resource");
                Assert.True(await rig.Context.Database.Resources.ReadByNameAsync(rig.TenantId, "retired-copy", token).ConfigureAwait(false) == null, "rejected duplicate stores no resource");
                TestResponse retiredDeleted = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Delete, "/v1.0/api/resources/" + copy.Id, null, token).ConfigureAwait(false);
                Assert.Equal(204, retiredDeleted.StatusCode, "saved retired resources can be deleted");
                TestResponse free = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Delete, "/v1.0/api/secrets/" + apiToken.Id, null, token).ConfigureAwait(false);
                Assert.Equal(204, free.StatusCode, "a secret no longer in use can be deleted");
            }
        }

        private static async Task ProfilesAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                Assert.Equal("Agent Smith", rig.Hugo.PersonaName, "default persona name");
                Assert.Equal("Mr. Anderson", rig.Hugo.UserAddress, "default user address");
                Assert.Equal("persona.agent-smith", rig.Hugo.PersonaTemplateKey, "default persona template");
                Assert.True(rig.Hugo.IsDefault && rig.Hugo.ExposeMcp && rig.Hugo.ExposeA2A && rig.Hugo.ExposeOpenAI && !rig.Hugo.ExposeManagementTools, "default exposure");
                Assert.True(rig.Hugo.MaxIterations == 12 && rig.Hugo.MaxToolCallsPerTurn == 24 && rig.Hugo.ToolTimeoutSeconds == 120, "default limits");
                Tenant tenant = (await rig.Context.Database.Tenants.ReadAsync(rig.TenantId, token).ConfigureAwait(false))!;
                Assert.Equal(rig.Hugo.Id, tenant.DefaultAgentProfileId, "tenant default profile");

                AgentProfile oncall = await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Post, "/v1.0/api/agent-profiles", new { name = "Hugo-Oncall", personaName = "Agent Jones", maxIterations = 5, instructionsAppend = "Page the on-call engineer." }, 201, token).ConfigureAwait(false);
                Assert.Equal("Agent Jones", oncall.PersonaName, "custom persona");
                Assert.Equal("Mr. Anderson", oncall.UserAddress, "default address on new profiles");
                TestResponse duplicateName = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/agent-profiles", new { name = "hugo-oncall" }, token).ConfigureAwait(false);
                Assert.Equal(409, duplicateName.StatusCode, "names are unique ignoring case");
                TestResponse badEndpoint = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + oncall.Id, new { inferenceEndpointId = "mep_nope" }, token).ConfigureAwait(false);
                Assert.Equal(400, badEndpoint.StatusCode, "unknown endpoint");

                Resource db = await rig.ResourceAsync("prod-db", token).ConfigureAwait(false);
                Binding first = await rig.BindAsync(oncall.Id, db.Id, AccessModeEnum.ReadWriteWithApproval, token).ConfigureAwait(false);
                Assert.Equal("prod-db", first.Alias, "alias defaults to the resource name");
                Assert.Equal("prod-db", first.ResourceName, "resource name on read");
                TestResponse duplicateAlias = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/agent-profiles/" + oncall.Id + "/bindings", new { resourceId = db.Id }, token).ConfigureAwait(false);
                Assert.Equal(409, duplicateAlias.StatusCode, "alias unique per profile");
                Binding second = await rig.BindAsync(oncall.Id, db.Id, AccessModeEnum.ReadOnly, token, r => r.Alias = "prod-db-ro").ConfigureAwait(false);
                Binding sameInHugo = await rig.BindAsync(rig.Hugo.Id, db.Id, AccessModeEnum.ReadOnly, token).ConfigureAwait(false);
                Assert.Equal("prod-db", sameInHugo.Alias, "two profiles bind the same resource");
                List<BindingToolRule> rules = await HttpTestHelpers.SendAsync<List<BindingToolRule>>(rig.Anderson, HttpMethod.Get, "/v1.0/api/agent-profiles/" + oncall.Id + "/bindings/" + first.Id + "/tool-rules", null, 200, token).ConfigureAwait(false);
                Assert.Equal(0, rules.Count, "no rules yet");
                Binding withRules = await HttpTestHelpers.SendAsync<Binding>(rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + oncall.Id + "/bindings/" + first.Id + "/tool-rules", new[] { new { toolName = "echo_delete", rule = "Deny" } }, 200, token).ConfigureAwait(false);
                Assert.Equal(1, withRules.ToolRules.Count, "rules replaced");
                List<Binding> listed = await HttpTestHelpers.SendAsync<List<Binding>>(rig.Anderson, HttpMethod.Get, "/v1.0/api/agent-profiles/" + oncall.Id + "/bindings", null, 200, token).ConfigureAwait(false);
                Assert.Equal(2, listed.Count, "two bindings");
                TestResponse wrongProfile = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Get, "/v1.0/api/agent-profiles/" + rig.Hugo.Id + "/bindings/" + second.Id, null, token).ConfigureAwait(false);
                Assert.Equal(404, wrongProfile.StatusCode, "a binding is addressed through its own profile");

                AgentProfile copy = await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Post, "/v1.0/api/agent-profiles/" + oncall.Id + "/duplicate", new { name = "Hugo-Oncall-2" }, 201, token).ConfigureAwait(false);
                Assert.Equal(2, (await rig.Context.Database.Bindings.ListByProfileAsync(rig.TenantId, copy.Id, token).ConfigureAwait(false)).Count, "duplicate copies bindings");
                Assert.Equal("Agent Jones", copy.PersonaName, "duplicate copies fields");
                Assert.True(!copy.IsDefault, "copies are never default");

                TestResponse deleteDefault = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Delete, "/v1.0/api/agent-profiles/" + rig.Hugo.Id, null, token).ConfigureAwait(false);
                Assert.Equal(409, deleteDefault.StatusCode, "the default profile cannot be deleted");
                await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + copy.Id, new { isDefault = true }, 200, token).ConfigureAwait(false);
                Assert.Equal(copy.Id, (await rig.Context.Database.Tenants.ReadAsync(rig.TenantId, token).ConfigureAwait(false))!.DefaultAgentProfileId, "default moved");
                AgentProfile hugoNow = await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Get, "/v1.0/api/agent-profiles/" + rig.Hugo.Id, null, 200, token).ConfigureAwait(false);
                Assert.True(!hugoNow.IsDefault, "previous default cleared");
                TestResponse deleted = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Delete, "/v1.0/api/agent-profiles/" + oncall.Id, null, token).ConfigureAwait(false);
                Assert.Equal(204, deleted.StatusCode, "delete profile");
                Assert.Equal(0, (await rig.Context.Database.Bindings.ListByProfileAsync(rig.TenantId, oncall.Id, token).ConfigureAwait(false)).Count, "bindings cascade");
                Assert.True(await rig.Context.Database.Resources.ReadAsync(rig.TenantId, db.Id, token).ConfigureAwait(false) != null, "resources are never deleted with a profile");
                Assert.True((await rig.AuditAsync(AuditEventTypeEnum.ProfileChanged, token).ConfigureAwait(false)).Count >= 4, "profile changes audited");
                Assert.True((await rig.AuditAsync(AuditEventTypeEnum.BindingChanged, token).ConfigureAwait(false)).Count >= 4, "binding changes audited");

                Tenant created = await HttpTestHelpers.SendAsync<Tenant>(rig.Host.AdminClient, HttpMethod.Post, "/v1.0/api/tenants", new { name = "acme" }, 201, token).ConfigureAwait(false);
                List<AgentProfile> acmeProfiles = await rig.Context.Database.AgentProfiles.ListAllAsync(created.Id, token).ConfigureAwait(false);
                Assert.True(acmeProfiles.Count == 1 && acmeProfiles[0].Name == "Hugo" && acmeProfiles[0].IsDefault, "a new tenant gets the Hugo profile at provisioning");
                Assert.Equal(acmeProfiles[0].Id, created.DefaultAgentProfileId, "the provisioned tenant points at it");
            }
        }

        private static async Task TemplatesAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                List<PromptTemplateView> views = await HttpTestHelpers.SendAsync<List<PromptTemplateView>>(rig.Anderson, HttpMethod.Get, "/v1.0/api/prompt-templates", null, 200, token).ConfigureAwait(false);
                foreach (string key in new[] { "instructions.root", "persona.agent-smith", "persona.neutral", "system.safety", "system.tool-usage", "system.mcp-preamble", "system.a2a-preamble", "section.resource", "a2a.skill-description", "connector.ssh.instructions", "connector.sqlite.instructions", "connector.echo.instructions" })
                {
                    PromptTemplateView? view = views.FirstOrDefault(v => v.Key == key);
                    Assert.True(view != null, "template " + key + " is listed");
                    Assert.Equal(PromptLayerEnum.Global, view!.ActiveLayer, key + " resolves to the seeded Global layer");
                    Assert.True(view.Global != null && view.Global.Exists && !view.Global.Customized, key + " Global seeded and not customized");
                }

                string key2 = "persona.agent-smith";
                PromptTemplateView saved = await HttpTestHelpers.SendAsync<PromptTemplateView>(rig.Anderson, HttpMethod.Put, "/v1.0/api/prompt-templates/" + key2, new { content = "You are {{profile.personaName}}, terse, serving {{profile.userAddress}}.", changeNote = "shorter" }, 200, token).ConfigureAwait(false);
                Assert.Equal(PromptLayerEnum.Tenant, saved.ActiveLayer, "tenant override active");
                Assert.True(saved.Tenant != null && saved.Tenant.Version == 1 && saved.Tenant.Customized, "tenant version 1");
                CompiledInstructions compiled = await CompileAsync(rig, rig.Hugo.Id, "Chat", token).ConfigureAwait(false);
                Assert.True(compiled.Text.StartsWith("You are Agent Smith, terse, serving Mr. Anderson.", StringComparison.Ordinal), "compiler uses the tenant layer: " + compiled.Text.Substring(0, 80));
                Assert.True(compiled.Segments.Any(s => s.Key == key2 && s.Layer == PromptLayerEnum.Tenant && s.Version == 1), "source map names the tenant layer");
                await HttpTestHelpers.SendAsync<PromptTemplateView>(rig.Anderson, HttpMethod.Put, "/v1.0/api/prompt-templates/" + key2, new { content = "You are {{profile.personaName}}. Version two for {{profile.userAddress}}." }, 200, token).ConfigureAwait(false);
                List<PromptTemplateVersion> versions = await HttpTestHelpers.SendAsync<List<PromptTemplateVersion>>(rig.Anderson, HttpMethod.Get, "/v1.0/api/prompt-templates/" + key2 + "/versions", null, 200, token).ConfigureAwait(false);
                Assert.True(versions.Count == 2 && versions[0].Version == 2, "two tenant versions, newest first");
                TestResponse invalid = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Put, "/v1.0/api/prompt-templates/" + key2, new { content = "{{#if x}}unclosed" }, token).ConfigureAwait(false);
                Assert.Equal(400, invalid.StatusCode, "invalid template rejected");
                List<PromptDiffLine> diff = await HttpTestHelpers.SendAsync<List<PromptDiffLine>>(rig.Anderson, HttpMethod.Get, "/v1.0/api/prompt-templates/" + key2 + "/diff", null, 200, token).ConfigureAwait(false);
                Assert.True(diff.Any(d => d.Op == "add") && diff.Any(d => d.Op == "remove"), "diff against the Global layer");

                PromptTemplateView reset = await HttpTestHelpers.SendAsync<PromptTemplateView>(rig.Anderson, HttpMethod.Post, "/v1.0/api/prompt-templates/" + key2 + "/reset?layer=Tenant", null, 200, token).ConfigureAwait(false);
                Assert.Equal(PromptLayerEnum.Global, reset.ActiveLayer, "tenant reset falls back to Global");
                Assert.True(reset.Tenant != null && !reset.Tenant.Exists, "tenant layer inactive");
                Assert.Equal(3, (await HttpTestHelpers.SendAsync<List<PromptTemplateVersion>>(rig.Anderson, HttpMethod.Get, "/v1.0/api/prompt-templates/" + key2 + "/versions", null, 200, token).ConfigureAwait(false)).Count, "reset keeps history and adds a version");
                CompiledInstructions afterReset = await CompileAsync(rig, rig.Hugo.Id, "Chat", token).ConfigureAwait(false);
                Assert.True(afterReset.Text.StartsWith("You are Agent Smith, an operations agent working for Mr. Anderson", StringComparison.Ordinal), "compiler cache invalidated by the reset");

                TestResponse notAdmin = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Put, "/v1.0/api/prompt-templates/system.safety?layer=Global", new { content = "x" }, token).ConfigureAwait(false);
                Assert.Equal(403, notAdmin.StatusCode, "the Global layer needs a system administrator");
                using (HttpClient admin = await rig.Host.ActivateSeededUserAsync("admin@hugo.local", token).ConfigureAwait(false))
                {
                    PromptTemplateView global = await HttpTestHelpers.SendAsync<PromptTemplateView>(admin, HttpMethod.Put, "/v1.0/api/prompt-templates/system.safety?layer=Global", new { content = "Global safety rules." }, 200, token).ConfigureAwait(false);
                    Assert.True(global.Global != null && global.Global.Customized && global.Global.Version == 2, "Global customized");

                    // Simulate a release that changed the Built-in after the Global layer was customized.
                    await rig.SqlAsync("UPDATE " + rig.Table("prompt_templates") + " SET base_hash = @p0 WHERE name = @p1 AND tenant_id IS NULL;", token, "old-built-in-hash", "system.safety").ConfigureAwait(false);
                    PromptTemplateView flagged = await HttpTestHelpers.SendAsync<PromptTemplateView>(rig.Anderson, HttpMethod.Get, "/v1.0/api/prompt-templates/system.safety", null, 200, token).ConfigureAwait(false);
                    Assert.True(flagged.Global!.DefaultUpdatedSinceCustomized, "built-in updated since customized is detected by hash");
                    PromptTemplateView adopted = await HttpTestHelpers.SendAsync<PromptTemplateView>(admin, HttpMethod.Post, "/v1.0/api/prompt-templates/system.safety/reset?layer=Global", null, 200, token).ConfigureAwait(false);
                    Assert.Equal(adopted.BuiltInContent, adopted.Global!.Content, "Global reset adopts the built-in");
                    Assert.True(!adopted.Global.Customized && !adopted.Global.DefaultUpdatedSinceCustomized, "flags cleared");
                }

                // An unedited Global row whose built-in changed is updated by seeding.
                PromptTemplateService service = rig.Context.Get<PromptTemplateService>();
                await rig.SqlAsync("UPDATE " + rig.Table("prompt_templates") + " SET content = @p0, base_hash = @p1 WHERE name = @p2 AND tenant_id IS NULL;", token, "old text", BuiltInTemplateCatalog.HashOf("old text"), "persona.neutral").ConfigureAwait(false);
                int changed = await service.SeedGlobalDefaultsAsync(token).ConfigureAwait(false);
                Assert.Equal(1, changed, "one unedited Global row updated");
                PromptTemplateView neutral = (await service.ViewAsync(rig.TenantId, "persona.neutral", token).ConfigureAwait(false))!;
                Assert.Equal(neutral.BuiltInContent, neutral.Global!.Content, "updated to the new built-in");

                BuiltInTemplate builtIn = await HttpTestHelpers.SendAsync<BuiltInTemplate>(rig.Anderson, HttpMethod.Get, "/v1.0/api/prompt-templates/section.resource/builtin", null, 200, token).ConfigureAwait(false);
                Assert.True(builtIn.Content.Contains("{{this.alias}}", StringComparison.Ordinal) && builtIn.Hash.Length == 64, "built-in view");
                PromptRenderResponse rendered = await HttpTestHelpers.SendAsync<PromptRenderResponse>(rig.Anderson, HttpMethod.Post, "/v1.0/api/prompt-templates/render", new { content = "{{> persona}}" }, 200, token).ConfigureAwait(false);
                Assert.True(rendered.Success && rendered.Rendered.Contains("Agent Smith", StringComparison.Ordinal), "render preview resolves includes through the layers");
                Assert.True((await rig.AuditAsync(AuditEventTypeEnum.PromptTemplateChanged, token).ConfigureAwait(false)).Count >= 4, "template changes audited");
            }
        }

        private static async Task CompilerAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                CompiledInstructions empty = await CompileAsync(rig, rig.Hugo.Id, "Chat", token).ConfigureAwait(false);
                Assert.True(empty.Text.Contains("You are Agent Smith", StringComparison.Ordinal) && empty.Text.Contains("Mr. Anderson", StringComparison.Ordinal), "persona defaults");
                Assert.True(empty.Text.Contains("No resources are attached", StringComparison.Ordinal), "no-resource text");
                Assert.True(empty.TokenEstimate > 100, "token estimate");

                Resource a = await rig.ResourceAsync("echo-a", token).ConfigureAwait(false);
                await HttpTestHelpers.SendAsync<Resource>(rig.Anderson, HttpMethod.Put, "/v1.0/api/resources/" + a.Id, new { notes = "Handle with care.", environment = "prod" }, 200, token).ConfigureAwait(false);
                Binding binding = await rig.BindAsync(rig.Hugo.Id, a.Id, AccessModeEnum.ReadOnly, token, r =>
                {
                    r.InstructionMode = InstructionModeEnum.Append;
                    r.InstructionText = "Only echo in uppercase.";
                }).ConfigureAwait(false);
                CompiledInstructions appended = await CompileAsync(rig, rig.Hugo.Id, "Chat", token).ConfigureAwait(false);
                Assert.True(!appended.FromCache, "binding change invalidates the cache");
                Assert.True(appended.Text.Contains("## echo-a (Echo (echo), environment: prod, access: ReadOnly)", StringComparison.Ordinal), "resource heading: " + appended.Text);
                int connectorText = appended.Text.IndexOf("Echo resources repeat what you send.", StringComparison.Ordinal);
                int bindingText = appended.Text.IndexOf("Only echo in uppercase.", StringComparison.Ordinal);
                Assert.True(connectorText > 0 && bindingText > connectorText, "Append places binding text after connector text");
                Assert.True(appended.Text.Contains("Operator notes: Handle with care.", StringComparison.Ordinal), "notes");
                Assert.True(appended.Text.Contains("This resource is read-only for you.", StringComparison.Ordinal), "access note");
                Assert.True(appended.Text.Contains("echo_read", StringComparison.Ordinal) && !appended.Text.Contains("Tools: echo_read, echo_write", StringComparison.Ordinal), "read-only tool list omits Write tools");
                Assert.True(appended.Segments.Any(s => s.Key == "connector.echo.instructions" && s.BindingAlias == "echo-a"), "connector segment in the source map");
                Assert.True(appended.Segments.Any(s => s.Key == "binding.instructions"), "binding segment in the source map");
                Assert.True(appended.Segments.All(s => s.Start >= 0 && s.Start + s.Length <= appended.Text.Length), "segments within the text");
                CompiledInstructions cached = await CompileAsync(rig, rig.Hugo.Id, "Chat", token).ConfigureAwait(false);
                Assert.True(cached.FromCache && cached.Text == appended.Text, "second compile is cached");

                await rig.UpdateBindingAsync(binding, r => r.InstructionMode = InstructionModeEnum.Replace, token).ConfigureAwait(false);
                CompiledInstructions replaced = await CompileAsync(rig, rig.Hugo.Id, "Chat", token).ConfigureAwait(false);
                Assert.True(!replaced.Text.Contains("Echo resources repeat what you send.", StringComparison.Ordinal) && replaced.Text.Contains("Only echo in uppercase.", StringComparison.Ordinal), "Replace drops connector text");

                CompiledInstructions mcp = await CompileAsync(rig, rig.Hugo.Id, "Mcp", token).ConfigureAwait(false);
                Assert.True(mcp.Text.Contains("You are reaching Hugo over MCP", StringComparison.Ordinal) && !replaced.Text.Contains("You are reaching Hugo over MCP", StringComparison.Ordinal), "MCP preamble only on MCP");
                CompiledInstructions a2a = await CompileAsync(rig, rig.Hugo.Id, "A2A", token).ConfigureAwait(false);
                Assert.True(a2a.Text.Contains("over the A2A protocol", StringComparison.Ordinal), "A2A preamble");

                await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + rig.Hugo.Id, new { personaName = "Agent Brown", userAddress = "Neo", instructionsAppend = "Never touch billing." }, 200, token).ConfigureAwait(false);
                CompiledInstructions renamed = await CompileAsync(rig, rig.Hugo.Id, "Chat", token).ConfigureAwait(false);
                Assert.True(renamed.Text.StartsWith("You are Agent Brown, an operations agent working for Neo", StringComparison.Ordinal), "profile change invalidates and applies");
                Assert.True(renamed.Text.TrimEnd().EndsWith("Never touch billing.", StringComparison.Ordinal), "profile append last");
                await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + rig.Hugo.Id, new { personaTemplateKey = "persona.neutral" }, 200, token).ConfigureAwait(false);
                CompiledInstructions neutral = await CompileAsync(rig, rig.Hugo.Id, "Chat", token).ConfigureAwait(false);
                Assert.True(neutral.Text.StartsWith("You are Agent Brown, an operations assistant for Neo", StringComparison.Ordinal), "persona template key selects the persona");

                TestResponse text = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Get, "/v1.0/api/agent-profiles/" + rig.Hugo.Id + "/compiled-instructions?text=true", null, token).ConfigureAwait(false);
                Assert.True(text.StatusCode == 200 && text.Body.StartsWith("You are Agent Brown", StringComparison.Ordinal), "plain text form");
                AgentCardPreview card = await HttpTestHelpers.SendAsync<AgentCardPreview>(rig.Anderson, HttpMethod.Get, "/v1.0/api/agent-profiles/" + rig.Hugo.Id + "/card", null, 200, token).ConfigureAwait(false);
                Assert.True(card.Skills.Any(s => s.Id == "agent-smith") && card.Skills.Any(s => s.Id == "echo"), "card skills per family");
            }
        }

        private static async Task ResolutionAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                Resource a = await rig.ResourceAsync("echo-a", token).ConfigureAwait(false);
                await rig.BindAsync(rig.Hugo.Id, a.Id, AccessModeEnum.ReadOnly, token).ConfigureAwait(false);
                AgentProfile other = await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Post, "/v1.0/api/agent-profiles", new { name = "Other" }, 201, token).ConfigureAwait(false);
                Resource b = await rig.ResourceAsync("echo-b", token).ConfigureAwait(false);
                await rig.BindAsync(other.Id, b.Id, AccessModeEnum.ReadOnly, token).ConfigureAwait(false);

                ToolInvocationOutcome tenantDefault = await rig.InvokeAsync("echo_read", "echo-a", null, token).ConfigureAwait(false);
                Assert.Equal(rig.Hugo.Id, tenantDefault.ProfileId, "tenant default profile");
                ToolInvocationOutcome explicitName = await rig.InvokeAsync("echo_read", "echo-b", null, token, null, null, "Other").ConfigureAwait(false);
                Assert.Equal(other.Id, explicitName.ProfileId, "explicit profile by name");
                ToolInvocationOutcome wrongProfile = await rig.InvokeAsync("echo_read", "echo-b", null, token).ConfigureAwait(false);
                Assert.Equal("binding_not_found", wrongProfile.DecisionCode, "aliases are per profile");

                await HttpTestHelpers.SendAsync<User>(rig.Anderson, HttpMethod.Put, "/v1.0/api/users/me", new { defaultAgentProfileId = other.Id }, 200, token).ConfigureAwait(false);
                ToolInvocationOutcome userDefault = await rig.InvokeAsync("echo_read", "echo-b", null, token).ConfigureAwait(false);
                Assert.Equal(other.Id, userDefault.ProfileId, "user default profile beats the tenant default");

                CredentialCreateResponse credential = await HttpTestHelpers.SendAsync<CredentialCreateResponse>(rig.Anderson, HttpMethod.Post, "/v1.0/api/tenants/" + rig.TenantId + "/credentials", new { name = "bound", agentProfileId = rig.Hugo.Id, lockToProfile = true }, 201, token).ConfigureAwait(false);
                Assert.True(credential.Credential.LockToProfile && credential.Credential.AgentProfileId == rig.Hugo.Id, "credential profile binding stored");
                TestResponse lockWithout = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/tenants/" + rig.TenantId + "/credentials", new { name = "bad", lockToProfile = true }, token).ConfigureAwait(false);
                Assert.Equal(400, lockWithout.StatusCode, "a lock needs a profile");
                using (HttpClient bound = rig.Host.CredentialClient(credential.AccessKey, credential.SecretKey))
                {
                    ToolInvocationOutcome viaCredential = await rig.InvokeAsync("echo_read", "echo-a", null, token, 200, bound).ConfigureAwait(false);
                    Assert.Equal(rig.Hugo.Id, viaCredential.ProfileId, "credential's bound profile beats the user default");
                    TestResponse locked = await HttpTestHelpers.SendAsync(bound, HttpMethod.Post, "/v1.0/api/tool-invocations", new { toolName = "echo_read", resource = "echo-b", profile = "Other" }, token).ConfigureAwait(false);
                    Assert.Equal(403, locked.StatusCode, "a locked credential cannot choose another profile");
                    Assert.True(locked.Body.Contains("ProfileLocked", StringComparison.Ordinal), "ProfileLocked code");
                }

                // ProfileUser: Execute on one profile only.
                User bob = await HttpTestHelpers.SendAsync<User>(rig.Anderson, HttpMethod.Post, "/v1.0/api/tenants/" + rig.TenantId + "/users", new { email = "bob@hugo.local", password = "Bob-Password-123", mustChangePassword = false }, 201, token).ConfigureAwait(false);
                await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/tenants/" + rig.TenantId + "/user-role-assignments", new { userId = bob.Id, roleName = "ProfileUser", resourceScope = "Resource", resourceId = other.Id }, token).ConfigureAwait(false);
                using (HttpClient bobClient = await rig.Host.UserClientAsync("bob@hugo.local", "Bob-Password-123", null, token).ConfigureAwait(false))
                {
                    ToolInvocationOutcome allowed = await rig.InvokeAsync("echo_read", "echo-b", null, token, 200, bobClient, other.Id).ConfigureAwait(false);
                    Assert.Equal(InvocationStatusEnum.Succeeded, allowed.Status, "ProfileUser executes the assigned profile: " + allowed.Reason);
                    ToolInvocationOutcome denied = await rig.InvokeAsync("echo_read", "echo-a", null, token, 200, bobClient, rig.Hugo.Id).ConfigureAwait(false);
                    Assert.Equal("forbidden", denied.DecisionCode, "ProfileUser cannot execute another profile");
                    CallerToolCatalog tools = await HttpTestHelpers.SendAsync<CallerToolCatalog>(bobClient, HttpMethod.Get, "/v1.0/api/agent-profiles/" + other.Id + "/tools", null, 200, token).ConfigureAwait(false);
                    Assert.True(tools.Tools.Any(t => t.Name == "echo_read" && t.Resources.Any(r => r.Alias == "echo-b")), "ProfileUser sees the profile's tools with the resource enum");
                    Assert.True(tools.Tools.First(t => t.Name == "echo_read").InputSchemaJson.Contains("\"enum\":[\"echo-b\"]", StringComparison.Ordinal), "resource enum in the schema");
                    TestResponse noCreate = await HttpTestHelpers.SendAsync(bobClient, HttpMethod.Post, "/v1.0/api/resources", new { name = "x", connectorType = "echo" }, token).ConfigureAwait(false);
                    Assert.Equal(403, noCreate.StatusCode, "ProfileUser cannot create resources");
                    TestResponse noApprove = await HttpTestHelpers.SendAsync(bobClient, HttpMethod.Get, "/v1.0/api/approvals", null, token).ConfigureAwait(false);
                    Assert.Equal(403, noApprove.StatusCode, "ProfileUser cannot list approvals");
                }
            }
        }

        private static async Task ScopingAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                Tenant other = await HttpTestHelpers.SendAsync<Tenant>(rig.Host.AdminClient, HttpMethod.Post, "/v1.0/api/tenants", new { name = "globex" }, 201, token).ConfigureAwait(false);
                string q = "?tenantId=" + other.Id;
                Resource foreign = await HttpTestHelpers.SendAsync<Resource>(rig.Host.AdminClient, HttpMethod.Post, "/v1.0/api/resources" + q, new { name = "foreign", connectorType = "echo" }, 201, token).ConfigureAwait(false);
                Assert.Equal(other.Id, foreign.TenantId, "administrators act on another tenant with ?tenantId");
                AgentProfile foreignProfile = (await rig.Context.Database.AgentProfiles.ListAllAsync(other.Id, token).ConfigureAwait(false)).First();
                Binding foreignBinding = await HttpTestHelpers.SendAsync<Binding>(rig.Host.AdminClient, HttpMethod.Post, "/v1.0/api/agent-profiles/" + foreignProfile.Id + "/bindings" + q, new { resourceId = foreign.Id, accessMode = "ReadWriteWithApproval" }, 201, token).ConfigureAwait(false);
                ModelEndpoint foreignEndpoint = await HttpTestHelpers.SendAsync<ModelEndpoint>(rig.Host.AdminClient, HttpMethod.Post, "/v1.0/api/model-endpoints" + q, new { name = "foreign-model", provider = "OpenAICompatible", baseUrl = "http://127.0.0.1:9", model = "m" }, 201, token).ConfigureAwait(false);
                ToolInvocationOutcome foreignCall = Serializer.Deserialize<ToolInvocationOutcome>((await HttpTestHelpers.SendAsync(rig.Host.AdminClient, HttpMethod.Post, "/v1.0/api/tool-invocations" + q, new { toolName = "echo_write", resource = "foreign", profile = foreignProfile.Id }, token).ConfigureAwait(false)).Body)!;
                Assert.Equal(InvocationDecisionEnum.ApprovalRequired, foreignCall.Decision, "foreign approval created");
                await HttpTestHelpers.SendAsync<ResourceKnowledge>(rig.Host.AdminClient, HttpMethod.Put, "/v1.0/api/resources/" + foreign.Id + "/knowledge/notes" + q, new { content = "secret plans" }, 200, token).ConfigureAwait(false);

                List<string> paths = new List<string>
                {
                    "/v1.0/api/resources/" + foreign.Id,
                    "/v1.0/api/resources/" + foreign.Id + "/referrers",
                    "/v1.0/api/resources/" + foreign.Id + "/knowledge",
                    "/v1.0/api/resources/" + foreign.Id + "/knowledge/notes/_root",
                    "/v1.0/api/agent-profiles/" + foreignProfile.Id,
                    "/v1.0/api/agent-profiles/" + foreignProfile.Id + "/bindings",
                    "/v1.0/api/agent-profiles/" + foreignProfile.Id + "/bindings/" + foreignBinding.Id,
                    "/v1.0/api/agent-profiles/" + foreignProfile.Id + "/compiled-instructions",
                    "/v1.0/api/agent-profiles/" + foreignProfile.Id + "/tools",
                    "/v1.0/api/model-endpoints/" + foreignEndpoint.Id,
                    "/v1.0/api/tool-invocations/" + foreignCall.InvocationId,
                    "/v1.0/api/approvals/" + foreignCall.ApprovalId
                };
                List<string> failures = new List<string>();
                foreach (string path in paths)
                {
                    TestResponse response = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Get, path, null, token).ConfigureAwait(false);
                    if (response.StatusCode != 404) failures.Add("GET " + path + " returned " + response.StatusCode);
                    TestResponse spoof = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Get, path + q, null, token).ConfigureAwait(false);
                    if (spoof.StatusCode != 404) failures.Add("GET " + path + q + " (tenantId query from a non-administrator) returned " + spoof.StatusCode);
                }
                foreach (string path in new[] { "/v1.0/api/resources/" + foreign.Id, "/v1.0/api/agent-profiles/" + foreignProfile.Id + "/bindings/" + foreignBinding.Id, "/v1.0/api/model-endpoints/" + foreignEndpoint.Id })
                {
                    TestResponse put = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Put, path, new { notes = "x" }, token).ConfigureAwait(false);
                    if (put.StatusCode != 404) failures.Add("PUT " + path + " returned " + put.StatusCode);
                    TestResponse delete = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Delete, path, null, token).ConfigureAwait(false);
                    if (delete.StatusCode != 404) failures.Add("DELETE " + path + " returned " + delete.StatusCode);
                }
                TestResponse approve = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/approvals/" + foreignCall.ApprovalId + "/approve", new { }, token).ConfigureAwait(false);
                if (approve.StatusCode != 404) failures.Add("approve foreign approval returned " + approve.StatusCode);
                TestResponse bindForeign = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/agent-profiles/" + rig.Hugo.Id + "/bindings", new { resourceId = foreign.Id }, token).ConfigureAwait(false);
                if (bindForeign.StatusCode != 400) failures.Add("binding a foreign resource returned " + bindForeign.StatusCode);
                TestResponse invokeForeign = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/tool-invocations", new { toolName = "echo_read", resource = "foreign", profile = foreignProfile.Id }, token).ConfigureAwait(false);
                if (invokeForeign.StatusCode != 404) failures.Add("invoking a foreign profile returned " + invokeForeign.StatusCode);
                Assert.True(failures.Count == 0, String.Join("; ", failures));

                EnumerationResult<Resource> resources = await HttpTestHelpers.SendAsync<EnumerationResult<Resource>>(rig.Anderson, HttpMethod.Get, "/v1.0/api/resources?maxResults=1000", null, 200, token).ConfigureAwait(false);
                Assert.True(resources.Objects.All(r => r.TenantId == rig.TenantId), "lists stay in the caller's tenant");
                EnumerationResult<Approval> approvals = await HttpTestHelpers.SendAsync<EnumerationResult<Approval>>(rig.Anderson, HttpMethod.Get, "/v1.0/api/approvals?maxResults=1000", null, 200, token).ConfigureAwait(false);
                Assert.Equal(0, approvals.Objects.Count, "no foreign approvals listed");
                EnumerationResult<ModelEndpoint> endpoints = await HttpTestHelpers.SendAsync<EnumerationResult<ModelEndpoint>>(rig.Anderson, HttpMethod.Get, "/v1.0/api/model-endpoints?maxResults=1000", null, 200, token).ConfigureAwait(false);
                Assert.Equal(0, endpoints.Objects.Count, "no foreign endpoints listed");

                // Private visibility inside one tenant.
                User carol = await HttpTestHelpers.SendAsync<User>(rig.Anderson, HttpMethod.Post, "/v1.0/api/tenants/" + rig.TenantId + "/users", new { email = "carol@hugo.local", password = "Carol-Password-123", mustChangePassword = false }, 201, token).ConfigureAwait(false);
                await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/tenants/" + rig.TenantId + "/user-role-assignments", new { userId = carol.Id, roleName = "Builder" }, token).ConfigureAwait(false);
                Resource privateResource = await HttpTestHelpers.SendAsync<Resource>(rig.Anderson, HttpMethod.Post, "/v1.0/api/resources", new { name = "private-one", connectorType = "echo", visibility = "Private" }, 201, token).ConfigureAwait(false);
                using (HttpClient carolClient = await rig.Host.UserClientAsync("carol@hugo.local", "Carol-Password-123", null, token).ConfigureAwait(false))
                {
                    TestResponse hidden = await HttpTestHelpers.SendAsync(carolClient, HttpMethod.Get, "/v1.0/api/resources/" + privateResource.Id, null, token).ConfigureAwait(false);
                    Assert.Equal(404, hidden.StatusCode, "another user's private resource is hidden");
                    EnumerationResult<Resource> carolList = await HttpTestHelpers.SendAsync<EnumerationResult<Resource>>(carolClient, HttpMethod.Get, "/v1.0/api/resources?maxResults=1000", null, 200, token).ConfigureAwait(false);
                    Assert.True(!carolList.Objects.Any(r => r.Id == privateResource.Id), "private resource not listed");
                }
            }
        }

        private static async Task<CompiledInstructions> CompileAsync(IntegrationRig rig, string profileId, string surface, CancellationToken token)
        {
            return await HttpTestHelpers.SendAsync<CompiledInstructions>(rig.Anderson, HttpMethod.Get, "/v1.0/api/agent-profiles/" + profileId + "/compiled-instructions?surface=" + surface, null, 200, token).ConfigureAwait(false);
        }

        #endregion
    }
}
