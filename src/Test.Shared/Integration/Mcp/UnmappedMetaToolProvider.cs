namespace Test.Shared.Integration.Mcp
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Connectors;
    using Hugo.Core.Models;
    using Hugo.Core.Security;
    using Hugo.Server.Services;

    /// <summary>
    /// Offers a meta tool that no MCP authorization mapping covers, to prove the MCP surface fails closed.
    /// </summary>
    public class UnmappedMetaToolProvider : IMetaToolProvider
    {
        /// <summary>
        /// The unmapped tool's name.
        /// </summary>
        public static readonly string ToolName = "test_unmapped_tool";

        /// <inheritdoc />
        public Task<List<CallerTool>> GetToolsAsync(RequestContext principal, AgentProfile profile, string surface, CancellationToken token)
        {
            CallerTool tool = new CallerTool();
            tool.Name = ToolName;
            tool.Title = "Unmapped";
            tool.Description = "A tool without an authorization mapping.";
            tool.InputSchemaJson = "{\"type\":\"object\"}";
            return Task.FromResult(new List<CallerTool> { tool });
        }
    }
}
