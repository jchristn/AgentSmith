namespace Test.Shared.Connectors
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Http;
    using Test.Shared.Connectors.Http;
    using Touchstone.Core;

    /// <summary>
    /// Suites for the generic HTTP connector. They run against an in-process stub server on 127.0.0.1, so they always run.
    /// </summary>
    public static class HttpSuites
    {
        #region Public-Methods

        /// <summary>
        /// Build the suites.
        /// </summary>
        /// <returns>Suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            const string unitId = "connectors.http";
            List<TestCaseDescriptor> cases = HttpUnitCases.Build(unitId);
            cases.AddRange(HttpStubCases.Build(unitId));
            return new List<TestSuiteDescriptor>
            {
                new TestSuiteDescriptor(unitId, "HTTP connector: settings, auth, allow lists, redirects, limits, and health checks against HttpStubServer", cases),
                ClassifierSuite()
            };
        }

        #endregion

        #region Private-Methods

        private static TestSuiteDescriptor ClassifierSuite()
        {
            const string suiteId = "connectors.http.classifier";
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            List<HttpClassifierCase> table = HttpClassifierTable.Cases();
            for (int i = 0; i < table.Count; i++)
            {
                HttpClassifierCase row = table[i];
                string id = "case-" + (i + 1).ToString("D3", CultureInfo.InvariantCulture);
                string display = row.Expected + ": [" + row.Variant + "] " + (row.Method ?? "(none)") + " " + (row.Path ?? "(none)").Replace("\n", "\\n", StringComparison.Ordinal);
                cases.Add(new TestCaseDescriptor(suiteId, id, display, ct =>
                {
                    HttpResourceOptions options = HttpResourceOptions.FromSettings(new ResourceSettings(row.Settings));
                    EffectClassification c = HttpRequestClassifier.Classify(row.Method, row.Path, options, "api");
                    string actual = c.Rejected ? "X" : c.Effect == EffectEnum.Read ? "R" : c.Effect == EffectEnum.Write ? "W" : "D";
                    if (actual != row.Expected)
                        throw new InvalidOperationException("Expected " + row.Expected + " but classified " + actual + " (" + c.Reason + ") for " + display);
                    if (String.IsNullOrEmpty(c.Reason)) throw new InvalidOperationException("Classification has no reason for " + display);
                    if (!c.Rejected && !c.ApprovalSummary.EndsWith(" on api", StringComparison.Ordinal)) throw new InvalidOperationException("Summary missing target: " + c.ApprovalSummary);
                    return Task.CompletedTask;
                }));
            }
            cases.Add(new TestCaseDescriptor(suiteId, "table-size", "The table has at least 60 cases", ct =>
            {
                if (table.Count < 60) throw new InvalidOperationException("Only " + table.Count + " classifier cases.");
                return Task.CompletedTask;
            }));
            return new TestSuiteDescriptor(suiteId, "HTTP request classifier table", cases);
        }

        #endregion
    }
}
