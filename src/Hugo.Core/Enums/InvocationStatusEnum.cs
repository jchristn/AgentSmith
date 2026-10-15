namespace Hugo.Core.Enums
{
    /// <summary>
    /// Execution status of a tool invocation.
    /// </summary>
    public enum InvocationStatusEnum
    {
        /// <summary>
        /// Waiting for an approval decision.
        /// </summary>
        Pending = 0,

        /// <summary>
        /// Executing now.
        /// </summary>
        Running = 1,

        /// <summary>
        /// The tool ran and returned a non-error result.
        /// </summary>
        Succeeded = 2,

        /// <summary>
        /// The tool ran and failed, or the call was invalid.
        /// </summary>
        Failed = 3,

        /// <summary>
        /// The tool exceeded its timeout.
        /// </summary>
        TimedOut = 4,

        /// <summary>
        /// The caller cancelled the call.
        /// </summary>
        Cancelled = 5,

        /// <summary>
        /// The call never ran (denied, rejected, expired, or cancelled approval).
        /// </summary>
        NotExecuted = 6
    }
}
