namespace Hugo.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;

    /// <summary>
    /// Role assignment request for a user or credential.
    /// </summary>
    public class AssignmentRequest
    {
        /// <summary>
        /// User identifier (user role assignments).
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Credential identifier (credential scope assignments).
        /// </summary>
        public string? CredentialId { get; set; } = null;

        /// <summary>
        /// Role identifier.
        /// </summary>
        public string? RoleId { get; set; } = null;

        /// <summary>
        /// Role name, used when RoleId is omitted.
        /// </summary>
        public string? RoleName { get; set; } = null;

        /// <summary>
        /// Tenant or Resource scope. Default Tenant.
        /// </summary>
        public ResourceScopeEnum? ResourceScope { get; set; } = null;

        /// <summary>
        /// Resource identifier for Resource scope.
        /// </summary>
        public string? ResourceId { get; set; } = null;

        /// <summary>
        /// Whether tenant-scoped grants apply to resources. Default true.
        /// </summary>
        public bool? InheritsToChildren { get; set; } = null;

        /// <summary>
        /// Whether the assignment is active.
        /// </summary>
        public bool? Active { get; set; } = null;
    }
}
