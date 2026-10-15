namespace Hugo.Connectors.Abstractions
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Pool of reusable connector sessions (SSH clients, Redis multiplexers, HTTP clients) keyed by the connector.
    /// The server evicts idle sessions and disposes every session of a resource when that resource changes.
    /// Implementations are thread-safe.
    /// </summary>
    public interface IConnectorSessionPool
    {
        /// <summary>
        /// Get a pooled session or create one. The key should include the resource identifier and a hash of every
        /// setting and secret that affects the session, so a changed resource never reuses a stale session.
        /// </summary>
        /// <typeparam name="T">Session type.</typeparam>
        /// <param name="key">Pool key.</param>
        /// <param name="factory">Factory invoked when no usable session exists.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The session. Callers must not dispose it.</returns>
        Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken token = default) where T : class, IDisposable;

        /// <summary>
        /// Dispose and remove a pooled session, for example after it failed.
        /// </summary>
        /// <param name="key">Pool key.</param>
        void Invalidate(string key);
    }
}
