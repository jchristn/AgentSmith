namespace Hugo.Server.Services
{
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Security;

    /// <summary>
    /// Runs a whole task on Hugo's own agent loop as a caller's principal and profile (D-012, plan section 13.5). The MCP
    /// tool ask_agent_smith calls it through <see cref="ServerContext.AgentDelegation"/>, which the module that owns
    /// conversations sets. Implementations must be thread-safe.
    /// </summary>
    public interface IAgentDelegationService
    {
        /// <summary>
        /// Run one turn and return its final answer, or the approval it waits for.
        /// </summary>
        /// <param name="principal">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="profileId">Agent profile identifier.</param>
        /// <param name="message">The task, as a user message.</param>
        /// <param name="conversationId">Conversation to continue, or null to start one.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<AgentDelegationResult> AskAsync(RequestContext principal, string tenantId, string profileId, string message, string? conversationId, CancellationToken token);
    }
}
