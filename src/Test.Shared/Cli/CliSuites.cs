namespace Test.Shared.Cli
{
    using System.Collections.Generic;
    using Touchstone.Core;

    /// <summary>
    /// Suites for the hugo command line (P7.20, P7.21). The relay and command suites use an in-process fake
    /// Hugo server on 127.0.0.1. The process suite also uses Voltaic's MCP
    /// client to launch the built <c>hugo</c> tool over stdio against a real isolated Hugo server and connector.
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
