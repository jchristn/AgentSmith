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
    using Hugo.Core.Security;
    using Hugo.Core.Services;
    using WatsonWebserver.Core;

    /// <summary>
    /// Role and role-permission routes. Built-in roles are listed read-only and can be cloned into custom roles.
    /// </summary>
    public class RoleRoutes
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
        public RoleRoutes(ServerContext context)
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
            routes.Scoped(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/roles", ResourceTypeEnum.Role, OperationTypeEnum.Read, ListAsync, "List roles, including built-in roles", "Roles", null, typeof(EnumerationResult<Role>));
            routes.Scoped(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/roles", ResourceTypeEnum.Role, OperationTypeEnum.Create, CreateAsync, "Create a custom role", "Roles", typeof(RoleRequest), typeof(Role));
            routes.Scoped(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/roles/{id}", ResourceTypeEnum.Role, OperationTypeEnum.Read, ReadAsync, "Read a role", "Roles", null, typeof(Role));
            routes.Scoped(HttpMethod.PUT, "/v1.0/api/tenants/{tenantId}/roles/{id}", ResourceTypeEnum.Role, OperationTypeEnum.Update, UpdateAsync, "Update a custom role", "Roles", typeof(RoleRequest), typeof(Role));
            routes.Scoped(HttpMethod.DELETE, "/v1.0/api/tenants/{tenantId}/roles/{id}", ResourceTypeEnum.Role, OperationTypeEnum.Delete, DeleteAsync, "Delete a custom role", "Roles");
            routes.Scoped(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/roles/{id}/clone", ResourceTypeEnum.Role, OperationTypeEnum.Create, CloneAsync, "Clone a role (including built-ins) into a custom role", "Roles", typeof(RoleRequest), typeof(Role));
            routes.Scoped(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/role-permissions", ResourceTypeEnum.Role, OperationTypeEnum.Read, ListMapsAsync, "List role-permission mappings (filter with roleId)", "Roles", null, typeof(EnumerationResult<RolePermissionMap>));
            routes.Scoped(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/role-permissions", ResourceTypeEnum.Role, OperationTypeEnum.Update, CreateMapAsync, "Map a permission to a custom role", "Roles", typeof(RolePermissionRequest), typeof(RolePermissionMap));
            routes.Scoped(HttpMethod.DELETE, "/v1.0/api/tenants/{tenantId}/role-permissions/{id}", ResourceTypeEnum.Role, OperationTypeEnum.Update, DeleteMapAsync, "Remove a role-permission mapping", "Roles");
        }

        #endregion

        #region Private-Methods

        private async Task ListAsync(HttpContextBase context)
        {
            EnumerationResult<Role> result = await _Context.Database.Roles.EnumerateAsync(RouteHelpers.Tenant(context), RouteHelpers.Enumeration(context), context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, result).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase context)
        {
            string tenantId = RouteHelpers.Tenant(context);
            RoleRequest request = RouteHelpers.RequiredBody<RoleRequest>(context);
            Role role = await NewRoleAsync(tenantId, request, context).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 201, role).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase context)
        {
            Role? role = await _Context.Database.Roles.ReadAsync(RouteHelpers.Tenant(context), RouteHelpers.Param(context, "id")!, context.Token).ConfigureAwait(false);
            if (role == null)
            {
                await RouteHelpers.NotFoundAsync(context, "Role").ConfigureAwait(false);
                return;
            }
            await RouteHelpers.JsonAsync(context, 200, role).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase context)
        {
            string tenantId = RouteHelpers.Tenant(context);
            Role role = await EditableRoleAsync(tenantId, RouteHelpers.Param(context, "id")!, context).ConfigureAwait(false);
            RoleRequest request = RouteHelpers.RequiredBody<RoleRequest>(context);
            if (!String.IsNullOrWhiteSpace(request.Name) && request.Name.Trim() != role.Name)
            {
                if (await _Context.Database.Roles.ReadByNameAsync(tenantId, request.Name.Trim(), context.Token).ConfigureAwait(false) != null) throw new ConflictException("Duplicate", "A role named '" + request.Name + "' already exists.");
                role.Name = request.Name.Trim();
            }
            if (request.Description != null) role.Description = request.Description;
            if (request.Active.HasValue) role.Active = request.Active.Value;
            await _Context.Database.Roles.UpdateAsync(role, context.Token).ConfigureAwait(false);
            await ChangedAsync(context, "Role", role.Id, "Role " + role.Name + " updated.").ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, role).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase context)
        {
            string tenantId = RouteHelpers.Tenant(context);
            Role role = await EditableRoleAsync(tenantId, RouteHelpers.Param(context, "id")!, context).ConfigureAwait(false);
            foreach (RolePermissionMap map in await _Context.Database.RolePermissionMaps.ListByRoleAsync(role.Id, context.Token).ConfigureAwait(false))
            {
                await _Context.Database.RolePermissionMaps.DeleteAsync(map.Id, context.Token).ConfigureAwait(false);
            }
            await _Context.Database.Roles.DeleteAsync(role.Id, context.Token).ConfigureAwait(false);
            await ChangedAsync(context, "Role", role.Id, "Role " + role.Name + " deleted.").ConfigureAwait(false);
            await RouteHelpers.NoContentAsync(context).ConfigureAwait(false);
        }

        private async Task CloneAsync(HttpContextBase context)
        {
            string tenantId = RouteHelpers.Tenant(context);
            Role source = await _Context.Database.Roles.ReadAsync(tenantId, RouteHelpers.Param(context, "id")!, context.Token).ConfigureAwait(false) ?? throw new NotFoundException("Role not found.");
            RoleRequest request = RouteHelpers.Body<RoleRequest>(context) ?? new RoleRequest();
            if (String.IsNullOrWhiteSpace(request.Name)) request.Name = source.Name + " (copy)";
            if (request.Description == null) request.Description = source.Description;
            Role clone = await NewRoleAsync(tenantId, request, context).ConfigureAwait(false);

            BuiltInRole? builtIn = BuiltInRoles.All.FirstOrDefault(r => r.Id == source.Id);
            if (builtIn != null)
            {
                foreach (BuiltInPermission definition in builtIn.Permissions)
                {
                    Permission permission = new Permission();
                    permission.TenantId = tenantId;
                    permission.Name = clone.Name + ": " + definition.Name;
                    permission.PermissionType = definition.PermissionType;
                    permission.ResourceTypes = new List<string>(definition.ResourceTypes);
                    permission.OperationTypes = new List<string>(definition.Operations);
                    await _Context.Database.Permissions.CreateAsync(permission, context.Token).ConfigureAwait(false);
                    await MapAsync(tenantId, clone.Id, permission.Id, context).ConfigureAwait(false);
                }
            }
            else
            {
                foreach (RolePermissionMap map in await _Context.Database.RolePermissionMaps.ListByRoleAsync(source.Id, context.Token).ConfigureAwait(false))
                {
                    await MapAsync(tenantId, clone.Id, map.PermissionId, context).ConfigureAwait(false);
                }
            }
            await RouteHelpers.JsonAsync(context, 201, clone).ConfigureAwait(false);
        }

        private async Task ListMapsAsync(HttpContextBase context)
        {
            string tenantId = RouteHelpers.Tenant(context);
            string? roleId = RouteHelpers.Query(context, "roleId");
            EnumerationQuery query = RouteHelpers.Enumeration(context);
            if (!String.IsNullOrEmpty(roleId))
            {
                Role? role = await _Context.Database.Roles.ReadAsync(tenantId, roleId, context.Token).ConfigureAwait(false);
                List<RolePermissionMap> maps = role == null ? new List<RolePermissionMap>() : await _Context.Database.RolePermissionMaps.ListByRoleAsync(role.Id, context.Token).ConfigureAwait(false);
                await RouteHelpers.JsonAsync(context, 200, EnumerationResult<RolePermissionMap>.Build(query, maps.Count, maps.Skip(query.Skip).Take(query.MaxResults).ToList())).ConfigureAwait(false);
                return;
            }
            EnumerationResult<RolePermissionMap> result = await _Context.Database.RolePermissionMaps.EnumerateAsync(tenantId, query, context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, result).ConfigureAwait(false);
        }

        private async Task CreateMapAsync(HttpContextBase context)
        {
            string tenantId = RouteHelpers.Tenant(context);
            RolePermissionRequest request = RouteHelpers.RequiredBody<RolePermissionRequest>(context);
            if (String.IsNullOrEmpty(request.RoleId) || String.IsNullOrEmpty(request.PermissionId)) throw new ValidationException("roleId and permissionId are required.");
            Role role = await EditableRoleAsync(tenantId, request.RoleId, context).ConfigureAwait(false);
            Permission permission = await _Context.Database.Permissions.ReadAsync(tenantId, request.PermissionId, context.Token).ConfigureAwait(false) ?? throw new ValidationException("The permission does not exist.");
            RolePermissionMap map = await MapAsync(tenantId, role.Id, permission.Id, context).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 201, map).ConfigureAwait(false);
        }

        private async Task DeleteMapAsync(HttpContextBase context)
        {
            string tenantId = RouteHelpers.Tenant(context);
            RolePermissionMap? map = await _Context.Database.RolePermissionMaps.ReadAsync(RouteHelpers.Param(context, "id")!, context.Token).ConfigureAwait(false);
            if (map == null || map.TenantId != tenantId) throw new NotFoundException("Role-permission mapping not found.");
            if (map.IsProtected) throw new ConflictException("Protected", "Built-in mappings cannot be removed.");
            await _Context.Database.RolePermissionMaps.DeleteAsync(map.Id, context.Token).ConfigureAwait(false);
            await ChangedAsync(context, "Role", map.RoleId, "Permission " + map.PermissionId + " removed from role.").ConfigureAwait(false);
            await RouteHelpers.NoContentAsync(context).ConfigureAwait(false);
        }

        private async Task<Role> NewRoleAsync(string tenantId, RoleRequest request, HttpContextBase context)
        {
            if (String.IsNullOrWhiteSpace(request.Name)) throw new ValidationException("A role name is required.", new Dictionary<string, string> { { "name", "A role name is required." } });
            if (await _Context.Database.Roles.ReadByNameAsync(tenantId, request.Name.Trim(), context.Token).ConfigureAwait(false) != null) throw new ConflictException("Duplicate", "A role named '" + request.Name + "' already exists.");
            Role role = new Role();
            role.TenantId = tenantId;
            role.Name = request.Name.Trim();
            role.Description = request.Description;
            role.Active = request.Active ?? true;
            await _Context.Database.Roles.CreateAsync(role, context.Token).ConfigureAwait(false);
            await ChangedAsync(context, "Role", role.Id, "Role " + role.Name + " created.").ConfigureAwait(false);
            return role;
        }

        private async Task<Role> EditableRoleAsync(string tenantId, string id, HttpContextBase context)
        {
            Role role = await _Context.Database.Roles.ReadAsync(tenantId, id, context.Token).ConfigureAwait(false) ?? throw new NotFoundException("Role not found.");
            if (role.IsBuiltIn || role.IsProtected || role.TenantId == null) throw new ConflictException("Protected", "Built-in roles cannot be modified; clone the role instead.");
            return role;
        }

        private async Task<RolePermissionMap> MapAsync(string tenantId, string roleId, string permissionId, HttpContextBase context)
        {
            RolePermissionMap map = new RolePermissionMap();
            map.TenantId = tenantId;
            map.RoleId = roleId;
            map.PermissionId = permissionId;
            await _Context.Database.RolePermissionMaps.CreateAsync(map, context.Token).ConfigureAwait(false);
            await ChangedAsync(context, "Role", roleId, "Permission " + permissionId + " mapped to role.").ConfigureAwait(false);
            return map;
        }

        private async Task ChangedAsync(HttpContextBase context, string resourceType, string resourceId, string description)
        {
            _Context.Authorization.Invalidate();
            await _Context.Audit.WriteAsync(RouteHelpers.Context(context), AuditEventTypeEnum.RoleChanged, resourceType, resourceId, description, RouteHelpers.Tenant(context), context.Token).ConfigureAwait(false);
        }

        #endregion
    }
}
