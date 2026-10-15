namespace Test.Automated.Suites
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Hugo.Sdk.Enumeration;
    using Hugo.Sdk.Enums;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Requests;
    using Hugo.Sdk.Responses;

    /// <summary>
    /// Roles, permissions, role-permission mappings, and role assignments.
    /// </summary>
    public class RbacSuite : TestSuite
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Name
        {
            get
            {
                return "RBAC";
            }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override async Task RunAsync(TestRunner runner, HarnessContext ctx)
        {
            await runner.RunAsync("Roles CRUD and clone", async () =>
            {
                RoleRequest create = new RoleRequest();
                create.Name = Unique("role");
                create.Description = "harness role";
                Role role = await ctx.Owner.Roles.CreateAsync(create).ConfigureAwait(false);
                Check.True(!role.IsBuiltIn, "custom role");
                RoleRequest update = new RoleRequest();
                update.Name = role.Name;
                update.Description = "updated";
                Role updated = await ctx.Owner.Roles.UpdateAsync(role.Id, update).ConfigureAwait(false);
                Check.Equal("updated", updated.Description, "description");
                RoleRequest clone = new RoleRequest();
                clone.Name = Unique("clone");
                Role cloned = await ctx.Owner.Roles.CloneAsync(role.Id, clone).ConfigureAwait(false);
                Check.Equal(clone.Name, cloned.Name, "clone name");
                Role read = await ctx.Owner.Roles.GetAsync(cloned.Id).ConfigureAwait(false);
                Check.Equal(cloned.Id, read.Id, "clone readable");
                await ctx.Owner.Roles.DeleteAsync(cloned.Id).ConfigureAwait(false);
                await ctx.Owner.Roles.DeleteAsync(role.Id).ConfigureAwait(false);
                await Check.FailsAsync(() => ctx.Owner.Roles.GetAsync(role.Id), 404).ConfigureAwait(false);
            }).ConfigureAwait(false);

            await runner.RunAsync("Enumeration paging walks every role", async () =>
            {
                EnumerationQuery query = new EnumerationQuery();
                query.MaxResults = 1;
                EnumerationResult<Role> first = await ctx.Owner.Roles.ListAsync(query).ConfigureAwait(false);
                Check.Equal(1, first.Objects.Count, "page size");
                Check.True(first.TotalRecords > 1, "more than one role (built-ins)");
                List<Role> all = new List<Role>();
                await foreach (Role role in ctx.Owner.Roles.EnumerateAsync(query).ConfigureAwait(false))
                {
                    all.Add(role);
                }
                Check.Equal(first.TotalRecords, (long)all.Count, "enumerated count");
                Check.Equal(all.Count, all.Select(r => r.Id).Distinct().Count(), "no duplicates");
                Check.True(all.Any(r => r.IsBuiltIn), "built-in roles included");
            }).ConfigureAwait(false);

            await runner.RunAsync("Permissions CRUD", async () =>
            {
                PermissionRequest create = new PermissionRequest();
                create.Name = Unique("perm");
                create.PermissionType = PermissionTypeEnum.Permit;
                create.ResourceTypes = new List<string> { "Secret" };
                create.OperationTypes = new List<string> { "Read" };
                Permission permission = await ctx.Owner.Permissions.CreateAsync(create).ConfigureAwait(false);
                Check.True(permission.ResourceTypes.Contains("Secret"), "resource types");
                PermissionRequest update = new PermissionRequest();
                update.Name = permission.Name;
                update.PermissionType = PermissionTypeEnum.Permit;
                update.ResourceTypes = new List<string> { "Secret", "Audit" };
                update.OperationTypes = new List<string> { "Read" };
                Permission updated = await ctx.Owner.Permissions.UpdateAsync(permission.Id, update).ConfigureAwait(false);
                Check.Equal(2, updated.ResourceTypes.Count, "updated resource types");
                EnumerationResult<Permission> page = await ctx.Owner.Permissions.ListAsync().ConfigureAwait(false);
                Check.True(page.TotalRecords > 0, "list");
                await ctx.Owner.Permissions.DeleteAsync(permission.Id).ConfigureAwait(false);
                await Check.FailsAsync(() => ctx.Owner.Permissions.GetAsync(permission.Id), 404).ConfigureAwait(false);
            }).ConfigureAwait(false);

            await runner.RunAsync("Role-permission mappings", async () =>
            {
                RoleRequest roleRequest = new RoleRequest();
                roleRequest.Name = Unique("maprole");
                Role role = await ctx.Owner.Roles.CreateAsync(roleRequest).ConfigureAwait(false);
                PermissionRequest permissionRequest = new PermissionRequest();
                permissionRequest.Name = Unique("mapperm");
                permissionRequest.PermissionType = PermissionTypeEnum.Permit;
                permissionRequest.ResourceTypes = new List<string> { "Secret" };
                permissionRequest.OperationTypes = new List<string> { "Read" };
                Permission permission = await ctx.Owner.Permissions.CreateAsync(permissionRequest).ConfigureAwait(false);
                try
                {
                    RolePermissionMap map = await ctx.Owner.RolePermissions.CreateAsync(role.Id, permission.Id).ConfigureAwait(false);
                    EnumerationResult<RolePermissionMap> maps = await ctx.Owner.RolePermissions.ListAsync(role.Id).ConfigureAwait(false);
                    Check.True(maps.Objects.Any(m => m.Id == map.Id && m.PermissionId == permission.Id), "mapping listed for the role");
                    await ctx.Owner.RolePermissions.DeleteAsync(map.Id).ConfigureAwait(false);
                    maps = await ctx.Owner.RolePermissions.ListAsync(role.Id).ConfigureAwait(false);
                    Check.True(maps.Objects.All(m => m.Id != map.Id), "mapping removed");
                }
                finally
                {
                    await ctx.Owner.Permissions.DeleteAsync(permission.Id).ConfigureAwait(false);
                    await ctx.Owner.Roles.DeleteAsync(role.Id).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            await runner.RunAsync("User role assignments", async () =>
            {
                RoleRequest roleRequest = new RoleRequest();
                roleRequest.Name = Unique("urarole");
                Role role = await ctx.Owner.Roles.CreateAsync(roleRequest).ConfigureAwait(false);
                UserRequest userRequest = new UserRequest();
                userRequest.Email = Unique("ura") + "@sdk-csharp.test";
                userRequest.Password = "Password-" + HarnessContext.RandomHex(4);
                User user = await ctx.Owner.Users.CreateAsync(userRequest).ConfigureAwait(false);
                try
                {
                    AssignmentRequest create = new AssignmentRequest();
                    create.UserId = user.Id;
                    create.RoleId = role.Id;
                    create.ResourceScope = ResourceScopeEnum.Tenant;
                    UserRoleAssignment assignment = await ctx.Owner.UserRoleAssignments.CreateAsync(create).ConfigureAwait(false);
                    Check.Equal(user.Id, assignment.UserId, "user");
                    UserRoleAssignment read = await ctx.Owner.UserRoleAssignments.GetAsync(assignment.Id).ConfigureAwait(false);
                    Check.Equal(role.Id, read.RoleId, "role");
                    AssignmentRequest update = new AssignmentRequest();
                    update.UserId = user.Id;
                    update.RoleId = role.Id;
                    update.InheritsToChildren = true;
                    UserRoleAssignment updated = await ctx.Owner.UserRoleAssignments.UpdateAsync(assignment.Id, update).ConfigureAwait(false);
                    Check.True(updated.InheritsToChildren, "inherits");
                    EnumerationResult<UserRoleAssignment> page = await ctx.Owner.UserRoleAssignments.ListForUserAsync(user.Id).ConfigureAwait(false);
                    Check.True(page.Objects.Any(a => a.Id == assignment.Id), "listed for the user");
                    EffectivePermissions permissions = await ctx.Owner.Users.GetPermissionsAsync(user.Id).ConfigureAwait(false);
                    Check.Equal(user.Id, permissions.PrincipalId, "permissions principal");
                    await ctx.Owner.UserRoleAssignments.DeleteAsync(assignment.Id).ConfigureAwait(false);
                    await Check.FailsAsync(() => ctx.Owner.UserRoleAssignments.GetAsync(assignment.Id), 404).ConfigureAwait(false);
                }
                finally
                {
                    await ctx.Owner.Users.DeleteAsync(user.Id).ConfigureAwait(false);
                    await ctx.Owner.Roles.DeleteAsync(role.Id).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            await runner.RunAsync("Credential scope assignments", async () =>
            {
                RoleRequest roleRequest = new RoleRequest();
                roleRequest.Name = Unique("csarole");
                Role role = await ctx.Owner.Roles.CreateAsync(roleRequest).ConfigureAwait(false);
                CredentialRequest credentialRequest = new CredentialRequest();
                credentialRequest.Name = Unique("csa");
                CredentialCreateResponse credential = await ctx.Owner.Credentials.CreateAsync(credentialRequest).ConfigureAwait(false);
                try
                {
                    AssignmentRequest create = new AssignmentRequest();
                    create.CredentialId = credential.Credential.Id;
                    create.RoleId = role.Id;
                    create.ResourceScope = ResourceScopeEnum.Tenant;
                    CredentialScopeAssignment assignment = await ctx.Owner.CredentialScopeAssignments.CreateAsync(create).ConfigureAwait(false);
                    Check.Equal(credential.Credential.Id, assignment.CredentialId, "credential");
                    CredentialScopeAssignment read = await ctx.Owner.CredentialScopeAssignments.GetAsync(assignment.Id).ConfigureAwait(false);
                    Check.Equal(role.Id, read.RoleId, "role");
                    EnumerationResult<CredentialScopeAssignment> page = await ctx.Owner.CredentialScopeAssignments.ListForCredentialAsync(credential.Credential.Id).ConfigureAwait(false);
                    Check.True(page.Objects.Any(a => a.Id == assignment.Id), "listed for the credential");
                    await ctx.Owner.CredentialScopeAssignments.DeleteAsync(assignment.Id).ConfigureAwait(false);
                    await Check.FailsAsync(() => ctx.Owner.CredentialScopeAssignments.GetAsync(assignment.Id), 404).ConfigureAwait(false);
                }
                finally
                {
                    await ctx.Owner.Credentials.DeleteAsync(credential.Credential.Id).ConfigureAwait(false);
                    await ctx.Owner.Roles.DeleteAsync(role.Id).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
        }

        #endregion
    }
}
