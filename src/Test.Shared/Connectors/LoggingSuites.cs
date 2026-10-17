namespace Test.Shared.Connectors
{
    using System.Collections.Generic;
    using Test.Shared.Connectors.Logging;
    using Touchstone.Core;

    /// <summary>
    /// Suites for the Logging connector family (SolarWinds Observability). They run against in-process
    /// HTTP stubs, so they always run and need no cloud account.
    /// </summary>
    public static class LoggingSuites
    {
        /// <summary>
        /// Build the suites.
        /// </summary>
        /// <returns>Suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            return new List<TestSuiteDescriptor>
            {
                new TestSuiteDescriptor("connectors.logging.common", "Logging family: contract, schemas, time ranges, cursors", LoggingCommonCases.Build()),
                new TestSuiteDescriptor("connectors.logging.swo", "SolarWinds Observability connector against SwoStub", SwoCases.Build())
            };
        }
    }
}
