namespace Hugo.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;

    /// <summary>
    /// Custom permission create or update request.
    /// </summary>
    public class PermissionRequest
    {
        /// <summary>
        /// Permission name. Required on create.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Permit or deny.
        /// </summary>
        public PermissionTypeEnum? PermissionType { get; set; } = null;

        /// <summary>
        /// Resource type names (ResourceTypeEnum) or All.
        /// </summary>
        public List<string>? ResourceTypes { get; set; } = null;

        /// <summary>
        /// Operation names (OperationTypeEnum) or All.
        /// </summary>
        public List<string>? OperationTypes { get; set; } = null;

        /// <summary>
        /// Whether the permission is active.
        /// </summary>
        public bool? Active { get; set; } = null;
    }
}
