namespace Hugo.Core.Security
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;

    /// <summary>
    /// A permission belonging to a built-in role definition.
    /// </summary>
    public class BuiltInPermission
    {
        /// <summary>
        /// Permission name, unique across built-ins.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Resource types.
        /// </summary>
        public List<string> ResourceTypes { get; set; } = new List<string>();

        /// <summary>
        /// Operations.
        /// </summary>
        public List<string> Operations { get; set; } = new List<string>();

        /// <summary>
        /// Permit or deny.
        /// </summary>
        public PermissionTypeEnum PermissionType { get; set; } = PermissionTypeEnum.Permit;
    }
}
