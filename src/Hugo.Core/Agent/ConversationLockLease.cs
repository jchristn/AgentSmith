namespace Hugo.Core.Agent
{
    using System;
    using System.Threading;

    /// <summary>
    /// A held conversation lock. Dispose to release; releasing twice is a no-op. Thread-safe.
    /// </summary>
    public class ConversationLockLease : IDisposable
    {
        #region Private-Members

        private readonly ConversationLockManager _Manager;
        private readonly string _Key;
        private int _Released = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="manager">Owning manager.</param>
        /// <param name="key">Conversation key.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public ConversationLockLease(ConversationLockManager manager, string key)
        {
            _Manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _Key = key ?? throw new ArgumentNullException(nameof(key));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Release the lock.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose pattern.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _Released, 1) == 1) return;
            _Manager.Release(_Key);
        }

        #endregion
    }
}
