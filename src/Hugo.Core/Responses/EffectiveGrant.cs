namespace Hugo.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;

    /// <summary>
    /// One effective grant.
    /// </summary>
    public class EffectiveGrant
    {
        /// <summary>
        /// Role that produced the grant.
        /// </summary>
        public string Source { get; set; } = String.Empty;

        /// <summary>
        /// Permit or deny.
        /// </summary>
        public PermissionTypeEnum PermissionType { get; set; } = PermissionTypeEnum.Permit;

        /// <summary>
        /// Resource types.
        /// </summary>
        public List<string> ResourceTypes { get; set; } = new List<string>();

        /// <summary>
        /// Operations.
        /// </summary>
        public List<string> Operations { get; set; } = new List<string>();

        /// <summary>
        /// Scope.
        /// </summary>
        public ResourceScopeEnum Scope { get; set; } = ResourceScopeEnum.Tenant;

        /// <summary>
        /// Resource identifier for Resource scope.
        /// </summary>
        public string? ResourceId { get; set; } = null;

        /// <summary>
        /// Whether a tenant grant applies to resources.
        /// </summary>
        public bool InheritsToChildren { get; set; } = true;
    }
}
