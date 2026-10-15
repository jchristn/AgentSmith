namespace Hugo.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;
    using Hugo.Core.Responses;
    using Hugo.Core.Security;
    using Hugo.Server.Services;
    using WatsonWebserver.Core;

    /// <summary>
    /// Tenant routes. Listing returns every tenant to system administrators and only the caller's tenant otherwise;
    /// create and delete are platform-administrator operations.
    /// </summary>
    public class TenantRoutes
    {
        #region Private-Members

        private readonly ServerContext _Context;
        private readonly IdentityService _Identity;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Server context.</param>
        /// <param name="identity">Identity service.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public TenantRoutes(ServerContext context, IdentityService identity)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            _Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="routes">Registrar.</param>
        public void Register(RouteRegistrar routes)
        {
            routes.Authenticated(HttpMethod.GET, "/v1.0/api/tenants", ListAsync, "List tenants visible to the caller", "Tenants", null, typeof(EnumerationResult<Tenant>));
            routes.Scoped(HttpMethod.POST, "/v1.0/api/tenants", ResourceTypeEnum.Tenant, OperationTypeEnum.Create, CreateAsync, "Create a tenant", "Tenants", typeof(TenantRequest), typeof(Tenant));
            routes.Scoped(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}", ResourceTypeEnum.Tenant, OperationTypeEnum.Read, ReadAsync, "Read a tenant", "Tenants", null, typeof(Tenant), "tenantId");
            routes.Scoped(HttpMethod.PUT, "/v1.0/api/tenants/{tenantId}", ResourceTypeEnum.Tenant, OperationTypeEnum.Update, UpdateAsync, "Update a tenant", "Tenants", typeof(TenantRequest), typeof(Tenant), "tenantId");
            routes.Scoped(HttpMethod.DELETE, "/v1.0/api/tenants/{tenantId}", ResourceTypeEnum.Tenant, OperationTypeEnum.Delete, DeleteAsync, "Delete a tenant and everything it owns", "Tenants", null, null, "tenantId");
        }

        #endregion

        #region Private-Methods

        private async Task ListAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            EnumerationQuery query = RouteHelpers.Enumeration(context);
            if (ctx.IsAdmin)
            {
                await RouteHelpers.JsonAsync(context, 200, await _Context.Database.Tenants.EnumerateAsync(query, context.Token).ConfigureAwait(false)).ConfigureAwait(false);
                return;
            }
            List<Tenant> own = new List<Tenant>();
            if (!String.IsNullOrEmpty(ctx.TenantId))
            {
                Tenant? tenant = await _Context.Database.Tenants.ReadAsync(ctx.TenantId, context.Token).ConfigureAwait(false);
                if (tenant != null) own.Add(tenant);
            }
            await RouteHelpers.JsonAsync(context, 200, EnumerationResult<Tenant>.Build(query, own.Count, own)).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase context)
        {
            Tenant tenant = await _Identity.CreateTenantAsync(RouteHelpers.Context(context), RouteHelpers.RequiredBody<TenantRequest>(context), context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 201, tenant).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase context)
        {
            Tenant? tenant = await _Context.Database.Tenants.ReadAsync(RouteHelpers.Tenant(context), context.Token).ConfigureAwait(false);
            if (tenant == null)
            {
                await RouteHelpers.NotFoundAsync(context, "Tenant").ConfigureAwait(false);
                return;
            }
            await RouteHelpers.JsonAsync(context, 200, tenant).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase context)
        {
            Tenant tenant = await _Identity.UpdateTenantAsync(RouteHelpers.Context(context), RouteHelpers.Tenant(context), RouteHelpers.RequiredBody<TenantRequest>(context), context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, tenant).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase context)
        {
            await _Identity.DeleteTenantAsync(RouteHelpers.Context(context), RouteHelpers.Tenant(context), context.Token).ConfigureAwait(false);
            await RouteHelpers.NoContentAsync(context).ConfigureAwait(false);
        }

        #endregion
    }
}
