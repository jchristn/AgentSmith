namespace Test.Shared.Connectors
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading.Tasks;
    using Hugo.Connectors.Observability;
    using Test.Shared.Connectors.Observability;
    using Touchstone.Core;

    /// <summary>
    /// Suites for the observability connectors (Prometheus, Loki, Elasticsearch and OpenSearch). Unit suites run against
    /// in-process stubs and always run; the testbed suite runs when HUGO_TESTBED=1.
    /// </summary>
    public static class ObservabilitySuites
    {
        /// <summary>
        /// Build the suites.
        /// </summary>
        /// <returns>Suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            return new List<TestSuiteDescriptor>
            {
                new TestSuiteDescriptor(ObservabilityCommonCases.SuiteId, "Observability connectors: contract, auth, transport, time, limits", ObservabilityCommonCases.Build()),
                new TestSuiteDescriptor(PrometheusCases.SuiteId, "Prometheus connector against a stub", PrometheusCases.Build()),
                new TestSuiteDescriptor(LokiCases.SuiteId, "Loki connector against a stub", LokiCases.Build()),
                new TestSuiteDescriptor(ElasticsearchCases.SuiteId, "Elasticsearch and OpenSearch connector against a stub", ElasticsearchCases.Build()),
                IndexGuardSuite(),
                ObservabilityTestbedCases.Build()
            };
        }

        private static TestSuiteDescriptor IndexGuardSuite()
        {
            const string suiteId = "connectors.observability.index-guard";
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            List<EsIndexGuardCase> table = EsIndexGuardTable.Cases();
            for (int i = 0; i < table.Count; i++)
            {
                EsIndexGuardCase row = table[i];
                string id = "case-" + (i + 1).ToString("D3", CultureInfo.InvariantCulture);
                string display = row.Expected + ": '" + row.Expression + "' with [" + String.Join(", ", row.Allowed) + "]";
                cases.Add(new TestCaseDescriptor(suiteId, id, display, ct =>
                {
                    EsIndexCheck check = EsIndexGuard.Check(row.Expression, row.Allowed);
                    string actual = check.Allowed ? "A" : check.Code == "permission_denied" ? "D" : check.Code == "invalid_argument" ? "I" : "?";
                    if (actual != row.Expected)
                        throw new InvalidOperationException("Expected " + row.Expected + " but got " + actual + " (" + check.Reason + ") for '" + row.Expression + "'.");
                    if (!check.Allowed && String.IsNullOrEmpty(check.Reason)) throw new InvalidOperationException("Refusal without a reason for '" + row.Expression + "'.");
                    return Task.CompletedTask;
                }));
            }
            cases.Add(new TestCaseDescriptor(suiteId, "table-size", "The table has at least 40 cases", ct =>
            {
                if (table.Count < 40) throw new InvalidOperationException("Only " + table.Count + " index guard cases.");
                return Task.CompletedTask;
            }));
            cases.Add(new TestCaseDescriptor(suiteId, "hit-filter", "Concrete index names and data stream backing indexes are checked for hits", ct =>
            {
                List<string> allowed = new List<string> { "logs-*" };
                if (!EsIndexGuard.IsIndexAllowed("logs-1", allowed)) throw new InvalidOperationException("logs-1");
                if (!EsIndexGuard.IsIndexAllowed(".ds-logs-app-2026.10.14-000001", allowed)) throw new InvalidOperationException("data stream backing index");
                if (EsIndexGuard.IsIndexAllowed(".ds-secret-1", allowed)) throw new InvalidOperationException("other data stream");
                if (EsIndexGuard.IsIndexAllowed("secret", allowed) || EsIndexGuard.IsIndexAllowed(null, allowed)) throw new InvalidOperationException("secret");
                if (!EsIndexGuard.GlobMatch("abc", "a*c") || EsIndexGuard.GlobMatch("abd", "a*c") || !EsIndexGuard.GlobMatch("a", "**a*")) throw new InvalidOperationException("glob");
                return Task.CompletedTask;
            }));
            return new TestSuiteDescriptor(suiteId, "Elasticsearch index allow list table", cases);
        }
    }
}
