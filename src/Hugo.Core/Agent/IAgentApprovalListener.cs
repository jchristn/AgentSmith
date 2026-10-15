namespace Hugo.Core.Agent
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Receives approval transitions for one tool call. The runtime supplies an implementation per call that
    /// emits <c>approval.required</c> and <c>approval.resolved</c> and times the approval wait. Implementations never
    /// throw except for cancellation.
    /// </summary>
    public interface IAgentApprovalListener
    {
        /// <summary>
        /// An approval was created and the call is waiting.
        /// </summary>
        /// <param name="approval">Approval details.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task ApprovalRequiredAsync(AgentApprovalRequest approval, CancellationToken token);

        /// <summary>
        /// The approval was decided or expired.
        /// </summary>
        /// <param name="resolution">Resolution.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task ApprovalResolvedAsync(AgentApprovalResolution resolution, CancellationToken token);
    }
}
