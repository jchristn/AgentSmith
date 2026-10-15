namespace Test.Shared.Connectors
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Kubernetes;
    using Test.Shared.Connectors.Kubernetes;
    using Touchstone.Core;

    /// <summary>
    /// Suites for the Kubernetes connector: <c>connectors.kubernetes</c> and <c>connectors.kubernetes.manifest</c>
    /// (offline), and <c>testbed.kubernetes</c> (k3s, when <c>HUGO_TESTBED=1</c> and the testbed is up).
    /// </summary>
    public static class KubernetesSuites
    {
        /// <summary>
        /// Build the suites.
        /// </summary>
        /// <returns>Suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            return new List<TestSuiteDescriptor>
            {
                new TestSuiteDescriptor(KubernetesUnitTests.SuiteId, "Kubernetes connector unit tests", KubernetesUnitTests.Build()),
                ManifestSuite(),
                KubernetesTestbedTests.Build()
            };
        }

        private static TestSuiteDescriptor ManifestSuite()
        {
            const string suiteId = "connectors.kubernetes.manifest";
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            List<KubernetesManifestCase> table = KubernetesManifestTable.Cases();
            for (int i = 0; i < table.Count; i++)
            {
                KubernetesManifestCase row = table[i];
                string id = "case-" + (i + 1).ToString("D3", CultureInfo.InvariantCulture);
                cases.Add(new TestCaseDescriptor(suiteId, id, row.Expected + ": " + row.Name, ct =>
                {
                    KubernetesOptions options = KubernetesOptions.FromSettings(new ResourceSettings(row.Settings));
                    EffectClassification c = KubernetesManifestClassifier.Classify(row.Manifest, options, row.Namespace, row.DryRun, row.Force, row.AllowFieldRemoval, "k8s-test");
                    string actual = c.Rejected ? "X" : c.Effect == EffectEnum.Read ? "R" : c.Effect == EffectEnum.Write ? "W" : "D";
                    if (actual != row.Expected) throw new InvalidOperationException("Expected " + row.Expected + " but classified " + actual + " (" + c.Reason + ") for: " + row.Name);
                    if (String.IsNullOrEmpty(c.Reason)) throw new InvalidOperationException("Classification has no reason for: " + row.Name);
                    if (String.IsNullOrEmpty(c.ApprovalSummary) || !c.ApprovalSummary.Contains("k8s-test", StringComparison.Ordinal)) throw new InvalidOperationException("Summary does not name the target for: " + row.Name);
                    return Task.CompletedTask;
                }));
            }
            cases.Add(new TestCaseDescriptor(suiteId, "table-size", "The table has at least 40 cases", ct =>
            {
                if (table.Count < 40) throw new InvalidOperationException("Only " + table.Count + " manifest cases.");
                return Task.CompletedTask;
            }));
            return new TestSuiteDescriptor(suiteId, "Kubernetes apply manifest classification table", cases);
        }
    }
}
