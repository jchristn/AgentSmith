namespace Hugo.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;
    using Hugo.Core.Responses;
    using Hugo.Core.Security;
    using Hugo.Server.Services;
    using WatsonWebserver.Core;

    /// <summary>
    /// Request history routes at /v1.0/api/request-history. Scoped to the caller's tenant; system administrators see
    /// every tenant unless they pass ?tenantId=. When capture is disabled the routes return empty results.
    /// </summary>
    public class RequestHistoryRoutes
    {
        #region Private-Members

        private readonly ServerContext _Context;
        private readonly RequestHistoryCaptureService _Capture;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Server context.</param>
        /// <param name="capture">Capture service.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public RequestHistoryRoutes(ServerContext context, RequestHistoryCaptureService capture)
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
            routes.Scoped(HttpMethod.GET, "/v1.0/api/request-history", ResourceTypeEnum.RequestHistory, OperationTypeEnum.Read, ListAsync, "List captured requests (method, statusCode, pathContains, fromUtc, toUtc, pageNumber, pageSize, tenantId, userId)", "Request History", null, typeof(RequestHistoryPage));
            routes.Scoped(HttpMethod.GET, "/v1.0/api/request-history/summary", ResourceTypeEnum.RequestHistory, OperationTypeEnum.Read, SummaryAsync, "Time-bucketed request counts (fromUtc, toUtc, bucketMinutes)", "Request History", null, typeof(RequestHistorySummary));
            routes.Scoped(HttpMethod.GET, "/v1.0/api/request-history/{id}", ResourceTypeEnum.RequestHistory, OperationTypeEnum.Read, ReadAsync, "Read a captured request with headers and bodies", "Request History", null, typeof(RequestHistoryEntry));
            routes.Scoped(HttpMethod.DELETE, "/v1.0/api/request-history/{id}", ResourceTypeEnum.RequestHistory, OperationTypeEnum.Delete, DeleteAsync, "Delete a captured request", "Request History");
            routes.Scoped(HttpMethod.DELETE, "/v1.0/api/request-history", ResourceTypeEnum.RequestHistory, OperationTypeEnum.Delete, DeleteManyAsync, "Delete captured requests matching the filter", "Request History", null, typeof(DeleteCountResponse));
        }

        #endregion

        #region Private-Methods

        private static RequestHistoryFilter Filter(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            RequestHistoryFilter filter = new RequestHistoryFilter();
            if (ctx.IsAdmin)
            {
                filter.TenantId = RouteHelpers.Query(context, "tenantId");
                filter.UserId = RouteHelpers.Query(context, "userId");
            }
            else
            {
                filter.TenantId = ctx.TenantId ?? "none";
                filter.UserId = RouteHelpers.Query(context, "userId");
            }
            filter.Method = RouteHelpers.Query(context, "method");
            filter.StatusCode = RouteHelpers.QueryInt(context, "statusCode");
            filter.PathContains = RouteHelpers.Query(context, "pathContains");
            filter.FromUtc = RouteHelpers.QueryDate(context, "fromUtc");
            filter.ToUtc = RouteHelpers.QueryDate(context, "toUtc");
            filter.PageNumber = RouteHelpers.QueryInt(context, "pageNumber") ?? 1;
            filter.PageSize = RouteHelpers.QueryInt(context, "pageSize") ?? 25;
            filter.BucketMinutes = RouteHelpers.QueryInt(context, "bucketMinutes") ?? 15;
            return filter;
        }

        private async Task ListAsync(HttpContextBase context)
        {
            RequestHistoryFilter filter = Filter(context);
            if (!_Capture.Enabled())
            {
                await RouteHelpers.JsonAsync(context, 200, new RequestHistoryPage { PageNumber = filter.PageNumber, PageSize = filter.PageSize }).ConfigureAwait(false);
                return;
            }
            await RouteHelpers.JsonAsync(context, 200, await _Context.Database.RequestHistory.EnumerateAsync(filter, context.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task SummaryAsync(HttpContextBase context)
        {
            RequestHistoryFilter filter = Filter(context);
            RequestHistorySummary summary = await _Context.Database.RequestHistory.SummarizeAsync(filter, context.Token).ConfigureAwait(false);
            if (!_Capture.Enabled())
            {
                foreach (RequestHistoryBucket bucket in summary.Buckets)
                {
                    bucket.SuccessCount = 0;
                    bucket.FailureCount = 0;
                    bucket.AverageDurationMs = 0;
                }
                summary.TotalCount = 0;
                summary.TotalSuccess = 0;
                summary.TotalFailure = 0;
                summary.AverageDurationMs = 0;
            }
            await RouteHelpers.JsonAsync(context, 200, summary).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase context)
        {
            RequestHistoryEntry? entry = await VisibleAsync(context).ConfigureAwait(false);
            if (entry == null)
            {
                await RouteHelpers.NotFoundAsync(context, "Request").ConfigureAwait(false);
                return;
            }
            await RouteHelpers.JsonAsync(context, 200, entry).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase context)
        {
            RequestHistoryEntry? entry = await VisibleAsync(context).ConfigureAwait(false);
            if (entry == null)
            {
                await RouteHelpers.NotFoundAsync(context, "Request").ConfigureAwait(false);
                return;
            }
            await _Context.Database.RequestHistory.DeleteAsync(entry.Id, context.Token).ConfigureAwait(false);
            await RouteHelpers.NoContentAsync(context).ConfigureAwait(false);
        }

        private async Task DeleteManyAsync(HttpContextBase context)
        {
            int deleted = await _Context.Database.RequestHistory.DeleteManyAsync(Filter(context), context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, new DeleteCountResponse { DeletedCount = deleted }).ConfigureAwait(false);
        }

        private async Task<RequestHistoryEntry?> VisibleAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            RequestHistoryEntry? entry = await _Context.Database.RequestHistory.ReadAsync(RouteHelpers.Param(context, "id")!, context.Token).ConfigureAwait(false);
            if (entry == null) return null;
            if (!ctx.IsAdmin && entry.TenantId != ctx.TenantId) return null;
            return entry;
        }

        #endregion
    }
}
