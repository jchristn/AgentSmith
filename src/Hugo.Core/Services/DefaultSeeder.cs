namespace Hugo.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Security;

    /// <summary>
    /// Idempotent first-boot seeding (P1.15, plan section 17.3): the built-in roles and their permissions (refreshed on
    /// every start with stable identifiers, so edits to built-ins are reverted), the default tenant, the system
    /// administrator, and the tenant administrator Thomas Anderson. Seeded accounts must change their password.
    /// Thread-safe (runs under the database startup lock).
    /// </summary>
    public class DefaultSeeder
    {
        #region Private-Members

        private static readonly string _BuiltInPermissionPrefix = "prm_builtin_";
        private static readonly string _BuiltInMapPrefix = "rpm_builtin_";
        private readonly DatabaseDriverBase _Database;
        private readonly Action<string>? _Log;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="log">Optional log sink.</param>
        /// <exception cref="ArgumentNullException">Thrown when database is null.</exception>
        public DefaultSeeder(DatabaseDriverBase database, Action<string>? log = null)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Log = log;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Seed. Safe to run on every start.
        /// </summary>
        /// <param name="options">Seed options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The default tenant.</returns>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        public async Task<Tenant> SeedAsync(SeedOptions options, CancellationToken token = default)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            await using (DatabaseLockHandle startupLock = await _Database.AcquireStartupLockAsync(token).ConfigureAwait(false))
            {
                return await SeedLockedAsync(options, token).ConfigureAwait(false);
            }
        }

        #endregion

        #region Private-Methods

        private async Task<Tenant> SeedLockedAsync(SeedOptions options, CancellationToken token)
        {
            await SeedBuiltInRolesAsync(token).ConfigureAwait(false);

            Tenant? tenant = await _Database.Tenants.ReadAsync(Constants.DefaultTenantId, token).ConfigureAwait(false);
            if (tenant == null)
            {
                tenant = new Tenant();
                tenant.Id = Constants.DefaultTenantId;
                tenant.Name = Constants.DefaultTenantName;
                tenant.Description = "Default tenant created on first boot.";
                tenant.IsProtected = true;
                await _Database.Tenants.CreateAsync(tenant, token).ConfigureAwait(false);
                _Log?.Invoke("seeded tenant " + tenant.Name + " (" + tenant.Id + ")");
            }

            User? admin = await _Database.Users.ReadByEmailAsync(tenant.Id, options.AdminEmail, token).ConfigureAwait(false);
            if (admin == null)
            {
                admin = new User();
                admin.TenantId = tenant.Id;
                admin.FirstName = "System";
                admin.LastName = "Administrator";
                admin.Email = options.AdminEmail;
                admin.PasswordHash = PasswordHasher.Hash(options.AdminPassword, options.PasswordHashIterations);
                admin.IsAdmin = true;
                admin.IsTenantAdmin = true;
                admin.MustChangePassword = true;
                admin.IsProtected = true;
                await _Database.Users.CreateAsync(admin, token).ConfigureAwait(false);
                await AssignAsync(tenant.Id, admin.Id, BuiltInRoles.TenantAdmin, token).ConfigureAwait(false);
                _Log?.Invoke("seeded system administrator " + admin.Email + " (must change password at first sign-in)");
            }

            User? anderson = await _Database.Users.ReadByEmailAsync(tenant.Id, options.TenantAdminEmail, token).ConfigureAwait(false);
            if (anderson == null)
            {
                anderson = new User();
                anderson.TenantId = tenant.Id;
                anderson.FirstName = options.TenantAdminFirstName;
                anderson.LastName = options.TenantAdminLastName;
                anderson.Email = options.TenantAdminEmail;
                anderson.PasswordHash = PasswordHasher.Hash(options.TenantAdminPassword, options.PasswordHashIterations);
                anderson.IsTenantAdmin = true;
                anderson.MustChangePassword = true;
                await _Database.Users.CreateAsync(anderson, token).ConfigureAwait(false);
                await AssignAsync(tenant.Id, anderson.Id, BuiltInRoles.TenantAdmin, token).ConfigureAwait(false);
                _Log?.Invoke("seeded tenant administrator " + anderson.Email + " (must change password at first sign-in)");
            }

            return tenant;
        }

        private async Task SeedBuiltInRolesAsync(CancellationToken token)
        {
            HashSet<string> expectedPermissions = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> expectedMaps = new HashSet<string>(StringComparer.Ordinal);

            foreach (BuiltInRole definition in BuiltInRoles.All)
            {
                Role? role = await _Database.Roles.ReadAsync(definition.Id, token).ConfigureAwait(false);
                bool create = role == null;
                if (role == null) role = new Role();
                role.Id = definition.Id;
                role.TenantId = null;
                role.Name = definition.Name;
                role.Description = definition.Description;
                role.IsBuiltIn = true;
                role.IsProtected = true;
                role.Active = true;
                if (create) await _Database.Roles.CreateAsync(role, token).ConfigureAwait(false);
                else await _Database.Roles.UpdateAsync(role, token).ConfigureAwait(false);

                foreach (BuiltInPermission permissionDefinition in definition.Permissions)
                {
                    string suffix = permissionDefinition.Name.Replace(".", "_").ToLowerInvariant();
                    string permissionId = _BuiltInPermissionPrefix + suffix;
                    expectedPermissions.Add(permissionId);
                    Permission? permission = await _Database.Permissions.ReadAsync(permissionId, token).ConfigureAwait(false);
                    bool createPermission = permission == null;
                    if (permission == null) permission = new Permission();
                    permission.Id = permissionId;
                    permission.TenantId = null;
                    permission.Name = permissionDefinition.Name;
                    permission.PermissionType = permissionDefinition.PermissionType;
                    permission.ResourceTypes = new List<string>(permissionDefinition.ResourceTypes);
                    permission.OperationTypes = new List<string>(permissionDefinition.Operations);
                    permission.IsBuiltIn = true;
                    permission.IsProtected = true;
                    permission.Active = true;
                    if (createPermission) await _Database.Permissions.CreateAsync(permission, token).ConfigureAwait(false);
                    else await _Database.Permissions.UpdateAsync(permission, token).ConfigureAwait(false);

                    string mapId = _BuiltInMapPrefix + suffix;
                    expectedMaps.Add(mapId);
                    RolePermissionMap? map = await _Database.RolePermissionMaps.ReadAsync(mapId, token).ConfigureAwait(false);
                    if (map == null)
                    {
                        map = new RolePermissionMap();
                        map.Id = mapId;
                        map.TenantId = null;
                        map.RoleId = role.Id;
                        map.PermissionId = permission.Id;
                        map.IsProtected = true;
                        await _Database.RolePermissionMaps.CreateAsync(map, token).ConfigureAwait(false);
                    }
                    else if (map.RoleId != role.Id || map.PermissionId != permission.Id || !map.Active)
                    {
                        map.RoleId = role.Id;
                        map.PermissionId = permission.Id;
                        map.Active = true;
                        map.IsProtected = true;
                        await _Database.RolePermissionMaps.UpdateAsync(map, token).ConfigureAwait(false);
                    }
                }
            }

            // Built-in permissions and maps that a newer build no longer defines are removed, so a refreshed role never
            // keeps a grant it lost.
            EnumerationQuery query = new EnumerationQuery();
            query.MaxResults = 1000;
            EnumerationResult<RolePermissionMap> maps = await _Database.RolePermissionMaps.EnumerateAsync(null, query, token).ConfigureAwait(false);
            foreach (RolePermissionMap stale in maps.Objects.Where(m => m.TenantId == null && m.Id.StartsWith(_BuiltInMapPrefix, StringComparison.Ordinal) && !expectedMaps.Contains(m.Id)))
            {
                await _Database.RolePermissionMaps.DeleteAsync(stale.Id, token).ConfigureAwait(false);
            }
            EnumerationResult<Permission> permissions = await _Database.Permissions.EnumerateAsync(null, query, token).ConfigureAwait(false);
            foreach (Permission stale in permissions.Objects.Where(p => p.TenantId == null && p.Id.StartsWith(_BuiltInPermissionPrefix, StringComparison.Ordinal) && !expectedPermissions.Contains(p.Id)))
            {
                await _Database.Permissions.DeleteAsync(stale.Id, token).ConfigureAwait(false);
            }
        }

        private async Task AssignAsync(string tenantId, string userId, string roleName, CancellationToken token)
        {
            BuiltInRole role = BuiltInRoles.Find(roleName) ?? throw new InvalidOperationException("Unknown built-in role " + roleName + ".");
            UserRoleAssignment assignment = new UserRoleAssignment();
            assignment.TenantId = tenantId;
            assignment.UserId = userId;
            assignment.RoleId = role.Id;
            assignment.RoleName = role.Name;
            assignment.ResourceScope = ResourceScopeEnum.Tenant;
            assignment.InheritsToChildren = true;
            assignment.IsProtected = true;
            await _Database.UserRoleAssignments.CreateAsync(assignment, token).ConfigureAwait(false);
        }

        #endregion
    }
}
