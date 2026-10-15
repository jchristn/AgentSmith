namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// The server's connector session pool (P3.9, plan section 8.1). Sessions are keyed by the connector (usually
    /// ConnectorResourceContext.SessionKey: type, resource, and configuration hash), created once per key under a
    /// per-key lock, evicted when idle longer than <see cref="IdleTimeout"/> or when the pool exceeds
    /// <see cref="MaxSessions"/> (least recently used first), and dropped for a resource whenever the resource changes.
    /// It also holds the per-resource concurrency gates (a SemaphoreSlim per resource rather than Padlock, the same
    /// choice P6.20 made). Thread-safe.
    /// </summary>
    public class ConnectorSessionPool : IConnectorSessionPool, IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Idle time after which a session is disposed. Default 10 minutes, minimum 1 second.
        /// </summary>
        public TimeSpan IdleTimeout
        {
            get
            {
                return _IdleTimeout;
            }
            set
            {
                _IdleTimeout = value < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : value;
            }
        }

        /// <summary>
        /// Maximum pooled sessions. Default 256, minimum 1, maximum 100000.
        /// </summary>
        public int MaxSessions
        {
            get
            {
                return _MaxSessions;
            }
            set
            {
                _MaxSessions = Math.Clamp(value, 1, 100000);
            }
        }

        /// <summary>
        /// Concurrent calls allowed per resource. Read when a resource's gate is first created. Default 4, minimum 1.
        /// </summary>
        public Func<int> MaxConcurrencyPerResource
        {
            get
            {
                return _MaxConcurrency;
            }
            set
            {
                _MaxConcurrency = value ?? (() => 4);
            }
        }

        /// <summary>
        /// Sessions currently pooled.
        /// </summary>
        public int Count
        {
            get
            {
                return _Sessions.Count;
            }
        }

        #endregion

        #region Private-Members

        private readonly ConcurrentDictionary<string, PooledSession> _Sessions = new ConcurrentDictionary<string, PooledSession>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _CreateLocks = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _Gates = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal);
        private TimeSpan _IdleTimeout = TimeSpan.FromMinutes(10);
        private int _MaxSessions = 256;
        private Func<int> _MaxConcurrency = () => 4;
        private bool _Disposed = false;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken token = default) where T : class, IDisposable
        {
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (_Disposed) throw new ObjectDisposedException(nameof(ConnectorSessionPool));
            if (_Sessions.TryGetValue(key, out PooledSession? existing) && existing.Session is T typed)
            {
                existing.LastUsedUtc = DateTime.UtcNow;
                return typed;
            }
            SemaphoreSlim createLock = _CreateLocks.GetOrAdd(key, k => new SemaphoreSlim(1, 1));
            await createLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_Sessions.TryGetValue(key, out existing))
                {
                    if (existing.Session is T again)
                    {
                        existing.LastUsedUtc = DateTime.UtcNow;
                        return again;
                    }
                    Invalidate(key);
                }
                T created = await factory(token).ConfigureAwait(false);
                _Sessions[key] = new PooledSession(created);
                if (_Sessions.Count > _MaxSessions) EvictOldest(_Sessions.Count - _MaxSessions);
                return created;
            }
            finally
            {
                createLock.Release();
            }
        }

        /// <inheritdoc />
        public void Invalidate(string key)
        {
            if (String.IsNullOrEmpty(key)) return;
            if (_Sessions.TryRemove(key, out PooledSession? session)) DisposeQuietly(session.Session);
        }

        /// <summary>
        /// Drop every session of a resource (called when the resource or one of its secrets changes, or it is deleted).
        /// </summary>
        /// <param name="resourceId">Resource identifier.</param>
        /// <returns>Sessions dropped.</returns>
        public int InvalidateResource(string resourceId)
        {
            if (String.IsNullOrEmpty(resourceId)) return 0;
            int dropped = 0;
            string marker = ":" + resourceId + ":";
            foreach (string key in _Sessions.Keys.Where(k => k.Contains(marker, StringComparison.Ordinal)).ToList())
            {
                Invalidate(key);
                dropped++;
            }
            return dropped;
        }

        /// <summary>
        /// Dispose sessions idle longer than <see cref="IdleTimeout"/>.
        /// </summary>
        /// <returns>Sessions disposed.</returns>
        public int Sweep()
        {
            DateTime cutoff = DateTime.UtcNow - _IdleTimeout;
            int swept = 0;
            foreach (KeyValuePair<string, PooledSession> entry in _Sessions.ToList())
            {
                if (entry.Value.LastUsedUtc >= cutoff) continue;
                Invalidate(entry.Key);
                swept++;
            }
            return swept;
        }

        /// <summary>
        /// Wait for a slot of a resource's concurrency gate.
        /// </summary>
        /// <param name="resourceId">Resource identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Lease; dispose it to release the slot.</returns>
        /// <exception cref="OperationCanceledException">Thrown when cancelled while waiting.</exception>
        public async Task<ResourceGateLease> AcquireAsync(string resourceId, CancellationToken token)
        {
            if (String.IsNullOrEmpty(resourceId)) throw new ArgumentNullException(nameof(resourceId));
            SemaphoreSlim gate = _Gates.GetOrAdd(resourceId, k =>
            {
                int slots = Math.Max(1, _MaxConcurrency());
                return new SemaphoreSlim(slots, slots);
            });
            await gate.WaitAsync(token).ConfigureAwait(false);
            return new ResourceGateLease(gate);
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
        /// Dispose sessions and locks.
        /// </summary>
        /// <param name="disposing">Whether called from Dispose.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                foreach (string key in _Sessions.Keys.ToList()) Invalidate(key);
            }
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private void EvictOldest(int count)
        {
            foreach (KeyValuePair<string, PooledSession> entry in _Sessions.OrderBy(e => e.Value.LastUsedUtc).Take(count).ToList()) Invalidate(entry.Key);
        }

        private static void DisposeQuietly(IDisposable session)
        {
            try
            {
                session.Dispose();
            }
            catch (Exception)
            {
                // A session that fails to close is dropped anyway.
            }
        }

        #endregion
    }
}
