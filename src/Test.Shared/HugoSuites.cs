namespace Test.Shared
{
    using System.Collections.Generic;
    using Test.Shared.Connectors;
    using Touchstone.Core;

    /// <summary>
    /// Registry of every Hugo test suite consumed by the console, xUnit, and NUnit runners.
    /// </summary>
    public static class HugoSuites
    {
        /// <summary>
        /// All suites.
        /// </summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get
            {
                List<TestSuiteDescriptor> suites = new List<TestSuiteDescriptor>();
                suites.AddRange(Test.Shared.Agent.AgentSuites.Build());
                suites.AddRange(Test.Shared.Suites.ConnectorFrameworkSuites.Build());
                suites.AddRange(AbstractionsSuites.Build());
                suites.AddRange(SshSuites.Build());
                suites.AddRange(StorageSuites.Build());
                suites.AddRange(SqlSuites.Build());
                suites.AddRange(LoggingSuites.Build());
                return suites;
            }
        }
    }
}
