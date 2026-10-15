namespace Hugo.Core.Agent
{
    using System.Threading;

    /// <summary>
    /// One keyed lock in a <see cref="ConversationLockManager"/>. Guarded by the manager.
    /// </summary>
    public class ConversationLockEntry
    {
        /// <summary>
        /// The lock.
        /// </summary>
        public SemaphoreSlim Semaphore { get; } = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Holders plus waiters; the entry is removed when it reaches zero.
        /// </summary>
        public int References { get; set; } = 0;
    }
}
