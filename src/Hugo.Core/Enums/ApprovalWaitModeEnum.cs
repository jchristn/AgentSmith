namespace Hugo.Core.Enums
{
    /// <summary>
    /// How a surface waits when a tool call needs approval (plan section 11.4).
    /// </summary>
    public enum ApprovalWaitModeEnum
    {
        /// <summary>
        /// Block until the approval is decided or expires (dashboard chat, A2A).
        /// </summary>
        Wait = 0,

        /// <summary>
        /// Return at once with the pending approval (REST invoke, OpenAI-compatible).
        /// </summary>
        NoWait = 1,

        /// <summary>
        /// Block up to a timeout, then return the pending approval (MCP).
        /// </summary>
        WaitWithTimeout = 2
    }
}
