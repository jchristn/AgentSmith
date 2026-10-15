namespace Hugo.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Services;
    using WatsonWebserver.Core;

    /// <summary>
    /// Authentication session routes (P1.28): list a tenant's sessions and revoke one.
    /// </summary>
    public class SessionRoutes
    {
        #region Private-Members

        private static readonly string _Base = "/v1.0/api/tenants/{tenantId}/sessions";
        private readonly ServerContext _Context;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Server context.</param>
        /// <exception cref="ArgumentNullException">Thrown when context is null.</exception>
        public SessionRoutes(ServerContext context)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
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
            routes.Scoped(HttpMethod.GET, _Base, ResourceTypeEnum.Session, OperationTypeEnum.Read, ListAsync, "List authentication sessions in the tenant", "Sessions", null, typeof(EnumerationResult<AuthSession>));
            routes.Scoped(HttpMethod.GET, _Base + "/{id}", ResourceTypeEnum.Session, OperationTypeEnum.Read, ReadAsync, "Read an authentication session", "Sessions", null, typeof(AuthSession));
            routes.Scoped(HttpMethod.DELETE, _Base + "/{id}", ResourceTypeEnum.Session, OperationTypeEnum.Delete, RevokeAsync, "Revoke an authentication session", "Sessions");
        }

        #endregion

        #region Private-Methods

        private async Task ListAsync(HttpContextBase http)
        {
            EnumerationResult<AuthSession> result = await _Context.Database.Sessions.EnumerateAsync(RouteHelpers.Tenant(http), RouteHelpers.Enumeration(http), http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, result).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase http)
        {
            AuthSession session = await LoadAsync(http).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, session).ConfigureAwait(false);
        }

        private async Task RevokeAsync(HttpContextBase http)
        {
            AuthSession session = await LoadAsync(http).ConfigureAwait(false);
            await _Context.Authentication.RevokeSessionAsync(session.Id, session.TenantId, "Revoked by " + (RouteHelpers.Context(http).PrincipalName ?? "an administrator") + ".", http.Token).ConfigureAwait(false);
            await RouteHelpers.NoContentAsync(http).ConfigureAwait(false);
        }

        private async Task<AuthSession> LoadAsync(HttpContextBase http)
        {
            AuthSession? session = await _Context.Database.Sessions.ReadAsync(RouteHelpers.Tenant(http), RouteHelpers.Param(http, "id")!, http.Token).ConfigureAwait(false);
            return session ?? throw new NotFoundException("Session not found.");
        }

        #endregion
    }
}
