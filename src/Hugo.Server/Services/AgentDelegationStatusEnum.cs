namespace Hugo.Server.Services
{
    /// <summary>
    /// Outcome of a task delegated to Hugo's own agent loop (ask_agent_smith, plan section 13.5).
    /// </summary>
    public enum AgentDelegationStatusEnum
    {
        /// <summary>
        /// The turn finished with a final answer.
        /// </summary>
        Completed,

        /// <summary>
        /// The turn waits for a human to decide an approval; continue the conversation once it is decided.
        /// </summary>
        ApprovalPending,

        /// <summary>
        /// The turn failed.
        /// </summary>
        Failed
    }
}
