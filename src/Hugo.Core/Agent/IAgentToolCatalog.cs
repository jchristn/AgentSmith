namespace Hugo.Core.Agent
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Supplies the tools visible to a turn: the profile's per-caller tool set (D-017) plus Hugo meta tools.
    /// Implemented by the server over the connector registry and the profile's bindings. Must be thread-safe if
    /// shared across turns.
    /// </summary>
    public interface IAgentToolCatalog
    {
        /// <summary>
        /// List the tools visible to this turn.
        /// </summary>
        /// <param name="turn">The turn request (tenant, principal, profile, conversation).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tool definitions. Never null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when turn is null.</exception>
        Task<List<AgentToolDefinition>> GetToolsAsync(AgentTurnRequest turn, CancellationToken token);
    }
}
