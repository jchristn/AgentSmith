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
    using Hugo.Core.Connectors;
    using Hugo.Core.Database;
    using Hugo.Core.Enums;
    using Hugo.Core.Instructions;
    using Hugo.Core.Models;
    using Hugo.Core.Responses;
    using Hugo.Core.Security;
    using Hugo.Core.Services;
    using Hugo.Server.Services;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;

    /// <summary>
    /// The compiled instruction golden snapshot for the joel scenario (P5.15: a profile with eight bindings across
    /// connector types, MCP surface) and the plugin loader (P3.21: a plugin assembly built by Test.Plugin is loaded,
    /// registered, executed, refused as a duplicate, and unloaded). Set HUGO_WRITE_GOLDEN=true to rewrite the snapshot.
    /// </summary>
    public static class SnapshotPluginSuite
    {
        #region Public-Members

        /// <summary>
        /// Repository-relative path of the golden snapshot.
        /// </summary>
        public static readonly string GoldenPath = Path.Combine("src", "Test.Shared", "Fixtures", "golden", "joel-mcp-instructions.md");

        #endregion

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
                string id = "integration-snapshot-" + provider.ToString().ToLowerInvariant();
                List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
                cases.Add(new TestCaseDescriptor(id, "joel-golden", "Compiled MCP instructions for a profile with eight bindings match the golden snapshot (" + provider + ")", ct => GoldenAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "plugin-loader", "A plugin assembly loads into its own context, registers, executes, is refused as a duplicate, and unloads (" + provider + ")", ct => PluginAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "plugin-server", "With Connectors.EnablePlugins the server lists the plugin connector and runs its tool through the pipeline (" + provider + ")", ct => PluginServerAsync(provider, ct)));
                suites.Add(new TestSuiteDescriptor(id, "Instruction snapshot and plugins (" + provider + ")", cases));
            }
            return suites;
        }

        #endregion

        #region Private-Methods

        private static async Task GoldenAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                DatabaseDriverBase db = rig.Context.Database;
                Tenant acme = await HttpTestHelpers.SendAsync<Tenant>(rig.Host.AdminClient, HttpMethod.Post, "/v1.0/api/tenants", new { name = "acme" }, 201, token).ConfigureAwait(false);
                AgentProfile hugo = (await db.AgentProfiles.ListAllAsync(acme.Id, token).ConfigureAwait(false)).Single(p => p.Name == "Hugo");
                User joel = new User { TenantId = acme.Id, Email = "joel@acme.example", FirstName = "Joel", LastName = "Christner", PasswordHash = "x" };
                await db.Users.CreateAsync(joel, token).ConfigureAwait(false);

                List<Scenario> scenarios = new List<Scenario>
                {
                    new Scenario("prod-db", "postgresql", "prod", "db.acme.internal:5432", AccessModeEnum.ReadOnly, "Primary in us-east-1. The orders table is partitioned by month.", "Shop OLTP database: customers, orders (partitioned by month), order_items, products. Revenue means sum(order_items.quantity * unit_price) excluding refunds."),
                    new Scenario("web-01", "ssh", "prod", "web-01.acme.internal:22", AccessModeEnum.ReadWriteWithApproval, "nginx and the checkout service run here.", null),
                    new Scenario("assets", "s3", "prod", "acme-assets", AccessModeEnum.ReadOnly, null, null),
                    new Scenario("papertrail", "papertrail", "prod", "papertrailapp.com", AccessModeEnum.ReadOnly, null, null),
                    new Scenario("legacy-db", "mysql", "prod", "legacy.acme.internal:3306", AccessModeEnum.ReadOnly, "Read-only replica of the old billing system.", null),
                    new Scenario("reports", "sqlite", "dev", "/data/reports.db", AccessModeEnum.ReadWrite, null, null),
                    new Scenario("swo", "solarwinds-observability", "prod", "na-01", AccessModeEnum.ReadOnly, null, null),
                    new Scenario("archive", "azure-blob", "prod", "acmearchive/backups", AccessModeEnum.ReadOnly, "Nightly database backups.", null)
                };
                foreach (Scenario s in scenarios)
                {
                    Resource resource = new Resource { TenantId = acme.Id, Name = s.Name, ConnectorType = s.Type, Environment = s.Environment, Endpoint = s.Endpoint, Notes = s.Notes };
                    await db.Resources.CreateAsync(resource, token).ConfigureAwait(false);
                    await db.Bindings.CreateAsync(new Binding { TenantId = acme.Id, AgentProfileId = hugo.Id, ResourceId = resource.Id, Alias = s.Name, AccessMode = s.Mode }, token).ConfigureAwait(false);
                    if (s.Knowledge != null) await db.ResourceKnowledge.UpsertAsync(new ResourceKnowledge { TenantId = acme.Id, ResourceId = resource.Id, Scope = "database", Key = "", Content = s.Knowledge, Source = Hugo.Connectors.Abstractions.KnowledgeSourceEnum.Operator }, token).ConfigureAwait(false);
                }

                RequestContext principal = new RequestContext();
                principal.IsAuthenticated = true;
                principal.TenantId = acme.Id;
                principal.PrincipalType = PrincipalTypeEnum.User;
                principal.UserId = joel.Id;
                principal.User = joel;
                principal.IsTenantAdmin = true;
                CompiledInstructions compiled = await rig.Context.Get<InstructionCompiler>().CompileAsync(acme.Id, hugo.Id, principal, InvocationSurfaceEnum.Mcp, token).ConfigureAwait(false);
                Assert.Equal(8, compiled.BindingCount, "eight bindings");
                Assert.True(compiled.Text.Contains("You are Agent Smith, an operations agent working for Mr. Anderson in the \"acme\" environment.", StringComparison.Ordinal), "persona");
                Assert.True(compiled.Text.Contains("You have access to 8 resources:", StringComparison.Ordinal), "resource count");
                Assert.True(compiled.Text.Contains("Revenue means sum(", StringComparison.Ordinal), "root knowledge included");
                Assert.True(compiled.Warnings.Count == 0, "no warnings: " + String.Join("; ", compiled.Warnings));
                Assert.True(compiled.Text.IndexOf((char)0x2014) < 0, "no em-dashes in compiled instructions");
                Assert.True(compiled.Text.Contains("call hugo_describe_resource", StringComparison.Ordinal), "eight bindings exceed the compact threshold, so connector guidance is abbreviated");
                Assert.True(compiled.Text.Length < 16000, "compact instructions stay short (" + compiled.Text.Length + " characters)");

                string path = Path.Combine(RepositoryRoot(), GoldenPath);
                if (Environment.GetEnvironmentVariable("HUGO_WRITE_GOLDEN") == "true")
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, compiled.Text);
                }
                Assert.True(File.Exists(path), GoldenPath + " is missing; run with HUGO_WRITE_GOLDEN=true.");
                string golden = File.ReadAllText(path).Replace("\r\n", "\n");
                if (golden != compiled.Text)
                {
                    string[] a = golden.Split('\n');
                    string[] b = compiled.Text.Split('\n');
                    int line = 0;
                    while (line < a.Length && line < b.Length && a[line] == b[line]) line++;
                    throw new TestAssertionException("Compiled instructions differ from " + GoldenPath + " at line " + (line + 1) + ": expected '" + (line < a.Length ? a[line] : "<end>") + "', got '" + (line < b.Length ? b[line] : "<end>") + "'. Review and rerun with HUGO_WRITE_GOLDEN=true when the change is intended.");
                }
            }
        }

        private static async Task PluginAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            string root = PluginDirectory();
            try
            {
                await using (DatabaseDriverBase db = await TestEnvironment.CreateDriverAsync(provider, token).ConfigureAwait(false))
                {
                    using (ConnectorRegistry registry = new ConnectorRegistry())
                    {
                        ConnectorRegistrations.RegisterBuiltIns(registry);
                        AuditService audit = new AuditService(db);
                        LoggingModule logging = new LoggingModule();
                        logging.Settings.EnableConsole = false;
                        using (PluginLoader loader = new PluginLoader(db, registry, audit, logging))
                        {
                            List<PluginRecord> records = await loader.LoadAllAsync(root, token).ConfigureAwait(false);
                            PluginRecord loaded = records.Single(r => r.Name == "Test.Plugin");
                            Assert.True(loaded.Loaded && loaded.ConnectorTypes.SequenceEqual(new[] { "plugin-echo" }), "plugin loaded: " + loaded.LoadError);
                            Assert.True(!String.IsNullOrEmpty(loaded.Sha256), "assembly hash recorded");
                            PluginRecord duplicate = records.Single(r => r.Name == "Zz.Duplicate");
                            Assert.True(!duplicate.Loaded && duplicate.LoadError!.Contains("already registered", StringComparison.Ordinal), "duplicate connector type refused: " + duplicate.LoadError);
                            PluginRecord missing = records.Single(r => r.Name == "Empty");
                            Assert.True(!missing.Loaded && missing.LoadError != null, "directory without its assembly refused");

                            IConnector connector = registry.Get("plugin-echo") ?? throw new TestAssertionException("plugin connector not registered");
                            Assert.True(connector.GetType().Assembly != typeof(SnapshotPluginSuite).Assembly, "loaded from the plugin assembly");
                            IConnectorTool tool = connector.Tools.Single(t => t.Descriptor.Name == "plugecho_read");
                            ConnectorInvocationContext context = new ConnectorInvocationContext();
                            context.Resource = new ConnectorResourceContext { Name = "p", ConnectorType = "plugin-echo" };
                            ToolResult result = await tool.ExecuteAsync(context, new ToolArguments("{\"text\":\"hi\"}"), token).ConfigureAwait(false);
                            Assert.Equal("plugin:hi", result.Text, "plugin tool executes");
                            Assert.True(loader.LoadedTypes().Contains("plugin-echo"), "loaded types");

                            Assert.True(await loader.UnloadAsync("Test.Plugin", token).ConfigureAwait(false), "unload");
                            Assert.True(registry.Get("plugin-echo") == null, "unregistered on unload");
                            PluginRecord after = (await db.Plugins.ReadByNameAsync("Test.Plugin", token).ConfigureAwait(false))!;
                            Assert.True(!after.Loaded, "record shows unloaded");
                            Hugo.Core.Requests.AuditFilter filter = new Hugo.Core.Requests.AuditFilter { EventType = AuditEventTypeEnum.PluginLoaded, PageSize = 100 };
                            Assert.True((await db.Audit.EnumerateAsync(filter, token).ConfigureAwait(false)).Objects.Count >= 4, "loads, refusals, and unloads audited");
                        }
                    }
                }
            }
            finally
            {
                TryDelete(root);
            }
        }

        private static async Task PluginServerAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            string root = PluginDirectory();
            try
            {
                await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, s =>
                {
                    s.Connectors.EnablePlugins = true;
                    s.Connectors.PluginDirectory = root;
                }, token).ConfigureAwait(false))
                {
                    ConnectorView view = await HttpTestHelpers.SendAsync<ConnectorView>(rig.Anderson, HttpMethod.Get, "/v1.0/api/connectors/plugin-echo", null, 200, token).ConfigureAwait(false);
                    Assert.True(view.Plugin && view.Tools.Any(t => t.Name == "plugecho_read"), "plugin connector listed");
                    Resource resource = await rig.ResourceAsync("plugged", token, "plugin-echo").ConfigureAwait(false);
                    await rig.BindAsync(rig.Hugo.Id, resource.Id, AccessModeEnum.ReadOnly, token).ConfigureAwait(false);
                    ToolInvocationOutcome outcome = await rig.InvokeAsync("plugecho_read", "plugged", new { text = "through the pipeline" }, token).ConfigureAwait(false);
                    Assert.Equal("plugin:through the pipeline", outcome.Result?.Text, "plugin tool through the pipeline");
                }
            }
            finally
            {
                TryDelete(root);
            }
        }

        private static string PluginDirectory()
        {
            string baseDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            string tfm = Path.GetFileName(baseDirectory);
            string configuration = Path.GetFileName(Path.GetDirectoryName(baseDirectory)!);
            string output = Path.Combine(RepositoryRoot(), "src", "Test.Plugin", "bin", configuration, tfm);
            if (!File.Exists(Path.Combine(output, "Test.Plugin.dll"))) throw new TestAssertionException("Test.Plugin.dll was not built at " + output + "; build the solution first.");
            string root = Path.Combine(Path.GetTempPath(), "hugo-plugins-" + Guid.NewGuid().ToString("N"));
            string plugin = Path.Combine(root, "Test.Plugin");
            Directory.CreateDirectory(plugin);
            foreach (string file in Directory.GetFiles(output, "Test.Plugin.*")) File.Copy(file, Path.Combine(plugin, Path.GetFileName(file)));
            string duplicate = Path.Combine(root, "Zz.Duplicate");
            Directory.CreateDirectory(duplicate);
            File.Copy(Path.Combine(output, "Test.Plugin.dll"), Path.Combine(duplicate, "Zz.Duplicate.dll"));
            Directory.CreateDirectory(Path.Combine(root, "Empty"));
            return root;
        }

        private static void TryDelete(string directory)
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static string RepositoryRoot()
        {
            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "src", "Hugo.sln"))) return directory.FullName;
                directory = directory.Parent;
            }
            throw new TestAssertionException("Could not find the repository root (src/Hugo.sln) above " + AppContext.BaseDirectory + ".");
        }

        #endregion
    }
}
