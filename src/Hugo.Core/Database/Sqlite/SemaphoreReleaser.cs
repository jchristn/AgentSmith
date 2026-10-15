namespace Hugo.Core.Database.Sqlite
{
    using System;
    using System.Threading;

    /// <summary>
    /// Releases a semaphore once when disposed.
    /// </summary>
    public sealed class SemaphoreReleaser : IDisposable
    {
        #region Private-Members

        private SemaphoreSlim? _Semaphore;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="semaphore">Semaphore already entered by the caller.</param>
        /// <exception cref="ArgumentNullException">Thrown when semaphore is null.</exception>
        public SemaphoreReleaser(SemaphoreSlim semaphore)
        {
            _Semaphore = semaphore ?? throw new ArgumentNullException(nameof(semaphore));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Release the semaphore.
        /// </summary>
        public void Dispose()
        {
            SemaphoreSlim? semaphore = Interlocked.Exchange(ref _Semaphore, null);
            semaphore?.Release();
        }

        #endregion
    }
}
