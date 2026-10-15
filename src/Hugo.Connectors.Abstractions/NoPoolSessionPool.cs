namespace Hugo.Connectors.Abstractions
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Simple in-memory session pool without eviction, suitable for tests and tools. Disposing the pool disposes every
    /// session. Thread-safe.
    /// </summary>
    public class NoPoolSessionPool : IConnectorSessionPool, IDisposable
    {
        #region Private-Members

        private readonly ConcurrentDictionary<string, IDisposable> _Sessions = new ConcurrentDictionary<string, IDisposable>(StringComparer.Ordinal);
        private readonly SemaphoreSlim _Lock = new SemaphoreSlim(1, 1);
        private bool _Disposed = false;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken token = default) where T : class, IDisposable
        {
            ArgumentNullException.ThrowIfNull(factory);
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            if (_Sessions.TryGetValue(key, out IDisposable? existing) && existing is T typed) return typed;

            await _Lock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_Sessions.TryGetValue(key, out existing) && existing is T again) return again;
                T created = await factory(token).ConfigureAwait(false);
                _Sessions[key] = created;
                return created;
            }
            finally
            {
                _Lock.Release();
            }
        }

        /// <inheritdoc />
        public void Invalidate(string key)
        {
            if (String.IsNullOrEmpty(key)) return;
            if (_Sessions.TryRemove(key, out IDisposable? session))
            {
                try
                {
                    session.Dispose();
                }
                catch (ObjectDisposedException)
                {
                }
            }
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
            if (disposing)
            {
                foreach (string key in _Sessions.Keys) Invalidate(key);
                _Lock.Dispose();
            }
            _Disposed = true;
        }

        #endregion
    }
}
