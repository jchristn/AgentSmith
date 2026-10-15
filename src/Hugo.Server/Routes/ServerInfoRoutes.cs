namespace Hugo.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using Hugo.Core;
    using Hugo.Core.Responses;
    using Hugo.Server.Services;
    using WatsonWebserver.Core;

    /// <summary>
    /// Server information route for the dashboard.
    /// </summary>
    public class ServerInfoRoutes
    {
        #region Private-Members

        private readonly ServerContext _Context;
        private readonly RequestHistoryCaptureService _Capture;
        private readonly DateTime _StartedUtc = DateTime.UtcNow;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Server context.</param>
        /// <param name="capture">Request history capture service.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public ServerInfoRoutes(ServerContext context, RequestHistoryCaptureService capture)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            _Capture = capture ?? throw new ArgumentNullException(nameof(capture));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="routes">Registrar.</param>
        public void Register(RouteRegistrar routes)
        {
            routes.Authenticated(HttpMethod.GET, "/v1.0/api/server-info", InfoAsync, "Server version, endpoints, and feature flags", "Server", null, typeof(ServerInfoResponse));
            routes.Authenticated(HttpMethod.GET, "/v1.0/api/info", InfoAsync, "Server version, endpoints, and feature flags (same as /v1.0/api/server-info)", "Server", null, typeof(ServerInfoResponse));
        }

        /// <summary>
        /// External REST base URL shown to people: Rest.ExternalBaseUrl, else the request's scheme and host, else the bind address.
        /// </summary>
        /// <param name="context">Server context.</param>
        /// <param name="http">HTTP context of the request being answered, or null.</param>
        /// <returns>URL without a trailing slash.</returns>
        public static string RestUrl(ServerContext context, HttpContextBase? http)
        {
            return ExternalUrls.RestBaseUrl(context, http);
        }

        /// <summary>
        /// Public MCP URL (including the path) derived from settings, or null when MCP is disabled.
        /// </summary>
        /// <param name="context">Server context.</param>
        /// <returns>URL or null.</returns>
        public static string? McpUrl(ServerContext context)
        {
            if (!context.Settings.Mcp.Enable) return null;
            return ExternalUrls.RestBaseUrl(context, null) + "/mcp";
        }

        #endregion

        #region Private-Methods

        private async Task InfoAsync(HttpContextBase context)
        {
            ServerInfoResponse info = new ServerInfoResponse();
            info.Product = Constants.ProductName;
            info.Version = Constants.ProductVersion;
            info.NodeId = _Context.NodeId;
            info.RestUrl = RestUrl(_Context, context);
            info.McpUrl = McpUrl(_Context);
            info.A2AUrl = _Context.Settings.A2A.Enable ? info.RestUrl + "/v1.0/a2a" : null;
            info.OpenApiUrl = info.RestUrl + "/openapi.json";
            info.DatabaseType = _Context.Settings.Database.Type.ToString();
            info.TelemetryEnabled = _Context.Telemetry.Active;
            info.RequestHistoryEnabled = _Capture.Enabled();
            info.RouteCount = _Context.Scopes.All().Count;
            info.RepositoryUrl = Constants.RepositoryUrl;
            info.StartedUtc = _StartedUtc;
            if (_Context.Settings.Mcp.Enable) info.Features.Add("mcp");
            if (_Context.Settings.A2A.Enable) info.Features.Add("a2a");
            if (_Context.Settings.OpenAI.Enable) info.Features.Add("openai");
            info.Features.Add("secrets");
            info.Features.Add("request-history");
            if (_Context.Telemetry.Active) info.Features.Add("telemetry");
            info.Observability = _Context.Get<ObservabilityService>().Info();
            await RouteHelpers.JsonAsync(context, 200, info).ConfigureAwait(false);
        }


        #endregion
    }
}
