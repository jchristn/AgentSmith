namespace Test.Shared.Integration
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Connectors;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Profiles;
    using Hugo.Core.Prompts;
    using Hugo.Core.Safety;
    using Hugo.Server.Services;
    using Test.Shared.Connectors.Echo;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;

    /// <summary>
    /// Unit suites for the integration layer: argument validation (P3.10), primary arguments and deny patterns, tool
    /// visibility (P5.16), the resource argument schema, the built-in template catalog (P5.3), template diffs, the session
    /// pool and concurrency gates (P3.9), and the built-in connector registrations (P3.3).
    /// </summary>
    public static class IntegrationUnitSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the suites.
        /// </summary>
        /// <returns>Suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            string id = "integration-unit";
            cases.Add(new TestCaseDescriptor(id, "argument-validator", "Tool arguments are validated against the schema subset (types, required, enum, bounds, items, additionalProperties, pattern)", ValidatorAsync));
            cases.Add(new TestCaseDescriptor(id, "deny-patterns", "Primary argument detection and deny pattern matching, validation, and fail-closed timeouts", DenyPatternsAsync));
            cases.Add(new TestCaseDescriptor(id, "visibility", "Tool visibility: ReadOnly hides Write and Destructive base tools, keeps classified tools, rules hide, RequiresModel hides, resource enum per tool", VisibilityAsync));
            cases.Add(new TestCaseDescriptor(id, "builtin-catalog", "Built-in templates load from Hugo.Core and connector descriptors with content hashes; diffs", CatalogAsync));
            cases.Add(new TestCaseDescriptor(id, "session-pool", "Session pool creates once per key, invalidates by resource, sweeps idle sessions, and gates concurrency", PoolAsync));
            cases.Add(new TestCaseDescriptor(id, "registrations", "Every wave 1 connector registers with valid tool names and schemas", RegistrationsAsync));
            return new List<TestSuiteDescriptor> { new TestSuiteDescriptor(id, "Integration unit", cases) };
        }

        #endregion

        #region Private-Methods

        private static Task ValidatorAsync(CancellationToken token)
        {
            string schema = "{\"type\":\"object\",\"properties\":{"
                + "\"sql\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":20},"
                + "\"n\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":10},"
                + "\"mode\":{\"type\":\"string\",\"enum\":[\"a\",\"b\"]},"
                + "\"tags\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"maxItems\":2},"
                + "\"opt\":{\"type\":[\"string\",\"null\"]},"
                + "\"code\":{\"type\":\"string\",\"pattern\":\"^[A-Z]{3}$\"},"
                + "\"obj\":{\"type\":\"object\",\"properties\":{\"k\":{\"type\":\"boolean\"}},\"required\":[\"k\"]}"
                + "},\"required\":[\"sql\"],\"additionalProperties\":false}";
            Assert.Equal(0, ToolArgumentValidator.Validate(schema, "{\"sql\":\"select 1\",\"n\":3,\"mode\":\"a\",\"tags\":[\"x\"],\"opt\":null,\"code\":\"ABC\",\"obj\":{\"k\":true}}").Count, "valid");
            Assert.Equal(0, ToolArgumentValidator.Validate(schema, "{\"sql\":\"x\",\"resource\":\"db\"}", "resource").Count, "ignored resource argument");
            Assert.True(ToolArgumentValidator.Validate(schema, "{}").Any(e => e.Contains("sql", StringComparison.Ordinal)), "required");
            Assert.True(ToolArgumentValidator.Validate(schema, "{\"sql\":5}").Count == 1, "type");
            Assert.True(ToolArgumentValidator.Validate(schema, "{\"sql\":\"x\",\"n\":11}").Count == 1, "maximum");
            Assert.True(ToolArgumentValidator.Validate(schema, "{\"sql\":\"x\",\"n\":1.5}").Count == 1, "integer");
            Assert.True(ToolArgumentValidator.Validate(schema, "{\"sql\":\"x\",\"mode\":\"c\"}").Count == 1, "enum");
            Assert.True(ToolArgumentValidator.Validate(schema, "{\"sql\":\"x\",\"tags\":[\"a\",\"b\",\"c\"]}").Count == 1, "maxItems");
            Assert.True(ToolArgumentValidator.Validate(schema, "{\"sql\":\"x\",\"tags\":[1]}").Count == 1, "items type");
            Assert.True(ToolArgumentValidator.Validate(schema, "{\"sql\":\"x\",\"code\":\"abc\"}").Count == 1, "pattern");
            Assert.True(ToolArgumentValidator.Validate(schema, "{\"sql\":\"x\",\"obj\":{}}").Count == 1, "nested required");
            Assert.True(ToolArgumentValidator.Validate(schema, "{\"sql\":\"x\",\"extra\":1}").Count == 1, "additionalProperties false");
            Assert.True(ToolArgumentValidator.Validate(schema, "{\"sql\":\"" + new string('x', 30) + "\"}").Count == 1, "maxLength");
            Assert.True(ToolArgumentValidator.Validate(schema, "[1]").Count == 1, "not an object");
            Assert.True(ToolArgumentValidator.Validate(schema, "{bad").Count == 1, "invalid JSON");
            Assert.Equal(0, ToolArgumentValidator.Validate("{\"type\":\"object\"}", "{\"anything\":true}").Count, "open schema");
            return Task.CompletedTask;
        }

        private static Task DenyPatternsAsync(CancellationToken token)
        {
            Assert.Equal("command", ToolArgumentValidator.PrimaryArgument("{\"type\":\"object\",\"properties\":{\"timeout\":{\"type\":\"integer\"},\"command\":{\"type\":\"string\"}},\"required\":[\"command\"]}"), "command by convention");
            Assert.Equal("sql", ToolArgumentValidator.PrimaryArgument("{\"type\":\"object\",\"properties\":{\"sql\":{\"type\":\"string\"},\"parameters\":{\"type\":\"object\"}},\"required\":[\"sql\"]}"), "sql by convention");
            Assert.Equal("path", ToolArgumentValidator.PrimaryArgument("{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"content\":{\"type\":\"string\"}},\"required\":[\"path\",\"content\"]}"), "first required string");
            Assert.Equal("target", ToolArgumentValidator.PrimaryArgument("{\"type\":\"object\",\"properties\":{\"command\":{\"type\":\"string\"},\"target\":{\"type\":\"string\",\"x-hugo-primary\":true}}}"), "x-hugo-primary wins");
            Assert.True(ToolArgumentValidator.PrimaryArgument("{\"type\":\"object\",\"properties\":{\"n\":{\"type\":\"integer\"}}}") == null, "no primary");
            List<string> patterns = new List<string> { "rm\\s+-rf", "(?i)drop\\s+table" };
            Assert.Equal("rm\\s+-rf", DenyPatternMatcher.FirstMatch(patterns, "sudo RM -RF /"), "case-insensitive match");
            Assert.True(DenyPatternMatcher.FirstMatch(patterns, "ls -la") == null, "no match");
            Assert.True(DenyPatternMatcher.FirstMatch(patterns, null) == null, "null value");
            Assert.True(DenyPatternMatcher.Validate(new[] { "ok", "([" }).Count == 1, "invalid regex reported");
            Assert.True(DenyPatternMatcher.Validate(new[] { "" }).Count == 1, "empty pattern reported");
            Assert.True(DenyPatternMatcher.Validate(Enumerable.Repeat("x", 65)).Count == 1, "too many patterns");
            Assert.True(DenyPatternMatcher.FirstMatch(new[] { "(" }, "anything") != null, "an invalid stored pattern fails closed");
            return Task.CompletedTask;
        }

        private static Task VisibilityAsync(CancellationToken token)
        {
            EchoConnector echo = new EchoConnector();
            BoundResource readOnly = Bound(echo, "ro", AccessModeEnum.ReadOnly, null);
            BoundResource readWrite = Bound(echo, "rw", AccessModeEnum.ReadWrite, null);
            List<CallerTool> onlyRo = ToolVisibilityFilter.Build(new[] { readOnly }, false);
            Assert.True(onlyRo.Any(t => t.Name == "echo_read"), "read tool listed");
            Assert.True(!onlyRo.Any(t => t.Name == "echo_write" || t.Name == "echo_delete"), "Write and Destructive base tools hidden when every binding is ReadOnly");
            Assert.True(onlyRo.Any(t => t.Name == "echo_classify"), "classified tools stay listed");
            Assert.True(!onlyRo.Any(t => t.Name == "echo_model"), "RequiresModel hidden without a model");
            Assert.True(ToolVisibilityFilter.Build(new[] { readOnly }, true).Any(t => t.Name == "echo_model"), "RequiresModel listed with a model");
            List<CallerTool> both = ToolVisibilityFilter.Build(new[] { readOnly, readWrite }, false);
            CallerTool write = both.First(t => t.Name == "echo_write");
            Assert.True(write.Resources.Count == 1 && write.Resources[0].Alias == "rw", "Write tool offered only on the ReadWrite binding");
            Assert.True(write.InputSchemaJson.Contains("\"enum\":[\"rw\"]", StringComparison.Ordinal) && write.InputSchemaJson.Contains("\"required\":[\"resource\"", StringComparison.Ordinal), "resource enum and required: " + write.InputSchemaJson);
            Assert.Equal(2, both.First(t => t.Name == "echo_read").Resources.Count, "read tool on both");
            Assert.True(both.First(t => t.Name == "echo_read").KnownEffect == EffectEnum.Read && both.First(t => t.Name == "echo_classify").KnownEffect == null, "known effects");
            Assert.True(both.First(t => t.Name == "echo_classify").Description.Contains("ro (ReadOnly)", StringComparison.Ordinal), "description notes access modes");
            BoundResource denied = Bound(echo, "denied", AccessModeEnum.ReadWrite, new BindingToolRule { ToolName = "echo_read", Rule = ToolRuleEnum.Deny });
            Assert.True(!ToolVisibilityFilter.Build(new[] { denied }, false).Any(t => t.Name == "echo_read"), "a tool denied on every binding is hidden");
            string schema = ToolVisibilityFilter.WithResourceArgument("{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"string\"}},\"required\":[\"x\"]}", new List<string> { "a", "b" });
            Assert.True(schema.StartsWith("{\"type\":\"object\",\"properties\":{\"resource\"", StringComparison.Ordinal) && schema.Contains("\"required\":[\"resource\",\"x\"]", StringComparison.Ordinal), "resource first: " + schema);
            return Task.CompletedTask;
        }

        private static Task CatalogAsync(CancellationToken token)
        {
            BuiltInTemplateCatalog catalog = new BuiltInTemplateCatalog();
            foreach (string key in new[] { "instructions.root", "persona.agent-smith", "persona.neutral", "system.safety", "system.tool-usage", "system.mcp-preamble", "system.a2a-preamble", "section.resource", "a2a.skill-description" })
            {
                BuiltInTemplate? template = catalog.Get(key);
                Assert.True(template != null && template.Content.Length > 0 && template.Hash == BuiltInTemplateCatalog.HashOf(template.Content), "embedded " + key);
            }
            Assert.True(catalog.Get("instructions.root")!.Content.Contains("{{> persona}}", StringComparison.Ordinal), "root includes the persona");
            Assert.True(catalog.Get("persona.agent-smith")!.Content.Contains("{{profile.personaName}}", StringComparison.Ordinal), "persona variables");
            catalog.RegisterConnectors(ConnectorRegistrations.BuiltIns());
            foreach (string type in new[] { "ssh", "s3", "azure-blob", "gcs", "sqlite", "mysql", "sqlserver", "postgresql", "papertrail", "solarwinds-observability" })
            {
                BuiltInTemplate? template = catalog.Get("connector." + type + ".instructions");
                Assert.True(template != null && template.Source == type && template.Content.Length > 200, "connector instructions for " + type);
            }
            Assert.Equal(BuiltInTemplateCatalog.HashOf("a\r\nb"), BuiltInTemplateCatalog.HashOf("a\nb"), "line endings normalized");
            List<PromptDiffLine> diff = PromptTemplateService.Diff("a\nb\nc", "a\nx\nc");
            Assert.Equal("equal,remove,add,equal", String.Join(",", diff.Select(d => d.Op)), "diff ops");
            Assert.True(PromptTemplateService.IsValidKey("connector.azure-blob.instructions") && !PromptTemplateService.IsValidKey("Bad Key") && !PromptTemplateService.IsValidKey(".x"), "key rules");
            return Task.CompletedTask;
        }

        private static async Task PoolAsync(CancellationToken token)
        {
            using (ConnectorSessionPool pool = new ConnectorSessionPool())
            {
                int created = 0;
                Func<CancellationToken, Task<FakeSession>> factory = ct =>
                {
                    Interlocked.Increment(ref created);
                    return Task.FromResult(new FakeSession());
                };
                FakeSession first = await pool.GetOrCreateAsync("echo:res_1:hash", factory, token).ConfigureAwait(false);
                FakeSession again = await pool.GetOrCreateAsync("echo:res_1:hash", factory, token).ConfigureAwait(false);
                Assert.True(ReferenceEquals(first, again) && created == 1, "one session per key");
                await Task.WhenAll(Enumerable.Range(0, 10).Select(i => pool.GetOrCreateAsync("echo:res_2:hash", factory, token))).ConfigureAwait(false);
                Assert.Equal(2, created, "concurrent creates for one key make one session");
                Assert.Equal(1, pool.InvalidateResource("res_1"), "invalidate by resource");
                Assert.True(first.Disposed, "invalidated session disposed");
                pool.IdleTimeout = TimeSpan.FromSeconds(1);
                await Task.Delay(1100, token).ConfigureAwait(false);
                Assert.Equal(1, pool.Sweep(), "idle session swept");
                Assert.Equal(0, pool.Count, "pool empty");

                pool.MaxConcurrencyPerResource = () => 2;
                ResourceGateLease one = await pool.AcquireAsync("res_gate", token).ConfigureAwait(false);
                ResourceGateLease two = await pool.AcquireAsync("res_gate", token).ConfigureAwait(false);
                Task<ResourceGateLease> third = pool.AcquireAsync("res_gate", token);
                await Task.Delay(100, token).ConfigureAwait(false);
                Assert.True(!third.IsCompleted, "third caller waits at the cap");
                one.Dispose();
                ResourceGateLease three = await third.WaitAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
                two.Dispose();
                three.Dispose();
                three.Dispose();
            }
        }

        private static Task RegistrationsAsync(CancellationToken token)
        {
            using (ConnectorRegistry registry = new ConnectorRegistry())
            {
                ConnectorRegistrations.RegisterBuiltIns(registry);
                List<string> types = registry.All().Select(c => c.Descriptor.Type).OrderBy(t => t, StringComparer.Ordinal).ToList();
                foreach (string type in new[] { "azure-blob", "gcs", "mysql", "papertrail", "postgresql", "s3", "solarwinds-observability", "sqlite", "sqlserver", "ssh" }) Assert.True(types.Contains(type), "wave 1 connector " + type);
                Assert.Equal(types.Count, types.Distinct(StringComparer.Ordinal).Count(), "unique types");
                Assert.True(registry.FamilyTools("sql").Any(t => t.Descriptor.Name == "sql_query"), "family tools");
                bool duplicate = false;
                try
                {
                    registry.Register(new Hugo.Connectors.Ssh.SshConnector());
                }
                catch (InvalidOperationException)
                {
                    duplicate = true;
                }
                Assert.True(duplicate, "duplicate type refused");
            }
            return Task.CompletedTask;
        }

        private static BoundResource Bound(IConnector connector, string alias, AccessModeEnum mode, BindingToolRule? rule)
        {
            BoundResource bound = new BoundResource();
            Binding binding = new Binding();
            binding.Alias = alias;
            binding.AccessMode = mode;
            if (rule != null) binding.ToolRules.Add(rule);
            bound.Binding = binding;
            Resource resource = new Resource();
            resource.Name = alias;
            resource.ConnectorType = connector.Descriptor.Type;
            bound.Resource = resource;
            bound.Connector = connector;
            return bound;
        }

        #endregion
    }
}
