namespace Hugo.Server.Services
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Agent;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Security;
    using Hugo.Core.Services;

    /// <summary>
    /// <see cref="IAgentDelegationService"/> over <see cref="AgentTurnHost"/>. Authorizes Execute on the profile, honors
    /// credentials locked to a profile, continues or starts a conversation owned by the caller, and runs the turn with
    /// approvals waiting up to <c>Approvals.McpWaitSeconds</c>. Thread-safe.
    /// </summary>
    public class AgentDelegationService : IAgentDelegationService
    {
        #region Private-Members

        private readonly ServerContext _Context;
        private readonly AgentTurnHost _Host;
        private readonly ProfileResolver _Resolver;
        private readonly ConversationService _Conversations;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Server context.</param>
        /// <param name="host">Agent turn host.</param>
        /// <param name="resolver">Profile resolver.</param>
        /// <param name="conversations">Conversation service.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public AgentDelegationService(ServerContext context, AgentTurnHost host, ProfileResolver resolver, ConversationService conversations)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            _Host = host ?? throw new ArgumentNullException(nameof(host));
            _Resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _Conversations = conversations ?? throw new ArgumentNullException(nameof(conversations));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<AgentDelegationResult> AskAsync(RequestContext principal, string tenantId, string profileId, string message, string? conversationId, CancellationToken token)
        {
            return await AskAsync(principal, tenantId, profileId, message, conversationId, InvocationSurfaceEnum.Mcp, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Ask a profile to handle a message, labeling the turn with a surface (Mcp for <c>ask_agent_smith</c>, Rest for
        /// the REST ask route).
        /// </summary>
        /// <param name="principal">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="profileId">Agent profile identifier or name.</param>
        /// <param name="message">The task or question.</param>
        /// <param name="conversationId">Conversation to continue, or null to start one.</param>
        /// <param name="surface">Surface label.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when principal or tenantId is null.</exception>
        /// <exception cref="ValidationException">Thrown when the message is empty or the profile has no inference endpoint.</exception>
        /// <exception cref="NotFoundException">Thrown when the profile or conversation is not visible.</exception>
        /// <exception cref="ForbiddenException">Thrown when the caller may not execute the profile.</exception>
        /// <exception cref="AgentTurnConflictException">Thrown when another turn is running in the conversation.</exception>
        public async Task<AgentDelegationResult> AskAsync(RequestContext principal, string tenantId, string profileId, string message, string? conversationId, InvocationSurfaceEnum surface, CancellationToken token)
        {
            if (principal == null) throw new ArgumentNullException(nameof(principal));
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(profileId)) throw new NotFoundException("Agent profile not found.");

            AgentProfile profile = (await _Resolver.ResolveAsync(principal, tenantId, profileId, token).ConfigureAwait(false)).Profile;
            AuthorizationDecision decision = await _Context.Authorization.AuthorizeAsync(principal, tenantId, ResourceTypeEnum.AgentProfile, OperationTypeEnum.Execute, profile.Id, token).ConfigureAwait(false);
            if (decision.Result == AuthorizationResultEnum.NotFound) throw new NotFoundException("Agent profile not found.");
            if (!decision.Permitted) throw new ForbiddenException("AuthorizationDenied", "The caller may not use agent profile " + profile.Name + " (Execute on AgentProfile is required).");

            Conversation conversation;
            if (!String.IsNullOrWhiteSpace(conversationId))
            {
                conversation = await _Conversations.ReadAsync(principal, tenantId, conversationId.Trim(), token).ConfigureAwait(false);
                if (!String.Equals(conversation.AgentProfileId, profile.Id, StringComparison.Ordinal)) throw new ValidationException("Conversation " + conversation.Id + " belongs to another agent profile.") { Code = "ConversationProfileMismatch" };
            }
            else
            {
                if (String.IsNullOrWhiteSpace(message)) throw new ValidationException("A message is required.") { Code = "MessageRequired" };
                conversation = await _Conversations.CreateAsync(principal, tenantId, profile, surface == InvocationSurfaceEnum.Rest ? ConversationSourceEnum.Rest : ConversationSourceEnum.Mcp, null, token).ConfigureAwait(false);
            }

            AgentTurnOptions options = new AgentTurnOptions();
            options.Principal = principal;
            options.TenantId = tenantId;
            options.Profile = profile;
            options.Conversation = conversation;
            options.UserMessage = message ?? String.Empty;
            options.Surface = surface;
            options.WaitMode = ApprovalWaitModeEnum.WaitWithTimeout;
            AgentTurnResult result = await _Host.RunAsync(options, token).ConfigureAwait(false);
            return ToResult(result, profile.Id);
        }

        /// <summary>
        /// Convert a turn result to a delegation result.
        /// </summary>
        /// <param name="result">Turn result.</param>
        /// <param name="profileId">Agent profile identifier.</param>
        /// <returns>Delegation result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when result is null.</exception>
        public static AgentDelegationResult ToResult(AgentTurnResult result, string profileId)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            AgentDelegationResult delegation = new AgentDelegationResult();
            delegation.ConversationId = result.ConversationId;
            delegation.TurnId = result.TurnId;
            delegation.ProfileId = profileId ?? String.Empty;
            delegation.FinalText = result.FinalText;
            delegation.FinishReason = AgentTurnHost.ReasonLabel(result.FinishReason);
            delegation.InputTokens = result.TokensIn;
            delegation.OutputTokens = result.TokensOut;
            delegation.DurationMs = result.DurationMs;
            delegation.ApprovalId = result.PendingApprovalId;
            switch (result.FinishReason)
            {
                case AgentFinishReasonEnum.ApprovalPending:
                    delegation.Status = AgentDelegationStatusEnum.ApprovalPending;
                    break;
                case AgentFinishReasonEnum.Error:
                    delegation.Status = AgentDelegationStatusEnum.Failed;
                    delegation.ErrorCode = result.ErrorCode ?? "error";
                    delegation.ErrorMessage = result.ErrorMessage;
                    break;
                case AgentFinishReasonEnum.Cancelled:
                    delegation.Status = AgentDelegationStatusEnum.Failed;
                    delegation.ErrorCode = "cancelled";
                    delegation.ErrorMessage = "The turn was cancelled.";
                    break;
                default:
                    delegation.Status = AgentDelegationStatusEnum.Completed;
                    break;
            }
            delegation.ToolTrace = result.ToolCalls.Select(c => new AgentDelegationToolStep
            {
                ToolName = c.ToolName,
                Resource = c.Resource,
                Effect = c.Effect.ToString(),
                Decision = c.Decision.ToString(),
                IsError = c.IsError,
                ErrorCode = c.ErrorCode,
                ApprovalId = c.ApprovalId,
                DurationMs = c.DurationMs
            }).ToList();
            return delegation;
        }

        #endregion
    }
}
