namespace Hugo.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Hugo.Core.Enums;
    using Hugo.Core.Responses;
    using Hugo.Server.Services;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Analytics routes (P6.18): overview, tokens, tools, and models, bucketed and gap-filled, tenant scoped. Query
    /// parameters: from and to (ISO 8601, UTC), interval (minute, hour, day).
    /// </summary>
    public class AnalyticsRoutes
    {
        #region Private-Members

        private static readonly string _Base = "/v1.0/api/analytics";
        private readonly AnalyticsService _Analytics;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="analytics">Analytics service.</param>
        /// <exception cref="ArgumentNullException">Thrown when analytics is null.</exception>
        public AnalyticsRoutes(AnalyticsService analytics)
        {
            _Analytics = analytics ?? throw new ArgumentNullException(nameof(analytics));
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
            foreach (string kind in new[] { "overview", "tokens", "tools", "models" })
            {
                string reportKind = kind;
                routes.Scoped(HttpMethod.GET, _Base + "/" + kind, ResourceTypeEnum.Analytics, OperationTypeEnum.Read, http => ReportAsync(http, reportKind), Summary(kind), "Analytics", null, typeof(AnalyticsReport), "none", QueryParameters());
            }
        }

        #endregion

        #region Private-Methods

        private async Task ReportAsync(HttpContextBase http, string kind)
        {
            AnalyticsReport report = await _Analytics.BuildAsync(kind, RouteHelpers.Tenant(http), RouteHelpers.QueryDate(http, "from"), RouteHelpers.QueryDate(http, "to"), RouteHelpers.Query(http, "interval"), http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, report).ConfigureAwait(false);
        }

        private static string Summary(string kind)
        {
            switch (kind)
            {
                case "tokens": return "Token usage per bucket (input and output) with window totals";
                case "tools": return "Tool calls, errors, and approvals per bucket, with per-tool rows";
                case "models": return "Turns, tokens, and latency per model endpoint, with per-bucket totals";
                default: return "Conversations, turns, tokens, tool calls, approvals, and latency per bucket";
            }
        }

        private static List<OpenApiParameterMetadata> QueryParameters()
        {
            return new List<OpenApiParameterMetadata>
            {
                OpenApiParameterMetadata.Query("from", "Window start (ISO 8601 UTC). Default: seven days before to.", false, OpenApiSchemaMetadata.String()),
                OpenApiParameterMetadata.Query("to", "Window end (ISO 8601 UTC). Default: now.", false, OpenApiSchemaMetadata.String()),
                OpenApiParameterMetadata.Query("interval", "Bucket width: minute, hour, or day. Default chosen by window length.", false, OpenApiSchemaMetadata.String())
            };
        }

        #endregion
    }
}
