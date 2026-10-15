namespace Hugo.Core.Enums
{
    /// <summary>
    /// A2A 1.0 task state as persisted in <c>a2a_tasks.state</c> (plan 14.4). Names follow the A2A specification; the
    /// server maps them onto the Voltaic wire enum.
    /// </summary>
    public enum A2ATaskStateEnum
    {
        /// <summary>
        /// Accepted, not yet running.
        /// </summary>
        Submitted = 0,

        /// <summary>
        /// The agent loop is running.
        /// </summary>
        Working = 1,

        /// <summary>
        /// Waiting for a human to decide an approval; the task resumes automatically once it is decided.
        /// </summary>
        InputRequired = 2,

        /// <summary>
        /// Finished with an answer.
        /// </summary>
        Completed = 3,

        /// <summary>
        /// Finished with an error.
        /// </summary>
        Failed = 4,

        /// <summary>
        /// Cancelled by the caller.
        /// </summary>
        Canceled = 5,

        /// <summary>
        /// Refused before it ran.
        /// </summary>
        Rejected = 6
    }
}
