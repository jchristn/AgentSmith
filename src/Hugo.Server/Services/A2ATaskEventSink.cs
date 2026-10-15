namespace Hugo.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Agent;
    using Hugo.Core.Agent.Events;
    using Hugo.Core.Enums;

    /// <summary>
    /// Maps a turn's approval events onto its A2A task (P7.15): <c>approval.required</c> moves the task to input-required
    /// with a message naming the approval, and <c>approval.resolved</c> moves it back to working. Thread-safe.
    /// </summary>
    public class A2ATaskEventSink : IAgentEventSink
    {
        #region Private-Members

        private readonly A2ATaskService _Tasks;
        private readonly string _TenantId;
        private readonly string _TaskId;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="tasks">Task service.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="taskId">Task identifier.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public A2ATaskEventSink(A2ATaskService tasks, string tenantId, string taskId)
        {
            _Tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
            _TenantId = tenantId ?? throw new ArgumentNullException(nameof(tenantId));
            _TaskId = taskId ?? throw new ArgumentNullException(nameof(taskId));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task PublishAsync(AgentEvent agentEvent, CancellationToken token)
        {
            if (agentEvent is ApprovalRequiredEvent required)
            {
                string message = "Waiting for approval " + required.ApprovalId + ": " + required.Summary + ". A person with approval rights must approve or reject it in the Hugo dashboard or over REST (POST /v1.0/api/approvals/" + required.ApprovalId + "/approve); the task resumes automatically. An A2A caller cannot approve its own request.";
                await _Tasks.TransitionAsync(_TenantId, _TaskId, A2ATaskStateEnum.InputRequired, message, required.ApprovalId, CancellationToken.None).ConfigureAwait(false);
            }
            else if (agentEvent is ApprovalResolvedEvent resolved)
            {
                string message = "Approval " + resolved.ApprovalId + " was " + resolved.Decision.ToString().ToLowerInvariant() + (String.IsNullOrEmpty(resolved.DecidedBy) ? String.Empty : " by " + resolved.DecidedBy) + "; continuing.";
                await _Tasks.TransitionAsync(_TenantId, _TaskId, A2ATaskStateEnum.Working, message, null, CancellationToken.None).ConfigureAwait(false);
            }
        }

        #endregion
    }
}
