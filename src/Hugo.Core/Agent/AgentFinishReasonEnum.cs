namespace Hugo.Core.Agent
{
    /// <summary>
    /// Why a turn ended. Persisted as the turn's <c>finish_reason</c>.
    /// </summary>
    public enum AgentFinishReasonEnum
    {
        /// <summary>
        /// The model answered without requesting more tools.
        /// </summary>
        Completed = 0,

        /// <summary>
        /// The iteration limit was reached and the final answer was forced (tool choice none).
        /// </summary>
        MaxIterations = 1,

        /// <summary>
        /// The per-turn tool call limit was reached and the final answer was forced.
        /// </summary>
        ToolCallLimit = 2,

        /// <summary>
        /// The turn ended waiting for an approval (surfaces that do not wait).
        /// </summary>
        ApprovalPending = 3,

        /// <summary>
        /// The client cancelled the turn.
        /// </summary>
        Cancelled = 4,

        /// <summary>
        /// The turn failed (for example every model endpoint was unavailable).
        /// </summary>
        Error = 5
    }
}
