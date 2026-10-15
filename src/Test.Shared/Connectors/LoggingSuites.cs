namespace Test.Shared.Connectors
{
    using System.Collections.Generic;
    using Touchstone.Core;

    /// <summary>
    /// Suites for the Logging connector family. Unit suites always run; testbed suites run when HUGO_TESTBED=1.
    /// </summary>
    public static class LoggingSuites
    {
        /// <summary>
        /// Build the suites.
        /// </summary>
        /// <returns>Suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            return new List<TestSuiteDescriptor>();
        }
    }
}
