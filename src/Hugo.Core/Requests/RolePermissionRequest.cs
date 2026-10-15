namespace Hugo.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;

    /// <summary>
    /// Role to permission mapping request.
    /// </summary>
    public class RolePermissionRequest
    {
        /// <summary>
        /// Role identifier.
        /// </summary>
        public string? RoleId { get; set; } = null;

        /// <summary>
        /// Permission identifier.
        /// </summary>
        public string? PermissionId { get; set; } = null;
    }
}
