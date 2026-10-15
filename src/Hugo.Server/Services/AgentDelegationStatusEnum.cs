namespace Hugo.Server.Services
{
    /// <summary>
    /// Outcome of a delegated ask (<see cref="IAgentDelegationService"/>).
    /// </summary>
    public enum AgentDelegationStatusEnum
    {
        /// <summary>
        /// The loop answered (including answers forced at the iteration or tool call limit).
        /// </summary>
        Completed = 0,

        /// <summary>
        /// A tool call waits for a human approval; <see cref="AgentDelegationResult.ApprovalId"/> names it. Ask again in
        /// the same conversation once it is decided (MCP callers use <c>hugo_await_approval</c> first).
        /// </summary>
        ApprovalPending = 1,

        /// <summary>
        /// The turn failed or was cancelled; see the error fields.
        /// </summary>
        Failed = 2
    }
}
