namespace Hugo.Server.Routes
{
    using System;
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
    /// Credential routes. Secrets are returned only by create and rotate.
    /// </summary>
    public class CredentialRoutes
    {
        #region Private-Members

        private readonly ServerContext _Context;
        private readonly IdentityService _Identity;
        private RouteRegistrar? _Routes = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Server context.</param>
        /// <param name="identity">Identity service.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public CredentialRoutes(ServerContext context, IdentityService identity)
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
            _Routes = routes;
            routes.Scoped(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/credentials", ResourceTypeEnum.Credential, OperationTypeEnum.Read, ListAsync, "List credentials", "Credentials", null, typeof(EnumerationResult<Credential>));
            routes.Scoped(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/credentials", ResourceTypeEnum.Credential, OperationTypeEnum.Create, CreateAsync, "Create a credential (the secret is returned once)", "Credentials", typeof(CredentialRequest), typeof(CredentialCreateResponse), statuses: new[] { 201 });
            routes.Scoped(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/credentials/{id}", ResourceTypeEnum.Credential, OperationTypeEnum.Read, ReadAsync, "Read a credential", "Credentials", null, typeof(Credential));
            routes.Scoped(HttpMethod.PUT, "/v1.0/api/tenants/{tenantId}/credentials/{id}", ResourceTypeEnum.Credential, OperationTypeEnum.Update, UpdateAsync, "Update a credential", "Credentials", typeof(CredentialRequest), typeof(Credential));
            routes.Scoped(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/credentials/{id}/rotate", ResourceTypeEnum.Credential, OperationTypeEnum.Update, RotateAsync, "Rotate a credential secret (the new secret is returned once)", "Credentials", null, typeof(CredentialCreateResponse));
            routes.Scoped(HttpMethod.DELETE, "/v1.0/api/tenants/{tenantId}/credentials/{id}", ResourceTypeEnum.Credential, OperationTypeEnum.Delete, DeleteAsync, "Delete a credential", "Credentials");
            routes.Authenticated(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/credentials/{id}/permissions", PermissionsAsync, "Effective permissions of a credential (self or with Credential Admin)", "Credentials", null, typeof(EffectivePermissionsResponse));
            routes.Authenticated(HttpMethod.GET, "/v1.0/tenants/{tenantId}/credentials/{id}/permissions", PermissionsAsync, "Effective permissions of a credential (self or with Credential Admin) (AUTHENTICATION.md path)", "Credentials", null, typeof(EffectivePermissionsResponse));
        }

        #endregion

        #region Private-Methods

        private async Task ListAsync(HttpContextBase context)
        {
            EnumerationResult<Credential> result = await _Context.Database.Credentials.EnumerateAsync(RouteHelpers.Tenant(context), RouteHelpers.Enumeration(context), context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, result).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase context)
        {
            CredentialCreateResponse response = await _Identity.CreateCredentialAsync(RouteHelpers.Context(context), RouteHelpers.Tenant(context), RouteHelpers.RequiredBody<CredentialRequest>(context), context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 201, response).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase context)
        {
            Credential? credential = await _Context.Database.Credentials.ReadAsync(RouteHelpers.Tenant(context), RouteHelpers.Param(context, "id")!, context.Token).ConfigureAwait(false);
            if (credential == null)
            {
                await RouteHelpers.NotFoundAsync(context, "Credential").ConfigureAwait(false);
                return;
            }
            await RouteHelpers.JsonAsync(context, 200, credential).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase context)
        {
            Credential credential = await _Identity.UpdateCredentialAsync(RouteHelpers.Context(context), RouteHelpers.Tenant(context), RouteHelpers.Param(context, "id")!, RouteHelpers.RequiredBody<CredentialRequest>(context), context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, credential).ConfigureAwait(false);
        }

        private async Task RotateAsync(HttpContextBase context)
        {
            CredentialCreateResponse response = await _Identity.RotateCredentialAsync(RouteHelpers.Context(context), RouteHelpers.Tenant(context), RouteHelpers.Param(context, "id")!, context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, response).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase context)
        {
            await _Identity.DeleteCredentialAsync(RouteHelpers.Context(context), RouteHelpers.Tenant(context), RouteHelpers.Param(context, "id")!, context.Token).ConfigureAwait(false);
            await RouteHelpers.NoContentAsync(context).ConfigureAwait(false);
        }

        private async Task PermissionsAsync(HttpContextBase context)
        {
            string tenantId = RouteHelpers.Tenant(context);
            string id = RouteHelpers.Param(context, "id")!;
            RequestContext ctx = RouteHelpers.Context(context);
            bool self = ctx.PrincipalType == PrincipalTypeEnum.Credential && ctx.TenantId == tenantId && ctx.CredentialId == id;
            if (!self && !await _Routes!.RequireAsync(context, tenantId, ResourceTypeEnum.Credential, OperationTypeEnum.Admin, id).ConfigureAwait(false)) return;
            EffectivePermissionsResponse response = await _Identity.EffectiveForCredentialAsync(tenantId, id, context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, response).ConfigureAwait(false);
        }

        #endregion
    }
}
