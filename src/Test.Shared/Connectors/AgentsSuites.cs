namespace Test.Shared.Connectors
{
    using System.Collections.Generic;
    using Test.Shared.Connectors.Agents;
    using Touchstone.Core;

    /// <summary>
    /// Suites for the agent connectors (<c>mcp</c> and <c>a2a-agent</c>). They run against in-process Voltaic servers on
    /// 127.0.0.1, so they always run.
    /// </summary>
    public static class AgentsSuites
    {
        #region Public-Methods

        /// <summary>
        /// Build the suites.
        /// </summary>
        /// <returns>Suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            const string mcpId = "connectors.agents.mcp";
            List<TestCaseDescriptor> mcp = McpUnitCases.Build(mcpId);
            mcp.AddRange(McpServerCases.Build(mcpId));
            const string a2aId = "connectors.agents.a2a";
            return new List<TestSuiteDescriptor>
            {
                new TestSuiteDescriptor(mcpId, "External MCP connector: discovery, filters, annotation effects, calls, auth, fencing, resources, Stdio gates against TestMcpServer", mcp),
                new TestSuiteDescriptor(a2aId, "Remote A2A agent connector: card, delegation, input-required, artifacts, streaming, tasks, auth against TestA2AAgent", A2AAgentCases.Build(a2aId))
            };
        }

        #endregion
    }
}
