namespace Hugo.Sdk.Models
{
    using System.Collections.Generic;
    using Hugo.Sdk.Enums;

    /// <summary>
    /// One grant contributing to a principal's effective permissions.
    /// </summary>
    public class PermissionGrant
    {
        #region Public-Members

        /// <summary>
        /// Where the grant comes from, for example a role name.
        /// </summary>
        public string? Source { get; set; } = null;

        /// <summary>
        /// Permit or Deny.
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
        /// Tenant or Resource scope.
        /// </summary>
        public ResourceScopeEnum Scope { get; set; } = ResourceScopeEnum.Tenant;

        /// <summary>
        /// Resource Identifier.
        /// </summary>
        public string? ResourceId { get; set; } = null;

        /// <summary>
        /// Inherits to children.
        /// </summary>
        public bool InheritsToChildren { get; set; } = false;

        #endregion
    }
}
