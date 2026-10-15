namespace Hugo.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;
    using Hugo.Core.Responses;
    using WatsonWebserver.Core;

    /// <summary>
    /// Tenant-scoped audit routes.
    /// </summary>
    public class AuditRoutes
    {
        #region Private-Members

        private readonly ServerContext _Context;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Server context.</param>
        /// <exception cref="ArgumentNullException">Thrown when context is null.</exception>
        public AuditRoutes(ServerContext context)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="routes">Registrar.</param>
        public void Register(RouteRegistrar routes)
        {
            routes.Scoped(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/audit", ResourceTypeEnum.Audit, OperationTypeEnum.Read, ListAsync, "List audit records (eventType, principalId, result, fromUtc, toUtc, pageNumber, pageSize)", "Audit", null, typeof(AuditPage));
            routes.Scoped(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/audit/{id}", ResourceTypeEnum.Audit, OperationTypeEnum.Read, ReadAsync, "Read an audit record", "Audit", null, typeof(AuditRecord));
            routes.Scoped(HttpMethod.DELETE, "/v1.0/api/tenants/{tenantId}/audit/{id}", ResourceTypeEnum.Audit, OperationTypeEnum.Delete, DeleteAsync, "Delete an audit record", "Audit");
            routes.Scoped(HttpMethod.DELETE, "/v1.0/api/tenants/{tenantId}/audit", ResourceTypeEnum.Audit, OperationTypeEnum.Delete, DeleteManyAsync, "Delete audit records matching the filter", "Audit", null, typeof(DeleteCountResponse));
        }

        #endregion

        #region Private-Methods

        private static AuditFilter Filter(HttpContextBase context)
        {
            AuditFilter filter = new AuditFilter();
            filter.TenantId = RouteHelpers.Tenant(context);
            filter.EventType = RouteHelpers.QueryEnum<AuditEventTypeEnum>(context, "eventType");
            filter.PrincipalId = RouteHelpers.Query(context, "principalId");
            filter.Result = RouteHelpers.QueryEnum<Hugo.Core.Enums.AuthorizationResultEnum>(context, "result");
            filter.FromUtc = RouteHelpers.QueryDate(context, "fromUtc");
            filter.ToUtc = RouteHelpers.QueryDate(context, "toUtc");
            filter.PageNumber = RouteHelpers.QueryInt(context, "pageNumber") ?? 1;
            filter.PageSize = RouteHelpers.QueryInt(context, "pageSize") ?? 25;
            return filter;
        }

        private async Task ListAsync(HttpContextBase context)
        {
            await RouteHelpers.JsonAsync(context, 200, await _Context.Database.Audit.EnumerateAsync(Filter(context), context.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase context)
        {
            AuditRecord? record = await _Context.Database.Audit.ReadAsync(RouteHelpers.Tenant(context), RouteHelpers.Param(context, "id")!, context.Token).ConfigureAwait(false);
            if (record == null)
            {
                await RouteHelpers.NotFoundAsync(context, "Audit record").ConfigureAwait(false);
                return;
            }
            await RouteHelpers.JsonAsync(context, 200, record).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase context)
        {
            AuditRecord? record = await _Context.Database.Audit.ReadAsync(RouteHelpers.Tenant(context), RouteHelpers.Param(context, "id")!, context.Token).ConfigureAwait(false);
            if (record == null)
            {
                await RouteHelpers.NotFoundAsync(context, "Audit record").ConfigureAwait(false);
                return;
            }
            await _Context.Database.Audit.DeleteAsync(record.Id, context.Token).ConfigureAwait(false);
            await RouteHelpers.NoContentAsync(context).ConfigureAwait(false);
        }

        private async Task DeleteManyAsync(HttpContextBase context)
        {
            int deleted = await _Context.Database.Audit.DeleteManyAsync(Filter(context), context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, new DeleteCountResponse { DeletedCount = deleted }).ConfigureAwait(false);
        }

        #endregion
    }
}
