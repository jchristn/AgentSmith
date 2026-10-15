namespace Test.Shared.Connectors
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Ssh.Classification;
    using Test.Shared.Connectors.Ssh;
    using Touchstone.Core;

    /// <summary>
    /// Suites for the Ssh connector family. Unit suites always run; testbed suites run when HUGO_TESTBED=1.
    /// </summary>
    public static class SshSuites
    {
        /// <summary>
        /// Build the suites.
        /// </summary>
        /// <returns>Suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            return new List<TestSuiteDescriptor>
            {
                new TestSuiteDescriptor(SshUnitTests.SuiteId, "SSH connector unit tests", SshUnitTests.Build()),
                ClassifierSuite(),
                SshTestbedTests.Build()
            };
        }

        private static TestSuiteDescriptor ClassifierSuite()
        {
            const string suiteId = "connectors.ssh.classifier";
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            List<SshClassifierCase> table = SshClassifierTable.Cases();
            for (int i = 0; i < table.Count; i++)
            {
                SshClassifierCase row = table[i];
                string id = "case-" + (i + 1).ToString("D3", CultureInfo.InvariantCulture);
                string display = row.Expected + ": " + row.Command.Replace("\n", "\\n", StringComparison.Ordinal);
                cases.Add(new TestCaseDescriptor(suiteId, id, display, ct =>
                {
                    EffectClassification c = SshCommandClassifier.Classify(row.Command, row.Options);
                    string actual = c.Rejected ? "X" : c.Effect == EffectEnum.Read ? "R" : c.Effect == EffectEnum.Write ? "W" : "D";
                    if (actual != row.Expected)
                        throw new InvalidOperationException("Expected " + row.Expected + " but classified " + actual + " (" + c.Reason + ") for: " + row.Command);
                    if (String.IsNullOrEmpty(c.Reason)) throw new InvalidOperationException("Classification has no reason for: " + row.Command);
                    return Task.CompletedTask;
                }));
            }
            cases.Add(new TestCaseDescriptor(suiteId, "table-size", "The table has at least 150 cases", ct =>
            {
                if (table.Count < 150) throw new InvalidOperationException("Only " + table.Count + " classifier cases.");
                return Task.CompletedTask;
            }));
            return new TestSuiteDescriptor(suiteId, "SSH command classifier table", cases);
        }
    }
}
