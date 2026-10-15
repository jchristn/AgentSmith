namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Agent;
    using Hugo.Core.Connectors;
    using Hugo.Core.Security;

    /// <summary>
    /// The agent runtime's tool catalog backed by <see cref="ToolCatalogService"/>: the same per-caller tools MCP lists,
    /// as agent tool definitions. Construct one per turn with the turn's authenticated caller. Thread-safe.
    /// </summary>
    public class HugoAgentToolCatalog : IAgentToolCatalog
    {
        #region Private-Members

        private readonly ToolCatalogService _Catalog;
        private readonly RequestContext _Principal;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="catalog">Tool catalog service.</param>
        /// <param name="principal">The turn's caller.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public HugoAgentToolCatalog(ToolCatalogService catalog, RequestContext principal)
        {
            _Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _Principal = principal ?? throw new ArgumentNullException(nameof(principal));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<List<AgentToolDefinition>> GetToolsAsync(AgentTurnRequest turn, CancellationToken token)
        {
            if (turn == null) throw new ArgumentNullException(nameof(turn));
            List<AgentToolDefinition> definitions = new List<AgentToolDefinition>();
            if (String.IsNullOrEmpty(turn.ProfileId)) return definitions;
            CallerToolCatalog catalog = await _Catalog.ListAsync(_Principal, turn.TenantId, turn.ProfileId, turn.Surface, token).ConfigureAwait(false);
            foreach (CallerTool tool in catalog.MetaTools) definitions.Add(Definition(tool));
            foreach (CallerTool tool in catalog.Tools) definitions.Add(Definition(tool));
            return definitions;
        }

        #endregion

        #region Private-Methods

        private static AgentToolDefinition Definition(CallerTool tool)
        {
            AgentToolDefinition definition = new AgentToolDefinition();
            definition.Name = tool.Name;
            definition.Description = tool.Description;
            definition.InputSchemaJson = tool.InputSchemaJson;
            definition.IsMetaTool = tool.IsMetaTool;
            definition.KnownEffect = tool.KnownEffect;
            definition.Family = tool.IsMetaTool ? null : tool.Family;
            return definition;
        }

        #endregion
    }
}
