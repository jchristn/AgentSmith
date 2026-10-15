namespace Hugo.Core.Security
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;

    /// <summary>
    /// One expanded grant: a permission applied at a scope through a role assignment.
    /// </summary>
    public class PermissionGrant
    {
        /// <summary>
        /// Resource types covered (ResourceTypeEnum names, or All).
        /// </summary>
        public List<string> ResourceTypes { get; set; } = new List<string>();

        /// <summary>
        /// Operations covered (OperationTypeEnum names, or All).
        /// </summary>
        public List<string> Operations { get; set; } = new List<string>();

        /// <summary>
        /// Permit or deny.
        /// </summary>
        public PermissionTypeEnum PermissionType { get; set; } = PermissionTypeEnum.Permit;

        /// <summary>
        /// Scope of the assignment that produced the grant.
        /// </summary>
        public ResourceScopeEnum Scope { get; set; } = ResourceScopeEnum.Tenant;

        /// <summary>
        /// Resource identifier when resource-scoped.
        /// </summary>
        public string? ResourceId { get; set; } = null;

        /// <summary>
        /// Whether a tenant-scoped grant applies to resources inside the tenant.
        /// </summary>
        public bool InheritsToChildren { get; set; } = true;

        /// <summary>
        /// Role name that produced the grant, for diagnostics.
        /// </summary>
        public string Source { get; set; } = String.Empty;
    }
}
