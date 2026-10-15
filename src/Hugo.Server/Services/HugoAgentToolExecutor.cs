namespace Hugo.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Agent;
    using Hugo.Core.Enums;
    using Hugo.Core.Responses;
    using Hugo.Core.Security;

    /// <summary>
    /// The agent runtime's tool executor backed by <see cref="ToolInvocationService"/>: every call the model makes goes
    /// through the full pipeline as the turn's caller and profile. Chat and A2A wait for approvals
    /// (<see cref="ApprovalWaitModeEnum.Wait"/>); the OpenAI-compatible surface does not. Meta tools are handled by
    /// <see cref="MetaToolHandler"/> when set. Construct one per turn. Thread-safe.
    /// </summary>
    public class HugoAgentToolExecutor : IAgentToolExecutor
    {
        #region Public-Members

        /// <summary>
        /// Handles hugo_* meta tool calls; null makes them unknown tools.
        /// </summary>
        public Func<AgentTurnRequest, AgentToolCall, CancellationToken, Task<AgentToolExecution?>>? MetaToolHandler { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly ToolInvocationService _Invocations;
        private readonly RequestContext _Principal;
        private readonly InvocationSurfaceEnum _Surface;
        private readonly ApprovalWaitModeEnum _WaitMode;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="invocations">Invocation pipeline.</param>
        /// <param name="principal">The turn's caller.</param>
        /// <param name="surface">Surface of the turn.</param>
        /// <param name="waitMode">How the surface waits for approvals.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public HugoAgentToolExecutor(ToolInvocationService invocations, RequestContext principal, InvocationSurfaceEnum surface, ApprovalWaitModeEnum waitMode)
        {
            _Invocations = invocations ?? throw new ArgumentNullException(nameof(invocations));
            _Principal = principal ?? throw new ArgumentNullException(nameof(principal));
            _Surface = surface;
            _WaitMode = waitMode;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<AgentToolExecution> ExecuteAsync(AgentTurnRequest turn, AgentToolCall call, IAgentApprovalListener approvals, CancellationToken token)
        {
            if (turn == null) throw new ArgumentNullException(nameof(turn));
            if (call == null) throw new ArgumentNullException(nameof(call));
            if (call.Definition != null && call.Definition.IsMetaTool && MetaToolHandler != null)
            {
                AgentToolExecution? meta = await MetaToolHandler(turn, call, token).ConfigureAwait(false);
                if (meta != null) return meta;
            }
            InvocationRequest request = new InvocationRequest();
            request.Principal = _Principal;
            request.TenantId = turn.TenantId;
            request.ProfileId = turn.ProfileId;
            request.ResourceAlias = call.Resource;
            request.ToolName = call.ToolName;
            request.ArgumentsJson = call.ArgumentsJson;
            request.Surface = _Surface;
            request.WaitMode = _WaitMode;
            request.ApprovalListener = approvals;
            request.ConversationId = turn.ConversationId;
            request.CorrelationId = turn.TurnId;
            ToolInvocationOutcome outcome = await _Invocations.InvokeAsync(request, token).ConfigureAwait(false);
            AgentToolExecution execution = new AgentToolExecution();
            execution.Result = outcome.Result ?? ToolResult.Error(outcome.DecisionCode ?? "not_executed", outcome.Reason ?? "The call did not run.");
            execution.Effect = outcome.Effect ?? EffectEnum.Read;
            execution.Decision = Map(outcome.Decision);
            execution.DecisionCode = outcome.DecisionCode;
            execution.ApprovalId = outcome.ApprovalId;
            execution.ApprovalSummary = outcome.ApprovalId == null ? null : outcome.ApprovalSummary;
            execution.Resource = outcome.BindingAlias;
            execution.ConnectorType = outcome.ConnectorType;
            execution.InvocationId = outcome.InvocationId;
            execution.DurationMs = outcome.DurationMs;
            execution.ApprovalWaitMs = outcome.ApprovalWaitMs;
            return execution;
        }

        #endregion

        #region Private-Methods

        private static AgentToolDecisionEnum Map(InvocationDecisionEnum decision)
        {
            switch (decision)
            {
                case InvocationDecisionEnum.Allowed:
                    return AgentToolDecisionEnum.Allowed;
                case InvocationDecisionEnum.ApprovalRequired:
                    return AgentToolDecisionEnum.ApprovalRequired;
                case InvocationDecisionEnum.Approved:
                    return AgentToolDecisionEnum.Approved;
                case InvocationDecisionEnum.Rejected:
                case InvocationDecisionEnum.Cancelled:
                    return AgentToolDecisionEnum.Rejected;
                case InvocationDecisionEnum.Expired:
                    return AgentToolDecisionEnum.Expired;
                default:
                    return AgentToolDecisionEnum.Denied;
            }
        }

        #endregion
    }
}
