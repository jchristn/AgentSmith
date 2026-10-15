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
    /// User routes, including effective permission inspection. Users may read their own record and permissions.
    /// </summary>
    public class UserRoutes
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
        public UserRoutes(ServerContext context, IdentityService identity)
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
            routes.Scoped(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/users", ResourceTypeEnum.User, OperationTypeEnum.Read, ListAsync, "List users", "Users", null, typeof(EnumerationResult<User>));
            routes.Scoped(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/users", ResourceTypeEnum.User, OperationTypeEnum.Create, CreateAsync, "Create a user", "Users", typeof(UserRequest), typeof(User), statuses: new[] { 201 });
            routes.Authenticated(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/users/{id}", ReadAsync, "Read a user (self or with User Read)", "Users", null, typeof(User));
            routes.Scoped(HttpMethod.PUT, "/v1.0/api/tenants/{tenantId}/users/{id}", ResourceTypeEnum.User, OperationTypeEnum.Update, UpdateAsync, "Update a user", "Users", typeof(UserRequest), typeof(User));
            routes.Scoped(HttpMethod.DELETE, "/v1.0/api/tenants/{tenantId}/users/{id}", ResourceTypeEnum.User, OperationTypeEnum.Delete, DeleteAsync, "Delete a user with credentials, sessions, and assignments", "Users");
            routes.Authenticated(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/users/{id}/permissions", PermissionsAsync, "Effective permissions of a user (self or with User Admin)", "Users", null, typeof(EffectivePermissionsResponse));
            routes.Authenticated(HttpMethod.GET, "/v1.0/tenants/{tenantId}/users/{id}/permissions", PermissionsAsync, "Effective permissions of a user (AUTHENTICATION.md path; same as /v1.0/api/tenants/{tenantId}/users/{id}/permissions)", "Users", null, typeof(EffectivePermissionsResponse));
            routes.Scoped(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/users/{id}/password", ResourceTypeEnum.User, OperationTypeEnum.Update, ResetPasswordAsync, "Set a user's password (an administrator reset; the user must change it at next sign-in unless mustChangePassword=false is passed in the query)", "Users", typeof(PasswordChangeRequest), typeof(User));
            routes.Authenticated(HttpMethod.GET, "/v1.0/api/users/me", MeAsync, "The signed-in user", "Users", null, typeof(User));
            routes.Authenticated(HttpMethod.PUT, "/v1.0/api/users/me", UpdateMeAsync, "Update the signed-in user's name, locale, and default agent profile", "Users", typeof(UserRequest), typeof(User));
            routes.Authenticated(HttpMethod.POST, "/v1.0/api/users/me/password", ChangeMyPasswordAsync, "Change the signed-in user's password (clears must-change-password)", "Users", typeof(PasswordChangeRequest), typeof(User));
        }

        #endregion

        #region Private-Methods

        private async Task ListAsync(HttpContextBase context)
        {
            EnumerationResult<User> result = await _Context.Database.Users.EnumerateAsync(RouteHelpers.Tenant(context), RouteHelpers.Enumeration(context), context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, result).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase context)
        {
            User user = await _Identity.CreateUserAsync(RouteHelpers.Context(context), RouteHelpers.Tenant(context), RouteHelpers.RequiredBody<UserRequest>(context), context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 201, user).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase context)
        {
            string tenantId = RouteHelpers.Tenant(context);
            string id = RouteHelpers.Param(context, "id")!;
            if (!IsSelf(context, tenantId, id) && !await _Routes!.RequireAsync(context, tenantId, ResourceTypeEnum.User, OperationTypeEnum.Read, id).ConfigureAwait(false)) return;
            User? user = await _Context.Database.Users.ReadAsync(tenantId, id, context.Token).ConfigureAwait(false);
            if (user == null)
            {
                await RouteHelpers.NotFoundAsync(context, "User").ConfigureAwait(false);
                return;
            }
            await RouteHelpers.JsonAsync(context, 200, user).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase context)
        {
            User user = await _Identity.UpdateUserAsync(RouteHelpers.Context(context), RouteHelpers.Tenant(context), RouteHelpers.Param(context, "id")!, RouteHelpers.RequiredBody<UserRequest>(context), context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, user).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase context)
        {
            await _Identity.DeleteUserAsync(RouteHelpers.Context(context), RouteHelpers.Tenant(context), RouteHelpers.Param(context, "id")!, context.Token).ConfigureAwait(false);
            await RouteHelpers.NoContentAsync(context).ConfigureAwait(false);
        }

        private async Task PermissionsAsync(HttpContextBase context)
        {
            string tenantId = RouteHelpers.Tenant(context);
            string id = RouteHelpers.Param(context, "id")!;
            if (!IsSelf(context, tenantId, id) && !await _Routes!.RequireAsync(context, tenantId, ResourceTypeEnum.User, OperationTypeEnum.Admin, id).ConfigureAwait(false)) return;
            EffectivePermissionsResponse response = await _Identity.EffectiveForUserAsync(tenantId, id, context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, response).ConfigureAwait(false);
        }

        private async Task ResetPasswordAsync(HttpContextBase context)
        {
            PasswordChangeRequest request = RouteHelpers.RequiredBody<PasswordChangeRequest>(context);
            bool? mustChange = RouteHelpers.QueryBool(context, "mustChangePassword");
            User user = await _Identity.ChangePasswordAsync(RouteHelpers.Context(context), RouteHelpers.Tenant(context), RouteHelpers.Param(context, "id")!, request, mustChange, context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, user).ConfigureAwait(false);
        }

        private async Task MeAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            if (ctx.PrincipalType != PrincipalTypeEnum.User || ctx.User == null)
            {
                await RouteHelpers.ErrorAsync(context, 400, "NotAUser", "The caller is not a user.").ConfigureAwait(false);
                return;
            }
            User? user = await _Context.Database.Users.ReadAsync(ctx.User.TenantId, ctx.User.Id, context.Token).ConfigureAwait(false);
            if (user == null)
            {
                await RouteHelpers.NotFoundAsync(context, "User").ConfigureAwait(false);
                return;
            }
            await RouteHelpers.JsonAsync(context, 200, user).ConfigureAwait(false);
        }

        private async Task UpdateMeAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            if (ctx.PrincipalType != PrincipalTypeEnum.User || ctx.User == null)
            {
                await RouteHelpers.ErrorAsync(context, 400, "NotAUser", "The caller is not a user.").ConfigureAwait(false);
                return;
            }
            UserRequest body = RouteHelpers.RequiredBody<UserRequest>(context);
            UserRequest request = new UserRequest();
            request.FirstName = body.FirstName;
            request.LastName = body.LastName;
            request.PreferredLocale = body.PreferredLocale;
            request.DefaultAgentProfileId = body.DefaultAgentProfileId;
            User user = await _Identity.UpdateUserAsync(ctx, ctx.User.TenantId, ctx.User.Id, request, context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, user).ConfigureAwait(false);
        }

        private async Task ChangeMyPasswordAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            if (ctx.PrincipalType != PrincipalTypeEnum.User || ctx.User == null)
            {
                await RouteHelpers.ErrorAsync(context, 400, "NotAUser", "Only users have passwords.").ConfigureAwait(false);
                return;
            }
            PasswordChangeRequest request = RouteHelpers.RequiredBody<PasswordChangeRequest>(context);
            User user = await _Identity.ChangePasswordAsync(ctx, ctx.User.TenantId, ctx.User.Id, request, null, context.Token).ConfigureAwait(false);
            _Context.Authentication.Cache.ClearLocal();
            await RouteHelpers.JsonAsync(context, 200, user).ConfigureAwait(false);
        }

        private static bool IsSelf(HttpContextBase context, string tenantId, string id)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            return ctx.PrincipalType == PrincipalTypeEnum.User && ctx.TenantId == tenantId && ctx.UserId == id;
        }

        #endregion
    }
}
