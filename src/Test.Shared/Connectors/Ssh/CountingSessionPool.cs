namespace Test.Shared.Connectors.Ssh
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Session pool that counts how many sessions were created, for pooling tests. Thread-safe.
    /// </summary>
    public class CountingSessionPool : IConnectorSessionPool, IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Number of times a factory was invoked.
        /// </summary>
        public int Created
        {
            get
            {
                return _Created;
            }
        }

        #endregion

        #region Private-Members

        private readonly NoPoolSessionPool _Inner = new NoPoolSessionPool();
        private int _Created = 0;
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
            _Inner.Invalidate(key);
        }

        /// <summary>
        /// Dispose every session.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose every session.
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
