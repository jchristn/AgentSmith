namespace Hugo.Server.Routes
{
    using System;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Enums;
    using Hugo.Core.Security;
    using Hugo.Core.Telemetry;
    using Hugo.Server.Services;
    using WatsonWebserver.Core;

    /// <summary>
    /// GET /v1.0/api/events (P1.35): a Server-Sent Events stream of the caller's tenant events with heartbeats. System
    /// administrators receive every event; the x-tenant-id header narrows them to one tenant.
    /// </summary>
    public class EventRoutes
    {
        #region Private-Members

        private readonly ServerContext _Context;
        private readonly EventHub _Hub;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Server context.</param>
        /// <param name="hub">Event hub.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public EventRoutes(ServerContext context, EventHub hub)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            _Hub = hub ?? throw new ArgumentNullException(nameof(hub));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="routes">Registrar.</param>
        /// <exception cref="ArgumentNullException">Thrown when routes is null.</exception>
        public void Register(RouteRegistrar routes)
        {
            if (routes == null) throw new ArgumentNullException(nameof(routes));
            routes.Scoped(HttpMethod.GET, "/v1.0/api/events", ResourceTypeEnum.Tenant, OperationTypeEnum.Read, StreamAsync, "Server-Sent Events stream of the caller's tenant events (heartbeat events while idle)", "Events");
        }

        #endregion

        #region Private-Methods

        private async Task StreamAsync(HttpContextBase http)
        {
            RequestContext ctx = RouteHelpers.Context(http);
            ctx.SuppressHistoryBodies = true;
            http.Response.StatusCode = 200;
            http.Response.ContentType = "text/event-stream";
            http.Response.Headers["Cache-Control"] = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no";
            http.Response.ServerSentEvents = true;

            bool admin = ctx.IsAdmin || ctx.PrincipalType == PrincipalTypeEnum.Administrator;
            using (EventSubscription subscription = _Hub.Subscribe(ctx.TenantId, admin))
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(http.Token, _Context.Stopping))
            {
                await http.Response.SendEvent(new ServerSentEvent { Event = "stream.opened", Data = "{\"tenantId\":" + (ctx.TenantId == null ? "null" : "\"" + ctx.TenantId + "\"") + "}" }, false, http.Token).ConfigureAwait(false);
                HugoMetrics.Count(HugoMetricCatalog.SseEvents, "stream", "events");
                while (!linked.IsCancellationRequested)
                {
                    HubEvent? next;
                    try
                    {
                        next = await subscription.NextAsync(TimeSpan.FromSeconds(_Hub.HeartbeatSeconds), linked.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    ServerSentEvent message = new ServerSentEvent();
                    if (next == null)
                    {
                        message.Event = "heartbeat";
                        message.Data = "{\"utc\":\"" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "\"}";
                    }
                    else
                    {
                        message.Id = next.Sequence.ToString(CultureInfo.InvariantCulture);
                        message.Event = next.Type;
                        message.Data = next.Data;
                    }
                    try
                    {
                        await http.Response.SendEvent(message, false, http.Token).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is System.IO.IOException || e is ObjectDisposedException || e is OperationCanceledException)
                    {
                        return;
                    }
                    HugoMetrics.Count(HugoMetricCatalog.SseEvents, "stream", "events");
                }
                try
                {
                    await http.Response.SendEvent(new ServerSentEvent { Event = "stream.closed", Data = "{}" }, true, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception e) when (e is System.IO.IOException || e is ObjectDisposedException || e is OperationCanceledException)
                {
                }
            }
        }

        #endregion
    }
}
