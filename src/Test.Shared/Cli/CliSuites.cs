namespace Test.Shared.Cli
{
    using System.Collections.Generic;
    using Touchstone.Core;

    /// <summary>
    /// Suites for the hugo command line (P7.20, P7.21). Every suite runs without external services: the relay and
    /// command suites use an in-process fake Hugo server on 127.0.0.1, and the process suite launches the built
    /// <c>hugo</c> tool and talks MCP to it over real stdio pipes.
    /// </summary>
    public static class CliSuites
    {
        /// <summary>
        /// Build the suites.
        /// </summary>
        /// <returns>Suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            return new List<TestSuiteDescriptor>
            {
                new TestSuiteDescriptor(CliArgumentCases.SuiteId, "hugo CLI: arguments, help, version, and helpers", CliArgumentCases.Build()),
                new TestSuiteDescriptor(CliCredentialCases.SuiteId, "hugo CLI: credential precedence and the credential file", CliCredentialCases.Build()),
                new TestSuiteDescriptor(CliRelayCases.SuiteId, "hugo CLI: stdio to Streamable HTTP MCP relay", CliRelayCases.Build()),
                new TestSuiteDescriptor(CliCommandCases.SuiteId, "hugo CLI: login, logout, whoami, profiles list", CliCommandCases.Build()),
                new TestSuiteDescriptor(CliProcessCases.SuiteId, "hugo CLI: MCP round trip with the tool process over stdio", CliProcessCases.Build())
            };
        }
    }
}
