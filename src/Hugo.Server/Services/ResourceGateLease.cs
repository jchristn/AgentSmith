namespace Hugo.Server.Services
{
    using System;
    using System.Threading;

    /// <summary>
    /// A held slot of a per-resource concurrency gate; disposing it releases the slot. Thread-safe.
    /// </summary>
    public class ResourceGateLease : IDisposable
    {
        #region Private-Members

        private readonly SemaphoreSlim _Gate;
        private int _Released = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate for a slot already acquired.
        /// </summary>
        /// <param name="gate">Gate.</param>
        /// <exception cref="ArgumentNullException">Thrown when gate is null.</exception>
        public ResourceGateLease(SemaphoreSlim gate)
        {
            _Gate = gate ?? throw new ArgumentNullException(nameof(gate));
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
        /// Release the slot once.
        /// </summary>
        /// <param name="disposing">Whether called from Dispose.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _Released, 1) == 1) return;
            if (disposing)
            {
                try
                {
                    _Gate.Release();
                }
                catch (ObjectDisposedException)
                {
                }
                catch (SemaphoreFullException)
                {
                }
            }
        }

        #endregion
    }
}
