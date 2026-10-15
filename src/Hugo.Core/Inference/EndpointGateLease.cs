namespace Hugo.Core.Inference
{
    using System;
    using System.Threading;

    /// <summary>
    /// A held slot of an <see cref="EndpointGate"/>. Dispose to release. Releasing twice is a no-op. Thread-safe.
    /// </summary>
    public class EndpointGateLease : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Milliseconds spent waiting in the queue before the slot was granted.
        /// </summary>
        public long QueuedMs { get; }

        #endregion

        #region Private-Members

        private SemaphoreSlim? _Semaphore;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="semaphore">Semaphore to release, or null for an unlimited gate.</param>
        /// <param name="queuedMs">Queue wait in milliseconds.</param>
        public EndpointGateLease(SemaphoreSlim? semaphore, long queuedMs)
        {
            _Semaphore = semaphore;
            QueuedMs = queuedMs;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Release the slot.
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
            SemaphoreSlim? semaphore = Interlocked.Exchange(ref _Semaphore, null);
            if (semaphore != null) semaphore.Release();
        }

        #endregion
    }
}
