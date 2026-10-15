namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading;
    using Hugo.Core.Serialization;
    using Hugo.Core.Telemetry;

    /// <summary>
    /// In-process publish and subscribe for server events (P1.35): approvals, tool invocations, health changes, settings
    /// and restart notices, and secret and key rotation progress. Subscribers see their own tenant's events; system
    /// administrators see every event. Events are not persisted (D-027: nothing relies on them for correctness).
    /// Thread-safe.
    /// </summary>
    public class EventHub
    {
        #region Public-Members

        /// <summary>
        /// Queue capacity per subscriber. Minimum 16, maximum 100000, default 1000.
        /// </summary>
        public int SubscriberCapacity
        {
            get
            {
                return _SubscriberCapacity;
            }
            set
            {
                _SubscriberCapacity = Math.Clamp(value, 16, 100000);
            }
        }

        /// <summary>
        /// Seconds between heartbeats on idle streams. Minimum 1, maximum 300, default 15.
        /// </summary>
        public int HeartbeatSeconds
        {
            get
            {
                return _HeartbeatSeconds;
            }
            set
            {
                _HeartbeatSeconds = Math.Clamp(value, 1, 300);
            }
        }

        /// <summary>
        /// Current subscriber count.
        /// </summary>
        public int SubscriberCount
        {
            get
            {
                return _Subscriptions.Count;
            }
        }

        #endregion

        #region Private-Members

        private readonly ConcurrentDictionary<EventSubscription, byte> _Subscriptions = new ConcurrentDictionary<EventSubscription, byte>();
        private long _Sequence = 0;
        private int _SubscriberCapacity = 1000;
        private int _HeartbeatSeconds = 15;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Publish an event.
        /// </summary>
        /// <param name="type">Event type.</param>
        /// <param name="tenantId">Tenant, or null for a system event.</param>
        /// <param name="payload">Payload object, serialized as JSON; null publishes {}.</param>
        /// <returns>The published event.</returns>
        /// <exception cref="ArgumentNullException">Thrown when type is null or empty.</exception>
        public HubEvent Publish(string type, string? tenantId, object? payload)
        {
            if (String.IsNullOrEmpty(type)) throw new ArgumentNullException(nameof(type));
            HubEvent hubEvent = new HubEvent();
            hubEvent.Sequence = Interlocked.Increment(ref _Sequence);
            hubEvent.Type = type;
            hubEvent.TenantId = tenantId;
            hubEvent.Data = payload == null ? "{}" : Serializer.Serialize(payload);
            foreach (EventSubscription subscription in _Subscriptions.Keys)
            {
                if (subscription.Accepts(hubEvent)) subscription.Offer(hubEvent);
            }
            return hubEvent;
        }

        /// <summary>
        /// Subscribe.
        /// </summary>
        /// <param name="tenantId">Tenant filter (for administrators, null means every tenant).</param>
        /// <param name="includeAllTenants">True for system administrators.</param>
        /// <returns>Subscription; dispose to unsubscribe.</returns>
        public EventSubscription Subscribe(string? tenantId, bool includeAllTenants)
        {
            EventSubscription subscription = new EventSubscription(tenantId, includeAllTenants, _SubscriberCapacity, s => _Subscriptions.TryRemove(s, out byte _));
            _Subscriptions[subscription] = 0;
            return subscription;
        }

        #endregion
    }
}
