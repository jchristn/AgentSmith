namespace Hugo.Sdk.Clients
{
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Sdk.Http;

    /// <summary>
    /// Server-sent events of the caller's tenant (/v1.0/api/events): stream.opened, heartbeat, secret.created,
    /// secret.updated, secret.deleted, settings.changed, approval and tool invocation events, and more.
    /// </summary>
    public class EventsClient : ClientBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="transport">Transport.</param>
        public EventsClient(HugoTransport transport) : base(transport)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Open the event stream. The stream runs until the token is cancelled or the server closes it; break out of
        /// the loop or cancel the token to close it.
        /// </summary>
        /// <param name="includeHeartbeats">Whether heartbeat events are yielded. Default false.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Events.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public async IAsyncEnumerable<SseEvent> StreamAsync(bool includeHeartbeats = false, [EnumeratorCancellation] CancellationToken token = default)
        {
            await foreach (SseEvent item in Transport.StreamAsync(HttpMethod.Get, "/v1.0/api/events", null, null, token).ConfigureAwait(false))
            {
                if (!includeHeartbeats && item.Event == "heartbeat") continue;
                yield return item;
            }
        }

        #endregion
    }
}
