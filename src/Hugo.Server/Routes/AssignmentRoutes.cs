namespace Hugo.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;
    using Hugo.Core.Services;
    using Hugo.Server.Services;
    using WatsonWebserver.Core;

    /// <summary>
    /// User role assignment and credential scope assignment routes.
    /// </summary>
    public class AssignmentRoutes
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
        public AssignmentRoutes(ServerContext context, IdentityService identity)
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
            routes.Scoped(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/user-role-assignments", ResourceTypeEnum.Assignment, OperationTypeEnum.Read, ListUserAsync, "List user role assignments (filter with userId)", "Assignments", null, typeof(EnumerationResult<UserRoleAssignment>));
            routes.Scoped(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/user-role-assignments", ResourceTypeEnum.Assignment, OperationTypeEnum.Create, CreateUserAsync, "Assign a role to a user", "Assignments", typeof(AssignmentRequest), typeof(UserRoleAssignment));
            routes.Scoped(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/user-role-assignments/{id}", ResourceTypeEnum.Assignment, OperationTypeEnum.Read, ReadUserAsync, "Read a user role assignment", "Assignments", null, typeof(UserRoleAssignment));
            routes.Scoped(HttpMethod.PUT, "/v1.0/api/tenants/{tenantId}/user-role-assignments/{id}", ResourceTypeEnum.Assignment, OperationTypeEnum.Update, UpdateUserAsync, "Update a user role assignment", "Assignments", typeof(AssignmentRequest), typeof(UserRoleAssignment));
            routes.Scoped(HttpMethod.DELETE, "/v1.0/api/tenants/{tenantId}/user-role-assignments/{id}", ResourceTypeEnum.Assignment, OperationTypeEnum.Delete, DeleteUserAsync, "Remove a user role assignment", "Assignments");
            routes.Scoped(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/credential-scope-assignments", ResourceTypeEnum.Assignment, OperationTypeEnum.Read, ListCredentialAsync, "List credential scope assignments (filter with credentialId)", "Assignments", null, typeof(EnumerationResult<CredentialScopeAssignment>));
            routes.Scoped(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/credential-scope-assignments", ResourceTypeEnum.Assignment, OperationTypeEnum.Create, CreateCredentialAsync, "Assign a role to a credential", "Assignments", typeof(AssignmentRequest), typeof(CredentialScopeAssignment));
            routes.Scoped(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/credential-scope-assignments/{id}", ResourceTypeEnum.Assignment, OperationTypeEnum.Read, ReadCredentialAsync, "Read a credential scope assignment", "Assignments", null, typeof(CredentialScopeAssignment));
            routes.Scoped(HttpMethod.PUT, "/v1.0/api/tenants/{tenantId}/credential-scope-assignments/{id}", ResourceTypeEnum.Assignment, OperationTypeEnum.Update, UpdateCredentialAsync, "Update a credential scope assignment", "Assignments", typeof(AssignmentRequest), typeof(CredentialScopeAssignment));
            routes.Scoped(HttpMethod.DELETE, "/v1.0/api/tenants/{tenantId}/credential-scope-assignments/{id}", ResourceTypeEnum.Assignment, OperationTypeEnum.Delete, DeleteCredentialAsync, "Remove a credential scope assignment", "Assignments");
        }

        #endregion

        #region Private-Methods

        private async Task ListUserAsync(HttpContextBase context)
        {
            string tenantId = RouteHelpers.Tenant(context);
            string? userId = RouteHelpers.Query(context, "userId");
            EnumerationQuery query = RouteHelpers.Enumeration(context);
            if (!String.IsNullOrEmpty(userId))
            {
                List<UserRoleAssignment> rows = await _Context.Database.UserRoleAssignments.ListByUserAsync(tenantId, userId, context.Token).ConfigureAwait(false);
                await RouteHelpers.JsonAsync(context, 200, EnumerationResult<UserRoleAssignment>.Build(query, rows.Count, rows.Skip(query.Skip).Take(query.MaxResults).ToList())).ConfigureAwait(false);
                return;
            }
            await RouteHelpers.JsonAsync(context, 200, await _Context.Database.UserRoleAssignments.EnumerateAsync(tenantId, query, context.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task CreateUserAsync(HttpContextBase context)
        {
            string tenantId = RouteHelpers.Tenant(context);
            AssignmentRequest request = RouteHelpers.RequiredBody<AssignmentRequest>(context);
            if (String.IsNullOrEmpty(request.UserId) || await _Context.Database.Users.ReadAsync(tenantId, request.UserId, context.Token).ConfigureAwait(false) == null) throw Invalid("userId", "The user does not exist in this tenant.");
            UserRoleAssignment assignment = new UserRoleAssignment();
            assignment.TenantId = tenantId;
            assignment.UserId = request.UserId;
            await ApplyAsync(tenantId, request, context, (role, scope, resourceId, inherits, active) =>
            {
                assignment.RoleId = role.Id;
                assignment.RoleName = role.Name;
                assignment.ResourceScope = scope;
                assignment.ResourceId = resourceId;
                assignment.InheritsToChildren = inherits;
                assignment.Active = active;
            }).ConfigureAwait(false);
            await _Context.Database.UserRoleAssignments.CreateAsync(assignment, context.Token).ConfigureAwait(false);
            await ChangedAsync(context, assignment.Id, "Role " + assignment.RoleName + " assigned to user " + assignment.UserId + ".").ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 201, assignment).ConfigureAwait(false);
        }

        private async Task ReadUserAsync(HttpContextBase context)
        {
            UserRoleAssignment? assignment = await _Context.Database.UserRoleAssignments.ReadAsync(RouteHelpers.Tenant(context), RouteHelpers.Param(context, "id")!, context.Token).ConfigureAwait(false);
            if (assignment == null)
            {
                await RouteHelpers.NotFoundAsync(context, "Assignment").ConfigureAwait(false);
                return;
            }
            await RouteHelpers.JsonAsync(context, 200, assignment).ConfigureAwait(false);
        }

        private async Task UpdateUserAsync(HttpContextBase context)
        {
            string tenantId = RouteHelpers.Tenant(context);
            UserRoleAssignment assignment = await _Context.Database.UserRoleAssignments.ReadAsync(tenantId, RouteHelpers.Param(context, "id")!, context.Token).ConfigureAwait(false) ?? throw new NotFoundException("Assignment not found.");
            if (assignment.IsProtected) throw new ConflictException("Protected", "Protected assignments cannot be modified.");
            AssignmentRequest request = RouteHelpers.RequiredBody<AssignmentRequest>(context);
            if (String.IsNullOrEmpty(request.RoleId) && String.IsNullOrEmpty(request.RoleName)) request.RoleId = assignment.RoleId;
            if (!request.ResourceScope.HasValue) request.ResourceScope = assignment.ResourceScope;
            if (request.ResourceId == null) request.ResourceId = assignment.ResourceId;
            if (!request.InheritsToChildren.HasValue) request.InheritsToChildren = assignment.InheritsToChildren;
            if (!request.Active.HasValue) request.Active = assignment.Active;
            await ApplyAsync(tenantId, request, context, (role, scope, resourceId, inherits, active) =>
            {
                assignment.RoleId = role.Id;
                assignment.RoleName = role.Name;
                assignment.ResourceScope = scope;
                assignment.ResourceId = resourceId;
                assignment.InheritsToChildren = inherits;
                assignment.Active = active;
            }).ConfigureAwait(false);
            await _Context.Database.UserRoleAssignments.UpdateAsync(assignment, context.Token).ConfigureAwait(false);
            await ChangedAsync(context, assignment.Id, "User role assignment updated.").ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, assignment).ConfigureAwait(false);
        }

        private async Task DeleteUserAsync(HttpContextBase context)
        {
            UserRoleAssignment assignment = await _Context.Database.UserRoleAssignments.ReadAsync(RouteHelpers.Tenant(context), RouteHelpers.Param(context, "id")!, context.Token).ConfigureAwait(false) ?? throw new NotFoundException("Assignment not found.");
            if (assignment.IsProtected) throw new ConflictException("Protected", "Protected assignments cannot be removed.");
            await _Context.Database.UserRoleAssignments.DeleteAsync(assignment.Id, context.Token).ConfigureAwait(false);
            await ChangedAsync(context, assignment.Id, "User role assignment removed.").ConfigureAwait(false);
            await RouteHelpers.NoContentAsync(context).ConfigureAwait(false);
        }

        private async Task ListCredentialAsync(HttpContextBase context)
        {
            string tenantId = RouteHelpers.Tenant(context);
            string? credentialId = RouteHelpers.Query(context, "credentialId");
            EnumerationQuery query = RouteHelpers.Enumeration(context);
            if (!String.IsNullOrEmpty(credentialId))
            {
                List<CredentialScopeAssignment> rows = await _Context.Database.CredentialScopeAssignments.ListByCredentialAsync(tenantId, credentialId, context.Token).ConfigureAwait(false);
                await RouteHelpers.JsonAsync(context, 200, EnumerationResult<CredentialScopeAssignment>.Build(query, rows.Count, rows.Skip(query.Skip).Take(query.MaxResults).ToList())).ConfigureAwait(false);
                return;
            }
            await RouteHelpers.JsonAsync(context, 200, await _Context.Database.CredentialScopeAssignments.EnumerateAsync(tenantId, query, context.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task CreateCredentialAsync(HttpContextBase context)
        {
            string tenantId = RouteHelpers.Tenant(context);
            AssignmentRequest request = RouteHelpers.RequiredBody<AssignmentRequest>(context);
            if (String.IsNullOrEmpty(request.CredentialId) || await _Context.Database.Credentials.ReadAsync(tenantId, request.CredentialId, context.Token).ConfigureAwait(false) == null) throw Invalid("credentialId", "The credential does not exist in this tenant.");
            CredentialScopeAssignment assignment = new CredentialScopeAssignment();
            assignment.TenantId = tenantId;
            assignment.CredentialId = request.CredentialId;
            await ApplyAsync(tenantId, request, context, (role, scope, resourceId, inherits, active) =>
            {
                assignment.RoleId = role.Id;
                assignment.RoleName = role.Name;
                assignment.ResourceScope = scope;
                assignment.ResourceId = resourceId;
                assignment.InheritsToChildren = inherits;
                assignment.Active = active;
            }).ConfigureAwait(false);
            await _Context.Database.CredentialScopeAssignments.CreateAsync(assignment, context.Token).ConfigureAwait(false);
            await ChangedAsync(context, assignment.Id, "Role " + assignment.RoleName + " assigned to credential " + assignment.CredentialId + ".").ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 201, assignment).ConfigureAwait(false);
        }

        private async Task ReadCredentialAsync(HttpContextBase context)
        {
            CredentialScopeAssignment? assignment = await _Context.Database.CredentialScopeAssignments.ReadAsync(RouteHelpers.Tenant(context), RouteHelpers.Param(context, "id")!, context.Token).ConfigureAwait(false);
            if (assignment == null)
            {
                await RouteHelpers.NotFoundAsync(context, "Assignment").ConfigureAwait(false);
                return;
            }
            await RouteHelpers.JsonAsync(context, 200, assignment).ConfigureAwait(false);
        }

        private async Task UpdateCredentialAsync(HttpContextBase context)
        {
            string tenantId = RouteHelpers.Tenant(context);
            CredentialScopeAssignment assignment = await _Context.Database.CredentialScopeAssignments.ReadAsync(tenantId, RouteHelpers.Param(context, "id")!, context.Token).ConfigureAwait(false) ?? throw new NotFoundException("Assignment not found.");
            if (assignment.IsProtected) throw new ConflictException("Protected", "Protected assignments cannot be modified.");
            AssignmentRequest request = RouteHelpers.RequiredBody<AssignmentRequest>(context);
            if (String.IsNullOrEmpty(request.RoleId) && String.IsNullOrEmpty(request.RoleName)) request.RoleId = assignment.RoleId;
            if (!request.ResourceScope.HasValue) request.ResourceScope = assignment.ResourceScope;
            if (request.ResourceId == null) request.ResourceId = assignment.ResourceId;
            if (!request.InheritsToChildren.HasValue) request.InheritsToChildren = assignment.InheritsToChildren;
            if (!request.Active.HasValue) request.Active = assignment.Active;
            await ApplyAsync(tenantId, request, context, (role, scope, resourceId, inherits, active) =>
            {
                assignment.RoleId = role.Id;
                assignment.RoleName = role.Name;
                assignment.ResourceScope = scope;
                assignment.ResourceId = resourceId;
                assignment.InheritsToChildren = inherits;
                assignment.Active = active;
            }).ConfigureAwait(false);
            await _Context.Database.CredentialScopeAssignments.UpdateAsync(assignment, context.Token).ConfigureAwait(false);
            await ChangedAsync(context, assignment.Id, "Credential scope assignment updated.").ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, assignment).ConfigureAwait(false);
        }

        private async Task DeleteCredentialAsync(HttpContextBase context)
        {
            CredentialScopeAssignment assignment = await _Context.Database.CredentialScopeAssignments.ReadAsync(RouteHelpers.Tenant(context), RouteHelpers.Param(context, "id")!, context.Token).ConfigureAwait(false) ?? throw new NotFoundException("Assignment not found.");
            if (assignment.IsProtected) throw new ConflictException("Protected", "Protected assignments cannot be removed.");
            await _Context.Database.CredentialScopeAssignments.DeleteAsync(assignment.Id, context.Token).ConfigureAwait(false);
            await ChangedAsync(context, assignment.Id, "Credential scope assignment removed.").ConfigureAwait(false);
            await RouteHelpers.NoContentAsync(context).ConfigureAwait(false);
        }

        private async Task ApplyAsync(string tenantId, AssignmentRequest request, HttpContextBase context, Action<Role, ResourceScopeEnum, string?, bool, bool> apply)
        {
            Role role = await _Identity.ResolveRoleAsync(tenantId, request.RoleId, request.RoleName, context.Token).ConfigureAwait(false);
            ResourceScopeEnum scope = request.ResourceScope ?? ResourceScopeEnum.Tenant;
            if (scope == ResourceScopeEnum.Resource && String.IsNullOrEmpty(request.ResourceId)) throw Invalid("resourceId", "A resource identifier is required for Resource scope.");
            apply(role, scope, scope == ResourceScopeEnum.Resource ? request.ResourceId : null, request.InheritsToChildren ?? true, request.Active ?? true);
        }

        private async Task ChangedAsync(HttpContextBase context, string id, string description)
        {
            _Context.Authorization.Invalidate();
            await _Context.Audit.WriteAsync(RouteHelpers.Context(context), AuditEventTypeEnum.AssignmentChanged, "Assignment", id, description, RouteHelpers.Tenant(context), context.Token).ConfigureAwait(false);
        }

        private static ValidationException Invalid(string field, string message)
        {
            return new ValidationException(message, new Dictionary<string, string> { { field, message } });
        }

        #endregion
    }
}
