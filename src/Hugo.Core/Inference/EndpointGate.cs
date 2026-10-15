namespace Hugo.Core.Inference
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Per-endpoint concurrency limit with a bounded queue wait (AssistantHub <c>EndpointConcurrencyLimiter</c>
    /// pattern). Callers acquire a lease before calling an endpoint and dispose it when the response, including
    /// any stream, is finished. When an endpoint's limit changes, new callers use a fresh semaphore with the new
    /// limit while existing leases release the old one. Thread-safe.
    /// </summary>
    public class EndpointGate
    {
        #region Private-Members

        private readonly object _Lock = new object();
        private readonly Dictionary<string, SemaphoreSlim> _Semaphores = new Dictionary<string, SemaphoreSlim>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _Limits = new Dictionary<string, int>(StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Acquire a slot for an endpoint.
        /// </summary>
        /// <param name="endpointKey">Endpoint key.</param>
        /// <param name="maxConcurrency">Concurrency limit; 0 or less means unlimited.</param>
        /// <param name="queueTimeout">Longest wait for a slot.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Lease to dispose when done.</returns>
        /// <exception cref="ArgumentNullException">Thrown when endpointKey is null.</exception>
        /// <exception cref="ModelEndpointException">Thrown with code <c>queue_timeout</c> when no slot frees in time.</exception>
        /// <exception cref="OperationCanceledException">Thrown when cancelled.</exception>
        public async Task<EndpointGateLease> AcquireAsync(string endpointKey, int maxConcurrency, TimeSpan queueTimeout, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(endpointKey);
            if (maxConcurrency <= 0) return new EndpointGateLease(null, 0);
            SemaphoreSlim semaphore = GetSemaphore(endpointKey, maxConcurrency);
            long start = Stopwatch.GetTimestamp();
            bool acquired = await semaphore.WaitAsync(queueTimeout, token).ConfigureAwait(false);
            if (!acquired)
                throw new ModelEndpointException("queue_timeout", endpointKey, "The model endpoint is at its concurrency limit (" + maxConcurrency + ") and no slot freed within " + queueTimeout.TotalSeconds.ToString("0.#") + " seconds.", null, true);
            return new EndpointGateLease(semaphore, (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }

        /// <summary>
        /// Number of free slots for an endpoint, or -1 when the endpoint has no gate yet.
        /// </summary>
        /// <param name="endpointKey">Endpoint key.</param>
        /// <returns>Free slots.</returns>
        public int Available(string endpointKey)
        {
            lock (_Lock)
            {
                if (endpointKey == null || !_Semaphores.TryGetValue(endpointKey, out SemaphoreSlim? semaphore)) return -1;
                return semaphore.CurrentCount;
            }
        }

        #endregion

        #region Private-Methods

        private SemaphoreSlim GetSemaphore(string endpointKey, int maxConcurrency)
        {
            lock (_Lock)
            {
                if (_Semaphores.TryGetValue(endpointKey, out SemaphoreSlim? existing) && _Limits[endpointKey] == maxConcurrency) return existing;
                SemaphoreSlim created = new SemaphoreSlim(maxConcurrency, maxConcurrency);
                _Semaphores[endpointKey] = created;
                _Limits[endpointKey] = maxConcurrency;
                return created;
            }
        }

        #endregion
    }
}
