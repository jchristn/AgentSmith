namespace Test.Shared.Connectors.Aws
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Session pool that counts created sessions and invalidations. Thread-safe.
    /// </summary>
    public class CountingPool : IConnectorSessionPool, IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Sessions created.
        /// </summary>
        public int Created
        {
            get
            {
                return _Created;
            }
        }

        /// <summary>
        /// Invalidations requested.
        /// </summary>
        public int Invalidations
        {
            get
            {
                return _Invalidations;
            }
        }

        #endregion

        #region Private-Members

        private readonly NoPoolSessionPool _Inner = new NoPoolSessionPool();
        private int _Created = 0;
        private int _Invalidations = 0;
        private bool _Disposed = false;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken token = default) where T : class, IDisposable
        {
            ArgumentNullException.ThrowIfNull(factory);
            return _Inner.GetOrCreateAsync<T>(key, ct =>
            {
                Interlocked.Increment(ref _Created);
                return factory(ct);
            }, token);
        }

        /// <inheritdoc />
        public void Invalidate(string key)
        {
            Interlocked.Increment(ref _Invalidations);
            _Inner.Invalidate(key);
        }

        /// <summary>
        /// Dispose pooled sessions.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose pooled sessions.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing) _Inner.Dispose();
            _Disposed = true;
        }

        #endregion
    }
}
