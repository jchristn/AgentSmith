namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Enums;
    using Hugo.Core.Serialization;
    using Hugo.Core.Telemetry;
    using Hugo.Server.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;

    /// <summary>
    /// Original Garrison LabelGuard acceptance plus Hugo's strict enum and settings compatibility checks (P0.16).
    /// TemplateRenderer's original eleven cases are registered separately in the templates suite.
    /// </summary>
    public static class HelperPortSuite
    {
        /// <summary>
        /// Build the helper acceptance suite.
        /// </summary>
        /// <returns>Suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            return new List<TestSuiteDescriptor>
            {
                new TestSuiteDescriptor("helper-ports", "Foundation helper provenance and compatibility", new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("helper-ports", "label-guard", "Original Garrison label cardinality and limit tests plus reset and callback isolation", LabelGuardAsync),
                    new TestCaseDescriptor("helper-ports", "strict-enums", "Hugo API enums require defined names, nullable values round trip, external and file settings remain compatible", StrictEnumsAsync)
                })
            };
        }

        private static Task LabelGuardAsync(CancellationToken token)
        {
            // Port of Garrison TelemetryCoverageSuite.LabelGuardAsync at the recorded fork commit (MIT).
            token.ThrowIfCancellationRequested();
            LabelGuard guard = new LabelGuard();
            guard.MaxLabelValues = 10;
            int folded = 0;
            guard.OnFolded = key => Interlocked.Increment(ref folded);
            for (int i = 0; i < 10; i++) Assert.Equal("agent-" + i, guard.Fold("agent", "agent-" + i), "Values within the limit keep their own series");
            Assert.Equal("other", guard.Fold("agent", "agent-10"), "The eleventh value folds");
            Assert.Equal("agent-3", guard.Fold("agent", "agent-3"), "Values already seen still pass");
            Assert.Equal("none", guard.Fold("agent", null), "Empty values are reported as none");
            Assert.Equal("tenant-x", guard.Fold("tenant", "tenant-x"), "Each label key has its own limit");
            Assert.Equal(1, folded, "Folded values are counted");
            guard.MaxLabelValues = 1;
            Assert.Equal(10, guard.MaxLabelValues, "The limit has a minimum of 10");
            guard.MaxLabelValues = 1000000;
            Assert.Equal(10000, guard.MaxLabelValues, "The limit has a maximum of 10000");
            Assert.Equal(10, guard.Count("agent"), "Folded values do not grow the remembered set");
            Assert.Equal("none", guard.Fold("agent", String.Empty), "Empty strings use the empty sentinel");
            Assert.Equal("unbounded", guard.Fold(String.Empty, "unbounded"), "Empty keys pass through");
            guard.Reset();
            Assert.Equal(0, guard.Count("agent"), "Reset clears every key");
            guard.MaxLabelValues = 10;
            for (int i = 0; i < 10; i++) guard.Fold("callback", i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            guard.OnFolded = key => throw new InvalidOperationException("Callback failure must not break telemetry.");
            Assert.Equal("other", guard.Fold("callback", "extra"), "Callback exceptions stay isolated");
            return Task.CompletedTask;
        }

        private static Task StrictEnumsAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Assert.Equal(ResourceTypeEnum.Tenant, Serializer.Deserialize<ResourceTypeEnum>("\"tenant\""), "Case-insensitive API enum names");
            Assert.Equal("\"Tenant\"", Serializer.Serialize(ResourceTypeEnum.Tenant), "Canonical enum output");
            Assert.Equal<ResourceTypeEnum?>(null, Serializer.Deserialize<ResourceTypeEnum?>("null"), "Nullable enum null");
            Assert.Equal<ResourceTypeEnum?>(ResourceTypeEnum.Tenant, Serializer.Deserialize<ResourceTypeEnum?>("\"Tenant\""), "Nullable enum name");
            foreach (string invalid in new[] { "0", "999", "\"0\"", "\"999\"", "\"Unknown\"", "true", "{}", "null" })
            {
                Reject(() => Serializer.Deserialize<ResourceTypeEnum>(invalid), "Invalid API enum input " + invalid);
            }
            Reject(() => Serializer.Serialize((ResourceTypeEnum)999), "Undefined enum output");
            // Third-party contracts and historical file settings keep their original numeric behavior.
            Assert.Equal(HttpStatusCode.OK, Serializer.Deserialize<HttpStatusCode>("200"), "External enum numeric compatibility");
            Assert.Equal(ResourceTypeEnum.Tenant, JsonSerializer.Deserialize<ResourceTypeEnum>("1", Serializer.FileOptions), "Historical numeric file enum compatibility");
            string? repository = FindRepository();
            Assert.True(repository != null, "Repository settings fixture is available");
            HugoSettings settings = Assert.NotNull(JsonSerializer.Deserialize<HugoSettings>(File.ReadAllText(Path.Combine(repository!, "src", "Hugo.Server", "hugo.json")), Serializer.FileOptions), "Shipped settings");
            Assert.Equal(1, settings.Logging.MinimumSeverity, "Shipped numeric logging severity remains loadable");
            Assert.Equal(settings.Database.Type, Assert.NotNull(JsonSerializer.Deserialize<HugoSettings>(JsonSerializer.Serialize(settings, Serializer.FileOptions), Serializer.FileOptions), "Settings round trip").Database.Type, "Settings enum round trip");
            return Task.CompletedTask;
        }

        private static string? FindRepository()
        {
            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "src", "Hugo.Server", "hugo.json"))) return directory.FullName;
                directory = directory.Parent;
            }
            return null;
        }

        private static void Reject(Action action, string message)
        {
            bool rejected = false;
            try { action(); }
            catch (JsonException) { rejected = true; }
            Assert.True(rejected, message);
        }
    }
}
