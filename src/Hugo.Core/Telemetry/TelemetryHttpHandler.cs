namespace Hugo.Core.Telemetry
{
    using System;
    using System.Diagnostics;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// HTTP message handler that records every outbound request (P13.24, P13.25): a client span named
    /// "&lt;service&gt; &lt;operation&gt;" and hugo.outbound.requests and hugo.outbound.duration by service,
    /// operation, and result (status class, timeout, error). The operation comes from
    /// <see cref="OperationKey"/> on the request, else the HTTP method, and <see cref="ServiceKey"/> can override the
    /// service for one request. W3C trace context is propagated by the .NET
    /// HTTP stack. Thread-safe.
    /// </summary>
    public class TelemetryHttpHandler : DelegatingHandler
    {
        #region Public-Members

        /// <summary>
        /// Request option naming the operation, for example lease or heartbeat.
        /// </summary>
        public static readonly HttpRequestOptionsKey<string> OperationKey = new HttpRequestOptionsKey<string>("hugo.operation");

        /// <summary>
        /// Request option overriding <see cref="Service"/> for one request, for example grafana on a client that probes
        /// several services.
        /// </summary>
        public static readonly HttpRequestOptionsKey<string> ServiceKey = new HttpRequestOptionsKey<string>("hugo.service");

        /// <summary>
        /// Downstream service label, for example control_plane, grafana, kubernetes.
        /// </summary>
        public string Service { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="service">Downstream service label.</param>
        /// <param name="inner">Inner handler.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null or empty.</exception>
        public TelemetryHttpHandler(string service, HttpMessageHandler inner) : base(inner ?? throw new ArgumentNullException(nameof(inner)))
        {
            if (String.IsNullOrEmpty(service)) throw new ArgumentNullException(nameof(service));
            Service = service;
        }

        /// <summary>
        /// Instantiate without an inner handler, for clients that chain handlers themselves (Kubernetes).
        /// </summary>
        /// <param name="service">Downstream service label.</param>
        /// <exception cref="ArgumentNullException">Thrown when service is null or empty.</exception>
        public TelemetryHttpHandler(string service)
        {
            if (String.IsNullOrEmpty(service)) throw new ArgumentNullException(nameof(service));
            Service = service;
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Send the request and record it.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Response.</returns>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string operation = request.Options.TryGetValue(OperationKey, out string? named) && !String.IsNullOrEmpty(named) ? named : request.Method.Method.ToLowerInvariant();
            string service = request.Options.TryGetValue(ServiceKey, out string? overridden) && !String.IsNullOrEmpty(overridden) ? overridden : Service;
            long start = Stopwatch.GetTimestamp();
            Activity? activity = HugoTracing.StartOutbound(service, operation);
            if (activity != null)
            {
                activity.SetTag("http.request.method", request.Method.Method);
                if (request.RequestUri != null)
                {
                    activity.SetTag("server.address", request.RequestUri.Host);
                    activity.SetTag("server.port", request.RequestUri.Port);
                }
            }
            try
            {
                HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                activity?.SetTag("http.response.status_code", (int)response.StatusCode);
                HugoTracing.EndOutbound(activity, service, operation, start, HugoMetrics.StatusClass((int)response.StatusCode));
                return response;
            }
            catch (OperationCanceledException e)
            {
                HugoTracing.EndOutbound(activity, service, operation, start, cancellationToken.IsCancellationRequested ? "cancelled" : "timeout", e);
                throw;
            }
            catch (Exception e)
            {
                HugoTracing.EndOutbound(activity, service, operation, start, "error", e);
                throw;
            }
        }

        #endregion
    }
}
