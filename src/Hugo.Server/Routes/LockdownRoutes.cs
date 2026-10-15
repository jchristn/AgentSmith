namespace Hugo.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using Hugo.Core.Enums;
    using Hugo.Core.Requests;
    using Hugo.Core.Responses;
    using Hugo.Server.Services;
    using WatsonWebserver.Core;

    /// <summary>
    /// Tenant lockdown routes (P3.23, plan section 11.6) on the caller's tenant: GET reads the state, POST turns lockdown
    /// on (every non-Read call is denied and pending approvals are cancelled), DELETE turns it off. Audited.
    /// </summary>
    public class LockdownRoutes
    {
        #region Private-Members

        private static readonly string _Path = "/v1.0/api/tenant/lockdown";
        private readonly LockdownService _Lockdown;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="lockdown">Lockdown service.</param>
        /// <exception cref="ArgumentNullException">Thrown when lockdown is null.</exception>
        public LockdownRoutes(LockdownService lockdown)
        {
            _Lockdown = lockdown ?? throw new ArgumentNullException(nameof(lockdown));
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
            routes.Scoped(HttpMethod.GET, _Path, ResourceTypeEnum.Tenant, OperationTypeEnum.Read, GetAsync, "Read the tenant's lockdown state", "Tenants", null, typeof(LockdownState));
            routes.Scoped(HttpMethod.POST, _Path, ResourceTypeEnum.Tenant, OperationTypeEnum.Update, OnAsync, "Turn tenant lockdown on: deny every non-Read call and cancel pending approvals", "Tenants", typeof(LockdownRequest), typeof(LockdownState));
            routes.Scoped(HttpMethod.DELETE, _Path, ResourceTypeEnum.Tenant, OperationTypeEnum.Update, OffAsync, "Turn tenant lockdown off", "Tenants", null, typeof(LockdownState));
        }

        #endregion

        #region Private-Methods

        private async Task GetAsync(HttpContextBase http)
        {
            await RouteHelpers.JsonAsync(http, 200, await _Lockdown.GetAsync(RouteHelpers.Tenant(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task OnAsync(HttpContextBase http)
        {
            LockdownRequest request = RouteHelpers.Body<LockdownRequest>(http) ?? new LockdownRequest();
            await RouteHelpers.JsonAsync(http, 200, await _Lockdown.SetAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), true, request.Reason, http.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task OffAsync(HttpContextBase http)
        {
            await RouteHelpers.JsonAsync(http, 200, await _Lockdown.SetAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), false, null, http.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
