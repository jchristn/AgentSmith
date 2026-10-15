namespace Hugo.Core.Database
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A held database-wide lock (the startup lock that serializes migrations and seeding across replicas). Dispose to
    /// release it. Releasing twice is harmless. Thread-safe.
    /// </summary>
    public class DatabaseLockHandle : IDisposable, IAsyncDisposable
    {
        #region Private-Members

        private readonly Func<Task> _Release;
        private int _Released = 0;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="release">Releases the lock.</param>
        /// <exception cref="ArgumentNullException">Thrown when release is null.</exception>
        public DatabaseLockHandle(Func<Task> release)
        {
            _Release = release ?? throw new ArgumentNullException(nameof(release));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Release the lock.
        /// </summary>
        /// <returns>Task.</returns>
        public async Task ReleaseAsync()
        {
            if (Interlocked.Exchange(ref _Released, 1) == 1) return;
            await _Release().ConfigureAwait(false);
        }

        /// <summary>
        /// Dispose.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Dispose asynchronously.
        /// </summary>
        /// <returns>Value task.</returns>
        public async ValueTask DisposeAsync()
        {
            await ReleaseAsync().ConfigureAwait(false);
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose pattern implementation.
        /// </summary>
        /// <param name="disposing">True when called from Dispose.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing) ReleaseAsync().GetAwaiter().GetResult();
            _Disposed = true;
        }

        #endregion
    }
}
