namespace Hugo.Server.Services
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Connectors;
    using Hugo.Core.Models;
    using Hugo.Core.Security;

    /// <summary>
    /// Supplies Hugo meta tools (hugo_whoami, hugo_list_resources, ask_agent_smith, and so on) for a caller of a
    /// profile. Registered with <see cref="ToolCatalogService.AddMetaToolProvider"/> by the protocol surfaces; the tool
    /// catalog lists them with <see cref="CallerTool.IsMetaTool"/> set. Implementations must be thread-safe.
    /// </summary>
    public interface IMetaToolProvider
    {
        /// <summary>
        /// Meta tools for a caller of a profile.
        /// </summary>
        /// <param name="principal">Caller.</param>
        /// <param name="profile">Profile.</param>
        /// <param name="surface">Surface label (chat, mcp, a2a, openai, rest).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tools. Never null.</returns>
        Task<List<CallerTool>> GetToolsAsync(RequestContext principal, AgentProfile profile, string surface, CancellationToken token);
    }
}
