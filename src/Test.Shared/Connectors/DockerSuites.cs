namespace Test.Shared.Connectors
{
    using System.Collections.Generic;
    using Test.Shared.Connectors.Docker;
    using Touchstone.Core;

    /// <summary>
    /// Suites for the Docker connector: <c>connectors.docker</c> (offline) and <c>testbed.docker</c>
    /// (Docker-in-Docker, when <c>HUGO_TESTBED=1</c> and the testbed is up).
    /// </summary>
    public static class DockerSuites
    {
        /// <summary>
        /// Build the suites.
        /// </summary>
        /// <returns>Suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            return new List<TestSuiteDescriptor>
            {
                new TestSuiteDescriptor(DockerUnitTests.SuiteId, "Docker connector unit tests", DockerUnitTests.Build()),
                DockerTestbedTests.Build()
            };
        }
    }
}
