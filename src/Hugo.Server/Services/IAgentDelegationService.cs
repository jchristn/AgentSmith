namespace Hugo.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Agent;
    using Hugo.Core.Security;
    using Hugo.Core.Services;

    /// <summary>
    /// Runs one turn of Hugo's own loop on behalf of a caller (P6.23): the entry point shared by MCP
    /// <c>ask_agent_smith</c> and <c>POST /v1.0/api/agent-profiles/{id}/ask</c>. Approvals behave as plan 11.4's MCP row:
    /// the turn waits up to <c>Approvals.McpWaitSeconds</c>, then ends with status ApprovalPending. Registered on the
    /// server context as <see cref="Hugo.Server.ServerContext.AgentDelegation"/>. Thread-safe.
    /// </summary>
    public interface IAgentDelegationService
    {
        /// <summary>
        /// Ask a profile to handle a message, as the caller's principal.
        /// </summary>
        /// <param name="principal">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="profileId">Agent profile identifier or name.</param>
        /// <param name="message">The task or question.</param>
        /// <param name="conversationId">Conversation to continue, or null to start one.</param>
        /// <param name="token">Cancellation token; cancels the turn.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when principal or tenantId is null.</exception>
        /// <exception cref="ValidationException">Thrown when the message is empty or the profile has no inference endpoint (code ModelEndpointRequired).</exception>
        /// <exception cref="NotFoundException">Thrown when the profile or conversation is not visible to the caller.</exception>
        /// <exception cref="ForbiddenException">Thrown when the caller may not execute the profile (code AuthorizationDenied or ProfileLocked).</exception>
        /// <exception cref="AgentTurnConflictException">Thrown when another turn is running in the conversation.</exception>
        Task<AgentDelegationResult> AskAsync(RequestContext principal, string tenantId, string profileId, string message, string? conversationId, CancellationToken token);
    }
}
