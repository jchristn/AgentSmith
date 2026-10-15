namespace Hugo.Core.Agent
{
    /// <summary>
    /// Role of a persisted conversation message.
    /// </summary>
    public enum AgentMessageRoleEnum
    {
        /// <summary>
        /// The human (or calling agent).
        /// </summary>
        User = 0,

        /// <summary>
        /// Hugo's answer, or Hugo's tool-call request.
        /// </summary>
        Assistant = 1,

        /// <summary>
        /// A tool result (wrapped as data).
        /// </summary>
        Tool = 2,

        /// <summary>
        /// A compaction summary replacing earlier messages. Rendered into the system prompt.
        /// </summary>
        Summary = 3,

        /// <summary>
        /// A system note. Rendered into the system prompt.
        /// </summary>
        System = 4
    }
}
