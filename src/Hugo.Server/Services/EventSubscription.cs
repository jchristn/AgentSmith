namespace Hugo.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;

    /// <summary>
    /// A subscriber's view of the event hub: a bounded queue of events it is allowed to see. Dispose to unsubscribe.
    /// Thread-safe.
    /// </summary>
    public class EventSubscription : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Tenant filter: events of this tenant only; null with <see cref="IncludeAllTenants"/> receives every event.
        /// </summary>
        public string? TenantId { get; }

        /// <summary>
        /// Whether the subscriber is a system administrator receiving every tenant's events and system events.
        /// </summary>
        public bool IncludeAllTenants { get; }

        /// <summary>
        /// Events dropped because the subscriber fell behind.
        /// </summary>
        public long Dropped
        {
            get
            {
                return Interlocked.Read(ref _Dropped);
            }
        }

        #endregion

        #region Private-Members

        private readonly Channel<HubEvent> _Channel;
        private readonly Action<EventSubscription> _OnDispose;
        private long _Dropped = 0;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="tenantId">Tenant filter.</param>
        /// <param name="includeAllTenants">Whether every event is delivered.</param>
        /// <param name="capacity">Queue capacity. Minimum 16, maximum 100000.</param>
        /// <param name="onDispose">Called once on dispose (the hub removes the subscription).</param>
        /// <exception cref="ArgumentNullException">Thrown when onDispose is null.</exception>
        public EventSubscription(string? tenantId, bool includeAllTenants, int capacity, Action<EventSubscription> onDispose)
        {
            TenantId = tenantId;
            IncludeAllTenants = includeAllTenants;
            _OnDispose = onDispose ?? throw new ArgumentNullException(nameof(onDispose));
            BoundedChannelOptions options = new BoundedChannelOptions(Math.Clamp(capacity, 16, 100000));
            options.FullMode = BoundedChannelFullMode.DropOldest;
            options.SingleReader = true;
            _Channel = Channel.CreateBounded<HubEvent>(options);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether this subscription may see an event.
        /// </summary>
        /// <param name="hubEvent">Event.</param>
        /// <returns>True when visible.</returns>
        public bool Accepts(HubEvent hubEvent)
        {
            if (hubEvent == null) return false;
            if (IncludeAllTenants) return TenantId == null || hubEvent.TenantId == null || hubEvent.TenantId == TenantId;
            return hubEvent.TenantId != null && hubEvent.TenantId == TenantId;
        }

        /// <summary>
        /// Queue an event (the oldest is dropped when the subscriber is behind).
        /// </summary>
        /// <param name="hubEvent">Event.</param>
        public void Offer(HubEvent hubEvent)
        {
            if (_Disposed || hubEvent == null) return;
            if (!_Channel.Writer.TryWrite(hubEvent)) Interlocked.Increment(ref _Dropped);
        }

        /// <summary>
        /// Wait for the next event.
        /// </summary>
        /// <param name="timeout">Longest wait.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Event, or null on timeout.</returns>
        public async Task<HubEvent?> NextAsync(TimeSpan timeout, CancellationToken token = default)
        {
            if (_Channel.Reader.TryRead(out HubEvent? ready)) return ready;
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                linked.CancelAfter(timeout);
                try
                {
                    if (await _Channel.Reader.WaitToReadAsync(linked.Token).ConfigureAwait(false) && _Channel.Reader.TryRead(out HubEvent? next)) return next;
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                }
            }
            return null;
        }

        /// <summary>
        /// Dispose.
        /// </summary>
        public void Dispose()
        {
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
            _Disposed = true;
            if (disposing)
            {
                _Channel.Writer.TryComplete();
                _OnDispose(this);
            }
        }

        #endregion
    }
}
