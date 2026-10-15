namespace Hugo.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Hugo.Core.Responses;
    using Hugo.Server.Services;
    using WatsonWebserver.Core;

    /// <summary>
    /// Observability status route (Phase 13): reachability of Grafana, Prometheus, Loki, and Tempo as probed by the server.
    /// </summary>
    public class ObservabilityRoutes
    {
        #region Private-Members

        private readonly ObservabilityService _Observability;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="observability">Observability service.</param>
        /// <exception cref="ArgumentNullException">Thrown when observability is null.</exception>
        public ObservabilityRoutes(ObservabilityService observability)
        {
            _Observability = observability ?? throw new ArgumentNullException(nameof(observability));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="routes">Registrar.</param>
        public void Register(RouteRegistrar routes)
        {
            routes.Authenticated(HttpMethod.GET, "/v1.0/api/observability/status", StatusAsync, "Reachability and version of the configured observability services (cached for 30 seconds)", "Server", null, typeof(List<ObservabilityServiceStatus>));
        }

        #endregion

        #region Private-Methods

        private async Task StatusAsync(HttpContextBase context)
        {
            List<ObservabilityServiceStatus> status = await _Observability.StatusAsync(context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, status).ConfigureAwait(false);
        }

        #endregion
    }
}
