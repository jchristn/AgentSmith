namespace Test.Shared.Agent
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Agent;

    /// <summary>
    /// Test double for the tool invocation pipeline: behavior per tool name, records every call, tracks peak
    /// concurrency, and counts calls cancelled in flight. Thread-safe.
    /// </summary>
    public class ScriptedToolExecutor : IAgentToolExecutor
    {
        #region Public-Members

        /// <summary>
        /// Behaviors by tool name. Unlisted tools are allowed and return <c>ok</c>.
        /// </summary>
        public Dictionary<string, ScriptedToolBehavior> Behaviors { get; } = new Dictionary<string, ScriptedToolBehavior>(StringComparer.Ordinal);

        /// <summary>
        /// Calls received, in arrival order.
        /// </summary>
        public List<AgentToolCall> Calls
        {
            get
            {
                lock (_Lock) return _Calls.ToList();
            }
        }

        /// <summary>
        /// Highest number of calls in flight at once.
        /// </summary>
        public int PeakConcurrency
        {
            get
            {
                lock (_Lock) return _Peak;
            }
        }

        /// <summary>
        /// Calls that observed cancellation while in flight.
        /// </summary>
        public int CancelledInFlight
        {
            get
            {
                lock (_Lock) return _Cancelled;
            }
        }

        /// <summary>
        /// Completed when any call starts executing (for cancellation tests).
        /// </summary>
        public TaskCompletionSource<bool> Started { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        #endregion

        #region Private-Members

        private readonly object _Lock = new object();
        private readonly List<AgentToolCall> _Calls = new List<AgentToolCall>();
        private int _InFlight = 0;
        private int _Peak = 0;
        private int _Cancelled = 0;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Set the behavior of a tool.
        /// </summary>
        /// <param name="tool">Tool name.</param>
        /// <param name="behavior">Behavior.</param>
        /// <returns>This executor.</returns>
        public ScriptedToolExecutor On(string tool, ScriptedToolBehavior behavior)
        {
            Behaviors[tool] = behavior;
            return this;
        }

        /// <inheritdoc />
        public async Task<AgentToolExecution> ExecuteAsync(AgentTurnRequest turn, AgentToolCall call, IAgentApprovalListener approvals, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(turn);
            ArgumentNullException.ThrowIfNull(call);
            ArgumentNullException.ThrowIfNull(approvals);
            ScriptedToolBehavior behavior;
            if (!Behaviors.TryGetValue(call.ToolName, out ScriptedToolBehavior? found)) behavior = new ScriptedToolBehavior();
            else behavior = found;
            lock (_Lock) _Calls.Add(call);

            AgentToolExecution execution = new AgentToolExecution { Effect = behavior.Effect, Resource = call.Resource, ConnectorType = "test" };
            if (behavior.Decision == AgentToolDecisionEnum.Denied)
            {
                execution.Decision = AgentToolDecisionEnum.Denied;
                execution.DecisionCode = "readonly_binding";
                execution.Result = ToolResult.Error("denied", "Denied: the resource is ReadOnly for this profile; " + behavior.Effect + " calls are not allowed.");
                return execution;
            }

            if (behavior.Decision == AgentToolDecisionEnum.ApprovalRequired)
            {
                AgentApprovalRequest approval = new AgentApprovalRequest
                {
                    ApprovalId = behavior.ApprovalId,
                    Summary = "Run " + call.ToolName + " on " + (call.Resource ?? "resource"),
                    Effect = behavior.Effect,
                    Reason = "approval_required",
                    Resource = call.Resource,
                    ToolName = call.ToolName,
                    RedactedArgumentsJson = call.ArgumentsJson,
                    ExpiresUtc = DateTime.UtcNow.AddMinutes(15)
                };
                execution.ApprovalId = approval.ApprovalId;
                execution.ApprovalSummary = approval.Summary;
                if (!behavior.WaitForApproval)
                {
                    execution.Decision = AgentToolDecisionEnum.ApprovalRequired;
                    execution.Result = ToolResult.FromText("{\"status\":\"approval_pending\",\"approvalId\":\"" + approval.ApprovalId + "\"}");
                    return execution;
                }

                await approvals.ApprovalRequiredAsync(approval, token).ConfigureAwait(false);
                AgentToolDecisionEnum outcome = await behavior.ApprovalSignal.Task.WaitAsync(token).ConfigureAwait(false);
                await approvals.ApprovalResolvedAsync(new AgentApprovalResolution { ApprovalId = approval.ApprovalId, Decision = outcome, DecidedBy = "mr.anderson" }, token).ConfigureAwait(false);
                if (outcome != AgentToolDecisionEnum.Approved)
                {
                    execution.Decision = outcome;
                    execution.Result = ToolResult.Error(outcome == AgentToolDecisionEnum.Expired ? "approval_expired" : "approval_rejected", "Not executed: the approval was " + outcome.ToString().ToLowerInvariant() + ".");
                    return execution;
                }
                execution.Decision = AgentToolDecisionEnum.Approved;
            }
            else
            {
                execution.Decision = AgentToolDecisionEnum.Allowed;
            }

            lock (_Lock)
            {
                _InFlight++;
                if (_InFlight > _Peak) _Peak = _InFlight;
            }
            Started.TrySetResult(true);
            try
            {
                if (behavior.DelayMs > 0) await Task.Delay(behavior.DelayMs, token).ConfigureAwait(false);
                execution.Result = ToolResult.FromText(behavior.Output);
                execution.DurationMs = Math.Max(1, behavior.DelayMs);
                return execution;
            }
            catch (OperationCanceledException)
            {
                lock (_Lock) _Cancelled++;
                throw;
            }
            finally
            {
                lock (_Lock) _InFlight--;
            }
        }

        #endregion
    }
}
