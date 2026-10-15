namespace Hugo.Core.Agent
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Executes one tool call through the tool invocation pipeline (plan 8.6): resolve, authorize, validate,
    /// classify, apply rules, approval, execute, scrub, record. Implemented by the server over
    /// <c>ToolInvocationService</c>. Must be thread-safe: the runtime may call it concurrently for independent
    /// Read calls.
    /// </summary>
    public interface IAgentToolExecutor
    {
        /// <summary>
        /// Execute a tool call. When the call needs approval and the surface waits (dashboard chat, A2A), the
        /// implementation calls <see cref="IAgentApprovalListener.ApprovalRequiredAsync"/>, blocks until the
        /// approval is decided or expires, calls <see cref="IAgentApprovalListener.ApprovalResolvedAsync"/>, and
        /// returns Approved (after executing), Rejected, or Expired. A surface that does not wait returns
        /// ApprovalRequired. Failures of the call itself are results, not exceptions.
        /// </summary>
        /// <param name="turn">The turn.</param>
        /// <param name="call">The call.</param>
        /// <param name="approvals">Listener for approval events of this call.</param>
        /// <param name="token">Cancellation token; cancels the approval wait and the in-flight call.</param>
        /// <returns>Execution outcome. Never null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="OperationCanceledException">Thrown when cancelled.</exception>
        Task<AgentToolExecution> ExecuteAsync(AgentTurnRequest turn, AgentToolCall call, IAgentApprovalListener approvals, CancellationToken token);
    }
}
