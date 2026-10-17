namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Agent;

    /// <summary>
    /// The tool set Hugo's own loop offers its model: the profile's connector tools without the <c>hugo_*</c> meta tools
    /// and <c>ask_agent_smith</c>. Current-profile instruction tools remain available. Other meta tools serve an MCP caller's model; inside the loop they would recurse
    /// (<c>ask_agent_smith</c>) or duplicate what the loop already knows. Thread-safe when the inner catalog is.
    /// </summary>
    public class LoopToolCatalog : IAgentToolCatalog
    {
        #region Private-Members

        private readonly IAgentToolCatalog _Inner;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="inner">Catalog to filter.</param>
        /// <exception cref="ArgumentNullException">Thrown when inner is null.</exception>
        public LoopToolCatalog(IAgentToolCatalog inner)
        {
            _Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<List<AgentToolDefinition>> GetToolsAsync(AgentTurnRequest turn, CancellationToken token)
        {
            if (turn == null) throw new ArgumentNullException(nameof(turn));
            List<AgentToolDefinition> tools = await _Inner.GetToolsAsync(turn, token).ConfigureAwait(false) ?? new List<AgentToolDefinition>();
            return tools.Where(t => (!t.IsMetaTool || AgentInstructionTools.IsInstructionTool(t.Name)) && !String.Equals(t.Name, "ask_agent_smith", StringComparison.Ordinal)).ToList();
        }

        #endregion
    }
}
