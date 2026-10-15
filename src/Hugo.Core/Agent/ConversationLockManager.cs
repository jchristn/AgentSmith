namespace Hugo.Core.Agent
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// One turn at a time per conversation (plan 13.3 step 1, P6.20). A keyed <see cref="SemaphoreSlim"/> map whose
    /// entries are removed when unused, so memory stays bounded by active conversations. This stands in for the
    /// Padlock package named in the plan; it is in-process, which is correct for the single-replica M1 deployment
    /// (D-027 leaves scale-out to a database lease). Thread-safe.
    /// </summary>
    public class ConversationLockManager
    {
        #region Private-Members

        private readonly object _Lock = new object();
        private readonly Dictionary<string, ConversationLockEntry> _Entries = new Dictionary<string, ConversationLockEntry>(StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Acquire the lock for a conversation.
        /// </summary>
        /// <param name="conversationId">Conversation ID.</param>
        /// <param name="wait">How long to wait; zero fails immediately when held.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Lease to dispose.</returns>
        /// <exception cref="ArgumentNullException">Thrown when conversationId is null.</exception>
        /// <exception cref="AgentTurnConflictException">Thrown when the lock is not free within the wait.</exception>
        /// <exception cref="OperationCanceledException">Thrown when cancelled.</exception>
        public async Task<ConversationLockLease> AcquireAsync(string conversationId, TimeSpan wait, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(conversationId);
            ConversationLockEntry entry;
            lock (_Lock)
            {
                if (!_Entries.TryGetValue(conversationId, out ConversationLockEntry? existing))
                {
                    existing = new ConversationLockEntry();
                    _Entries[conversationId] = existing;
                }
                existing.References++;
                entry = existing;
            }

            bool acquired = false;
            try
            {
                acquired = await entry.Semaphore.WaitAsync(wait < TimeSpan.Zero ? TimeSpan.Zero : wait, token).ConfigureAwait(false);
            }
            finally
            {
                if (!acquired) Dereference(conversationId, entry);
            }
            if (!acquired) throw new AgentTurnConflictException(conversationId);
            return new ConversationLockLease(this, conversationId);
        }

        /// <summary>
        /// Whether a turn currently holds the conversation's lock.
        /// </summary>
        /// <param name="conversationId">Conversation ID.</param>
        /// <returns>True when held.</returns>
        public bool IsHeld(string conversationId)
        {
            lock (_Lock)
            {
                if (conversationId == null || !_Entries.TryGetValue(conversationId, out ConversationLockEntry? entry)) return false;
                return entry.Semaphore.CurrentCount == 0;
            }
        }

        /// <summary>
        /// Release a held lock. Called by <see cref="ConversationLockLease"/>.
        /// </summary>
        /// <param name="conversationId">Conversation ID.</param>
        public void Release(string conversationId)
        {
            ConversationLockEntry? entry;
            lock (_Lock)
            {
                if (conversationId == null || !_Entries.TryGetValue(conversationId, out entry)) return;
            }
            entry.Semaphore.Release();
            Dereference(conversationId, entry);
        }

        #endregion

        #region Private-Methods

        private void Dereference(string conversationId, ConversationLockEntry entry)
        {
            lock (_Lock)
            {
                entry.References--;
                if (entry.References <= 0 && _Entries.TryGetValue(conversationId, out ConversationLockEntry? current) && ReferenceEquals(current, entry))
                    _Entries.Remove(conversationId);
            }
        }

        #endregion
    }
}
