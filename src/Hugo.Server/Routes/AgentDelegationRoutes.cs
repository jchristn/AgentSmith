namespace Hugo.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using Hugo.Core.Agent;
    using Hugo.Core.Enums;
    using Hugo.Core.Requests;
    using Hugo.Server.Services;
    using WatsonWebserver.Core;

    /// <summary>
    /// REST twin of MCP <c>ask_agent_smith</c> (P6.23): <c>POST /v1.0/api/agent-profiles/{id}/ask</c>.
    /// </summary>
    public class AgentDelegationRoutes
    {
        #region Private-Members

        private readonly AgentDelegationService _Delegation;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="delegation">Delegation service.</param>
        /// <exception cref="ArgumentNullException">Thrown when delegation is null.</exception>
        public AgentDelegationRoutes(AgentDelegationService delegation)
        {
            _Delegation = delegation ?? throw new ArgumentNullException(nameof(delegation));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="routes">Registrar.</param>
        /// <exception cref="ArgumentNullException">Thrown when routes is null.</exception>
        public void Register(RouteRegistrar routes)
        {
            if (routes == null) throw new ArgumentNullException(nameof(routes));
            routes.Scoped(HttpMethod.POST, "/v1.0/api/agent-profiles/{id}/ask", ResourceTypeEnum.AgentProfile, OperationTypeEnum.Execute, AskAsync, "Delegate a task to the profile's own agent loop and wait for the answer (approvals wait up to Approvals.McpWaitSeconds, then the result reports ApprovalPending)", "Agent Profiles", typeof(ChatMessageRequest), typeof(AgentDelegationResult));
        }

        #endregion

        #region Private-Methods

        private async Task AskAsync(HttpContextBase http)
        {
            ChatMessageRequest request = RouteHelpers.Body<ChatMessageRequest>(http) ?? new ChatMessageRequest();
            try
            {
                AgentDelegationResult result = await _Delegation.AskAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), RouteHelpers.Param(http, "id")!, request.Message ?? String.Empty, request.ConversationId, InvocationSurfaceEnum.Rest, http.Token).ConfigureAwait(false);
                await RouteHelpers.JsonAsync(http, 200, result).ConfigureAwait(false);
            }
            catch (AgentTurnConflictException e)
            {
                await RouteHelpers.ErrorAsync(http, 409, "TurnInProgress", e.Message).ConfigureAwait(false);
            }
        }

        #endregion
    }
}
