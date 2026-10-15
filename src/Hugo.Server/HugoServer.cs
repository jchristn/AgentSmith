namespace Hugo.Server
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core;
    using Hugo.Core.Enums;
    using Hugo.Core.Database;
    using Hugo.Core.Security;
    using Hugo.Core.Telemetry;
    using Hugo.Server.Modules;
    using Hugo.Server.Routes;
    using Hugo.Server.Services;
    using SyslogLogging;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// The Hugo host (P1.4). Owns the Watson listener, wires authentication, CORS preflight, OpenAPI, request timing,
    /// request history capture, and telemetry, and composes the feature modules. Thread-safe after start.
    /// </summary>
    public class HugoServer : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Server context.
        /// </summary>
        public ServerContext Context
        {
            get
            {
                return _Context;
            }
        }

        /// <summary>
        /// Modules in initialization order.
        /// </summary>
        public IReadOnlyList<IServerModule> Modules
        {
            get
            {
                return _Modules;
            }
        }

        /// <summary>
        /// Milliseconds <see cref="StopAsync"/> waits for in-flight requests after it starts draining. Minimum 0, maximum
        /// 120000, default 15000.
        /// </summary>
        public int ShutdownGraceMs
        {
            get
            {
                return _ShutdownGraceMs;
            }
            set
            {
                _ShutdownGraceMs = Math.Clamp(value, 0, 120000);
            }
        }

        /// <summary>
        /// Requests currently being handled.
        /// </summary>
        public int InFlightRequests
        {
            get
            {
                return _InFlight.Count;
            }
        }

        /// <summary>
        /// Route registrar (exposes the operation scope map).
        /// </summary>
        public RouteRegistrar Routes
        {
            get
            {
                return _Routes;
            }
        }

        /// <summary>
        /// The underlying Watson server (tests enumerate its routes).
        /// </summary>
        public Webserver Webserver
        {
            get
            {
                return _Server;
            }
        }

        #endregion

        #region Private-Members

        private readonly ServerContext _Context;
        private readonly Webserver _Server;
        private readonly RouteRegistrar _Routes;
        private readonly List<IServerModule> _Modules;
        private readonly CancellationTokenSource _Stopping = new CancellationTokenSource();
        private readonly string _Header = "[HugoServer] ";
        private readonly ConcurrentDictionary<Guid, DateTime> _InFlight = new ConcurrentDictionary<Guid, DateTime>();
        private readonly GaugeRegistration _InFlightGauge;
        private string _CorsOrigin = "*";
        private int _ShutdownGraceMs = 15000;
        private int _StopRequested = 0;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Server context.</param>
        /// <param name="modules">Modules; null uses the standard set.</param>
        /// <exception cref="ArgumentNullException">Thrown when context is null.</exception>
        public HugoServer(ServerContext context, List<IServerModule>? modules = null)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            _Modules = modules ?? new List<IServerModule>
            {
                new IdentityModule(),
                new ConfigurationModule(),
                new IntegrationModule(),
                new AgentModule(),
                new Hugo.Server.Protocols.McpModule()
            };

            WebserverSettings settings = new WebserverSettings();
            settings.Hostname = context.Settings.Rest.Hostname;
            settings.Port = context.Settings.Rest.Port;
            settings.Ssl.Enable = context.Settings.Rest.Ssl;
            // Watson's default response headers echo request-style headers (Accept, Accept-Language, Accept-Charset,
            // Cache-Control, Host) and generic CORS values. Hugo sends its own CORS headers on every response (AddCors),
            // so no defaults are added.
            settings.Headers.DefaultHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // Watson's built-in HTTP telemetry (http.server.*, one server span per request named "{method} {route}") is
            // the foundation; the Radiant pipeline subscribes to the Watson meter and source (P1.38).
            settings.Telemetry.Enable = true;
            settings.Telemetry.EnableMetrics = true;
            settings.Telemetry.EnableTraces = true;
            settings.Telemetry.PropagateContext = true;
            _Server = new Webserver(settings, DefaultRouteAsync);
            // The Watson constructor adds a Host default header; a response has no Host header.
            _Server.Settings.Headers.DefaultHeaders.Remove("Host");
            context.Register(_Server);
            if (context.TryGet<EventHub>() == null) context.Register(new EventHub());

            _Routes = new RouteRegistrar(_Server, context.Authorization, context.Database, context.Scopes, context.Logging);
            _Routes.DatabaseRetryAfterSeconds = () => context.Settings.Rest.DatabaseRetryAfterSeconds;
            context.Register(_Routes);
            _InFlightGauge = HugoMetrics.RegisterGauge(HugoMetricCatalog.HttpInFlight, () => _InFlight.Count);
            if (context.Settings.Rest.CorsOrigins.Count > 0) _CorsOrigin = String.Join(" ", context.Settings.Rest.CorsOrigins);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Initialize modules, register routes, start the listener, and start background work.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task StartAsync(CancellationToken token = default)
        {
            ConfigureServer();

            foreach (IServerModule module in _Modules)
            {
                await module.InitializeAsync(_Context, token).ConfigureAwait(false);
                _Context.Logging.Debug(_Header + "initialized " + module.Name);
            }

            new CoreRoutes(_Context).Register(_Routes);
            foreach (IServerModule module in _Modules) module.RegisterRoutes(_Context, _Routes);

            List<string> unmapped = _Routes.UnmappedRoutes();
            if (unmapped.Count > 0) _Context.Logging.Warn(_Header + "routes without an operation scope (they fail closed to administrators): " + String.Join(", ", unmapped));

            _Server.Start();
            _Context.Logging.Info(_Header + "listening on " + _Context.Settings.Rest.Hostname + ":" + _Context.Settings.Rest.Port + " with " + _Context.Scopes.All().Count + " routes");

            foreach (IServerModule module in _Modules)
            {
                await module.StartAsync(_Context, _Stopping.Token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Drain (in-flight requests get <see cref="ShutdownGraceMs"/>), then stop background work and the listener. Calls
        /// after the first return at once.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task StopAsync(CancellationToken token = default)
        {
            if (Interlocked.Exchange(ref _StopRequested, 1) == 1) return;

            _Context.BeginShutdown();
            _Context.Logging.Info(_Header + "draining (" + _InFlight.Count + " requests in flight)");
            DateTime graceEnds = DateTime.UtcNow.AddMilliseconds(_ShutdownGraceMs);
            while (!_InFlight.IsEmpty && DateTime.UtcNow < graceEnds && !token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(50, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            _Stopping.Cancel();
            foreach (IServerModule module in Enumerable.Reverse(_Modules))
            {
                try
                {
                    await module.StopAsync(token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _Context.Logging.Warn(_Header + "error stopping " + module.Name + ": " + e.Message);
                }
            }
            _Server.Stop();
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
            if (disposing)
            {
                _Stopping.Dispose();
                _InFlightGauge.Dispose();
                if (_Server is IDisposable disposable) disposable.Dispose();
            }
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private void ConfigureServer()
        {
            _Server.Routes.AuthenticateRequest = AuthenticateAsync;
            _Server.Routes.Exception = ExceptionRouteAsync;
            _Server.Routes.Preflight = PreflightAsync;
            _Server.Routes.PreRouting = PreRoutingAsync;
            _Server.Routes.PostRouting = PostRoutingAsync;
            _Server.UseOpenApi(openApi =>
            {
                openApi.Info.Title = "Hugo API";
                openApi.Info.Version = Constants.ProductVersion;
                openApi.Info.Description = "Hugo, a multi-tenant super agent server: identity, access control, secrets, prompt templates, and administration.";
            });
        }

        private async Task AuthenticateAsync(HttpContextBase context)
        {
            // Authentication reads sessions, credentials, and users. A database failure here answers with the JSON error
            // shape (503 with Retry-After), never Watson's HTML page.
            try
            {
                await _Context.Authentication.AuthenticateRequestAsync(context).ConfigureAwait(false);
                if (!context.Response.ResponseSent && context.Metadata is RequestContext ctx && ctx.IsAuthenticated && !ctx.IsAdmin
                    && !_Routes.IsMappedOrUnknown(context.Request.Method, context.Request.Url.RawWithoutQuery))
                {
                    // A route without an operation scope fails closed: only system administrators reach it.
                    await RouteHelpers.ErrorAsync(context, 403, "RouteNotMapped", "This route has no authorization mapping and is restricted to system administrators.").ConfigureAwait(false);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                context.Metadata = RequestContext.Unauthenticated();
                DatabaseFailureEnum failure = DatabaseFailures.Classify(e);
                _Context.Logging.Warn(_Header + "authenticating " + context.Request.Method + " " + context.Request.Url.RawWithoutQuery + " failed: " + SecretScrubber.Shared.Scrub(failure == DatabaseFailureEnum.None ? e.ToString() : "database " + failure.ToString().ToLowerInvariant() + ": " + e.Message));
                if (context.Response.ResponseSent) return;
                if (failure != DatabaseFailureEnum.None) await RouteHelpers.DatabaseFailureAsync(context, failure, _Context.Settings.Rest.DatabaseRetryAfterSeconds).ConfigureAwait(false);
                else await RouteHelpers.ErrorAsync(context, 500, "InternalError", "An internal error occurred. See server logs with the request identifier.").ConfigureAwait(false);
            }
        }

        private async Task ExceptionRouteAsync(HttpContextBase context, Exception e)
        {
            DatabaseFailureEnum failure = DatabaseFailures.Classify(e);
            _Context.Logging.Warn(_Header + context.Request.Method + " " + context.Request.Url.RawWithoutQuery + " escaped the route exception mapping: " + SecretScrubber.Shared.Scrub(e.ToString()));
            if (context.Response.ResponseSent) return;
            if (failure != DatabaseFailureEnum.None) await RouteHelpers.DatabaseFailureAsync(context, failure, _Context.Settings.Rest.DatabaseRetryAfterSeconds).ConfigureAwait(false);
            else await RouteHelpers.ErrorAsync(context, 500, "InternalError", "An internal error occurred. See server logs with the request identifier.").ConfigureAwait(false);
        }

        private async Task PreflightAsync(HttpContextBase context)
        {
            context.Response.StatusCode = 200;
            AddCors(context);
            context.Response.Headers.Add("Access-Control-Max-Age", "86400");
            await context.Response.Send().ConfigureAwait(false);
        }

        private Task PreRoutingAsync(HttpContextBase context)
        {
            _InFlight[context.Request.Guid] = DateTime.UtcNow;
            RequestTelemetry.CaptureSpan(context);

            // Every response carries x-request-id: the caller's value when it is reasonable, otherwise a new one that the
            // authentication layer and error bodies reuse.
            string? requestId = context.Request.Headers[Constants.HeaderRequestId];
            if (String.IsNullOrEmpty(requestId) || requestId.Length > 64)
            {
                requestId = context.Request.Guid.ToString("N");
                context.Request.Headers[Constants.HeaderRequestId] = requestId;
            }
            context.Response.Headers[Constants.HeaderRequestId] = requestId;
            AddCors(context);
            return Task.CompletedTask;
        }

        private async Task PostRoutingAsync(HttpContextBase context)
        {
            _InFlight.TryRemove(context.Request.Guid, out DateTime _);
            context.Timestamp.End = DateTime.UtcNow;

            IRequestObserver? observer = _Context.TryGet<IRequestObserver>();
            if (observer != null && !String.Equals(context.Request.Url.RawWithoutQuery, "/metrics", StringComparison.Ordinal))
            {
                try
                {
                    observer.Observe(context);
                }
                catch (Exception e)
                {
                    _Context.Logging.Debug(_Header + "request observer failed: " + e.Message);
                }
            }

            string route = RequestTelemetry.RouteTemplate(context);
            string? tenant = RequestTelemetry.Tenant(context);
            double seconds = context.Timestamp.TotalMs.HasValue ? context.Timestamp.TotalMs.Value / 1000.0 : 0;
            Activity? requestSpan = RequestTelemetry.TakeSpan(context);
            if (!String.Equals(route, "/metrics", StringComparison.Ordinal))
            {
                // Watson has ended the request span by now. A local child of it (never exported) carries its trace into
                // the measurements so they get it as their exemplar.
                Activity? exemplar = null;
                if (requestSpan != null && Activity.Current == null)
                {
                    exemplar = new Activity("hugo.http.exemplar");
                    exemplar.SetParentId(requestSpan.TraceId, requestSpan.SpanId, requestSpan.ActivityTraceFlags);
                    exemplar.Start();
                }
                try
                {
                    RequestTelemetry.Record(context, route, tenant, seconds);
                }
                finally
                {
                    exemplar?.Stop();
                }
            }

            if (_Context.Logging.Settings.MinimumSeverity <= Severity.Debug)
            {
                string? traceId = (requestSpan ?? Activity.Current)?.TraceId.ToHexString();
                _Context.Logging.Debug(_Header + context.Request.Method + " " + context.Request.Url.RawWithoutQuery + " " + context.Response.StatusCode + " (" + (context.Timestamp.TotalMs.HasValue ? context.Timestamp.TotalMs.Value.ToString("F2") : "?") + "ms) "
                    + HugoTracing.LogFields("route", route, "tenant", tenant, "status", context.Response.StatusCode.ToString(), "trace", traceId));
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }

        private void AddCors(HttpContextBase context)
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = _CorsOrigin;
            context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, PATCH, DELETE, OPTIONS, HEAD";
            context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization, Accept, Accept-Language, x-api-key, x-token, x-tenant-id, x-tenant-guid, x-email, x-password, x-access-key, x-secret-key, x-date, x-nonce, x-signature, x-request-id, x-hugo-profile, x-hugo-conversation, traceparent, tracestate, Mcp-Session-Id, MCP-Protocol-Version, Mcp-Method, Mcp-Name, Last-Event-ID";
            context.Response.Headers["Access-Control-Expose-Headers"] = "x-request-id, Retry-After, Mcp-Session-Id, MCP-Protocol-Version, WWW-Authenticate";
        }

        private static async Task DefaultRouteAsync(HttpContextBase context)
        {
            // The same error shape as every other error, including the request identifier sent in x-request-id.
            Hugo.Core.Responses.ErrorResponse error = new Hugo.Core.Responses.ErrorResponse("NotFound", "RouteNotFound", "No route matches the request.");
            error.RequestId = context.Metadata is RequestContext ctx ? ctx.RequestId : context.Request.Headers[Constants.HeaderRequestId];
            await RouteHelpers.JsonAsync(context, 404, error).ConfigureAwait(false);
        }

        #endregion
    }
}
