namespace Hugo.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;
    using Hugo.Core.Services;
    using Hugo.Server.Services;
    using WatsonWebserver.Core;

    /// <summary>
    /// Permission routes. Built-in permissions are listed read-only.
    /// </summary>
    public class PermissionRoutes
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
        public PermissionRoutes(ServerContext context)
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
            routes.Scoped(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/permissions", ResourceTypeEnum.Permission, OperationTypeEnum.Read, ListAsync, "List permissions, including built-ins", "Permissions", null, typeof(EnumerationResult<Permission>));
            routes.Scoped(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/permissions", ResourceTypeEnum.Permission, OperationTypeEnum.Create, CreateAsync, "Create a permission", "Permissions", typeof(PermissionRequest), typeof(Permission));
            routes.Scoped(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/permissions/{id}", ResourceTypeEnum.Permission, OperationTypeEnum.Read, ReadAsync, "Read a permission", "Permissions", null, typeof(Permission));
            routes.Scoped(HttpMethod.PUT, "/v1.0/api/tenants/{tenantId}/permissions/{id}", ResourceTypeEnum.Permission, OperationTypeEnum.Update, UpdateAsync, "Update a permission", "Permissions", typeof(PermissionRequest), typeof(Permission));
            routes.Scoped(HttpMethod.DELETE, "/v1.0/api/tenants/{tenantId}/permissions/{id}", ResourceTypeEnum.Permission, OperationTypeEnum.Delete, DeleteAsync, "Delete a permission", "Permissions");
        }

        #endregion

        #region Private-Methods

        private async Task ListAsync(HttpContextBase context)
        {
            EnumerationResult<Permission> result = await _Context.Database.Permissions.EnumerateAsync(RouteHelpers.Tenant(context), RouteHelpers.Enumeration(context), context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, result).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase context)
        {
            string tenantId = RouteHelpers.Tenant(context);
            PermissionRequest request = RouteHelpers.RequiredBody<PermissionRequest>(context);
            if (String.IsNullOrWhiteSpace(request.Name)) throw new ValidationException("A permission name is required.", new Dictionary<string, string> { { "name", "A permission name is required." } });
            IdentityService.ValidatePermissionLists(request.ResourceTypes, request.OperationTypes);
            if (await _Context.Database.Permissions.ReadByNameAsync(tenantId, request.Name.Trim(), context.Token).ConfigureAwait(false) != null) throw new ConflictException("Duplicate", "A permission named '" + request.Name + "' already exists.");

            Permission permission = new Permission();
            permission.TenantId = tenantId;
            permission.Name = request.Name.Trim();
            permission.PermissionType = request.PermissionType ?? PermissionTypeEnum.Permit;
            permission.ResourceTypes = request.ResourceTypes!;
            permission.OperationTypes = request.OperationTypes!;
            permission.Active = request.Active ?? true;
            await _Context.Database.Permissions.CreateAsync(permission, context.Token).ConfigureAwait(false);
            await ChangedAsync(context, permission.Id, "Permission " + permission.Name + " created.").ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 201, permission).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase context)
        {
            Permission? permission = await _Context.Database.Permissions.ReadAsync(RouteHelpers.Tenant(context), RouteHelpers.Param(context, "id")!, context.Token).ConfigureAwait(false);
            if (permission == null)
            {
                await RouteHelpers.NotFoundAsync(context, "Permission").ConfigureAwait(false);
                return;
            }
            await RouteHelpers.JsonAsync(context, 200, permission).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase context)
        {
            Permission permission = await EditableAsync(context).ConfigureAwait(false);
            PermissionRequest request = RouteHelpers.RequiredBody<PermissionRequest>(context);
            if (!String.IsNullOrWhiteSpace(request.Name)) permission.Name = request.Name.Trim();
            if (request.PermissionType.HasValue) permission.PermissionType = request.PermissionType.Value;
            if (request.ResourceTypes != null || request.OperationTypes != null)
            {
                List<string> resources = request.ResourceTypes ?? permission.ResourceTypes;
                List<string> operations = request.OperationTypes ?? permission.OperationTypes;
                IdentityService.ValidatePermissionLists(resources, operations);
                permission.ResourceTypes = resources;
                permission.OperationTypes = operations;
            }
            if (request.Active.HasValue) permission.Active = request.Active.Value;
            await _Context.Database.Permissions.UpdateAsync(permission, context.Token).ConfigureAwait(false);
            await ChangedAsync(context, permission.Id, "Permission " + permission.Name + " updated.").ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, permission).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase context)
        {
            Permission permission = await EditableAsync(context).ConfigureAwait(false);
            await _Context.Database.Permissions.DeleteAsync(permission.Id, context.Token).ConfigureAwait(false);
            await ChangedAsync(context, permission.Id, "Permission " + permission.Name + " deleted.").ConfigureAwait(false);
            await RouteHelpers.NoContentAsync(context).ConfigureAwait(false);
        }

        private async Task<Permission> EditableAsync(HttpContextBase context)
        {
            Permission permission = await _Context.Database.Permissions.ReadAsync(RouteHelpers.Tenant(context), RouteHelpers.Param(context, "id")!, context.Token).ConfigureAwait(false) ?? throw new NotFoundException("Permission not found.");
            if (permission.IsBuiltIn || permission.IsProtected || permission.TenantId == null) throw new ConflictException("Protected", "Built-in permissions cannot be modified.");
            return permission;
        }

        private async Task ChangedAsync(HttpContextBase context, string id, string description)
        {
            _Context.Authorization.Invalidate();
            await _Context.Audit.WriteAsync(RouteHelpers.Context(context), AuditEventTypeEnum.PermissionChanged, "Permission", id, description, RouteHelpers.Tenant(context), context.Token).ConfigureAwait(false);
        }

        #endregion
    }
}
