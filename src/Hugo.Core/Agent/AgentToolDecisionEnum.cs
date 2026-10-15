namespace Hugo.Core.Agent
{
    /// <summary>
    /// Final decision for one tool call within a turn, as reported by <see cref="IAgentToolExecutor"/>.
    /// </summary>
    public enum AgentToolDecisionEnum
    {
        /// <summary>
        /// Allowed without approval and executed.
        /// </summary>
        Allowed = 0,

        /// <summary>
        /// Denied by policy; not executed. The result explains why.
        /// </summary>
        Denied = 1,

        /// <summary>
        /// Approval is required and still pending; not executed. Returned by surfaces that do not wait (the
        /// OpenAI-compatible surface ends the turn and resumes on a later message).
        /// </summary>
        ApprovalRequired = 2,

        /// <summary>
        /// Approved by a human and executed.
        /// </summary>
        Approved = 3,

        /// <summary>
        /// Rejected by a human; not executed.
        /// </summary>
        Rejected = 4,

        /// <summary>
        /// The approval expired before a decision; not executed.
        /// </summary>
        Expired = 5
    }
}
