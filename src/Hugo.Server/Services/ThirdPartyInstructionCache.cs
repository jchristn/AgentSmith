namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Connectors;
    using Hugo.Core.Instructions;
    using Hugo.Core.Profiles;

    /// <summary>
    /// Supplies third-party guidance to the instruction compiler (P13.28) for every connector that implements
    /// <see cref="IRemoteInstructionsProvider"/>, without the compiler or the core knowing which connectors those are.
    /// Fetches go through <see cref="ResourceService.RemoteInstructionsAsync"/> (bounded by its timeout, scrubbed with the
    /// resource's secrets) and are cached per resource version. A compile waits at most <see cref="CompileWaitMilliseconds"/>
    /// for a first fetch; when that wait runs out the fetch finishes in the background and <see cref="Changed"/> fires so
    /// the host can drop cached compilations. Expired entries are served stale while a refresh runs. Failures are cached
    /// for <see cref="FailureRetrySeconds"/>, so an unreachable remote costs at most one short wait per retry window.
    /// Thread-safe.
    /// </summary>
    public class ThirdPartyInstructionCache : IThirdPartyInstructionSource
    {
        #region Public-Members

        /// <summary>
        /// Raised with the tenant identifier when a fetch finished after a compile stopped waiting for it, or when a
        /// refresh changed the text, so cached compilations of that tenant can be dropped.
        /// </summary>
        public event EventHandler<string>? Changed;

        /// <summary>
        /// Longest a compile waits for a first fetch, in milliseconds. Default 2000, minimum 0 (never wait; the text
        /// appears once the background fetch finishes), maximum 60000.
        /// </summary>
        public int CompileWaitMilliseconds
        {
            get
            {
                return _CompileWaitMilliseconds;
            }
            set
            {
                _CompileWaitMilliseconds = Math.Clamp(value, 0, 60000);
            }
        }

        /// <summary>
        /// How long fetched text is served before it is refreshed in the background, in seconds. Default 600, minimum 0
        /// (refresh on every compile), maximum 86400.
        /// </summary>
        public int TimeToLiveSeconds
        {
            get
            {
                return _TimeToLiveSeconds;
            }
            set
            {
                _TimeToLiveSeconds = Math.Clamp(value, 0, 86400);
            }
        }

        /// <summary>
        /// How long a failed fetch is remembered before it is tried again, in seconds. Default 60, minimum 0, maximum 86400.
        /// </summary>
        public int FailureRetrySeconds
        {
            get
            {
                return _FailureRetrySeconds;
            }
            set
            {
                _FailureRetrySeconds = Math.Clamp(value, 0, 86400);
            }
        }

        /// <summary>
        /// Maximum cached entries; the cache is cleared when it grows past this. Default 4096, minimum 1, maximum 1000000.
        /// </summary>
        public int MaxEntries
        {
            get
            {
                return _MaxEntries;
            }
            set
            {
                _MaxEntries = Math.Clamp(value, 1, 1000000);
            }
        }

        #endregion

        #region Private-Members

        private readonly ConnectorRegistry _Registry;
        private readonly ResourceService _Resources;
        private readonly ConcurrentDictionary<string, ThirdPartyInstructionEntry> _Entries = new ConcurrentDictionary<string, ThirdPartyInstructionEntry>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, Task<ThirdPartyInstructionEntry>> _InFlight = new ConcurrentDictionary<string, Task<ThirdPartyInstructionEntry>>(StringComparer.Ordinal);
        private int _CompileWaitMilliseconds = 2000;
        private int _TimeToLiveSeconds = 600;
        private int _FailureRetrySeconds = 60;
        private int _MaxEntries = 4096;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="registry">Connector registry (decides which resources offer third-party guidance).</param>
        /// <param name="resources">Resource service (fetches, scrubs, and bounds each request).</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public ThirdPartyInstructionCache(ConnectorRegistry registry, ResourceService resources)
        {
            _Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _Resources = resources ?? throw new ArgumentNullException(nameof(resources));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The resource's scrubbed, unfenced third-party guidance from the cache, or from a fetch that finishes within
        /// <see cref="CompileWaitMilliseconds"/>; null otherwise.
        /// </summary>
        /// <param name="item">Bound resource.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The text, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when item is null.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        public async Task<string?> GetAsync(BoundResource item, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(item);
            token.ThrowIfCancellationRequested();
            if (_Registry.Get(item.Resource.ConnectorType) is not IRemoteInstructionsProvider) return null;
            string tenantId = item.Resource.TenantId;
            string key = tenantId + "|" + item.Resource.Id + "|" + item.Resource.LastUpdateUtc.Ticks.ToString(CultureInfo.InvariantCulture);

            if (_Entries.TryGetValue(key, out ThirdPartyInstructionEntry? cached))
            {
                if (!Expired(cached)) return cached.Text;
                // Serve the stale text now; a background refresh announces any change through Changed.
                _ = Fetch(key, tenantId, item.Resource.Id, item.Binding.Alias, cached.Text);
                return cached.Text;
            }

            Task<ThirdPartyInstructionEntry> fetch = Fetch(key, tenantId, item.Resource.Id, item.Binding.Alias, null);
            if (!fetch.IsCompleted && _CompileWaitMilliseconds > 0)
            {
                using (CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    Task delay = Task.Delay(_CompileWaitMilliseconds, wait.Token);
                    await Task.WhenAny(fetch, delay).ConfigureAwait(false);
                    wait.Cancel();
                }
                token.ThrowIfCancellationRequested();
            }
            if (fetch.IsCompletedSuccessfully) return fetch.Result.Text;
            // The compile goes on without the text; once the fetch lands, cached compilations are dropped.
            _ = fetch.ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully && t.Result.Text != null) Changed?.Invoke(this, tenantId);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return null;
        }

        /// <summary>
        /// Drop every cached entry.
        /// </summary>
        public void Clear()
        {
            _Entries.Clear();
        }

        #endregion

        #region Private-Methods

        private bool Expired(ThirdPartyInstructionEntry entry)
        {
            int seconds = entry.Error == null ? _TimeToLiveSeconds : _FailureRetrySeconds;
            return DateTime.UtcNow - entry.FetchedUtc >= TimeSpan.FromSeconds(seconds);
        }

        private Task<ThirdPartyInstructionEntry> Fetch(string key, string tenantId, string resourceId, string alias, string? previous)
        {
            Task<ThirdPartyInstructionEntry> task = _InFlight.GetOrAdd(key, k => Task.Run(() => FetchAsync(k, tenantId, resourceId, alias, previous)));
            _ = task.ContinueWith(t => _InFlight.TryRemove(new KeyValuePair<string, Task<ThirdPartyInstructionEntry>>(key, t)), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return task;
        }

        private async Task<ThirdPartyInstructionEntry> FetchAsync(string key, string tenantId, string resourceId, string alias, string? previous)
        {
            ThirdPartyInstructionEntry entry = new ThirdPartyInstructionEntry();
            try
            {
                // Not tied to the caller's token: a compile that stops waiting must not abandon the fetch. The resource
                // service bounds it with its own timeout.
                RemoteInstructionsResult result = await _Resources.RemoteInstructionsAsync(null, tenantId, resourceId, alias, CancellationToken.None).ConfigureAwait(false);
                entry.Text = String.IsNullOrWhiteSpace(result.ScrubbedText) ? null : result.ScrubbedText;
                entry.Error = result.Error;
            }
            catch (Exception)
            {
                entry.Text = null;
                entry.Error = ConnectorErrorCodes.ConnectionFailed;
            }
            // A failed refresh keeps serving the last good text until the next retry window.
            if (entry.Error != null && previous != null) entry.Text = previous;
            entry.FetchedUtc = DateTime.UtcNow;
            if (_Entries.Count >= _MaxEntries) _Entries.Clear();
            _Entries[key] = entry;
            if (previous != null && !String.Equals(previous, entry.Text, StringComparison.Ordinal)) Changed?.Invoke(this, tenantId);
            return entry;
        }

        #endregion
    }
}
