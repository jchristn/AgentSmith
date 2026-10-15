namespace Hugo.Sdk.Models
{
    using System;
    using Hugo.Sdk.Enums;

    /// <summary>
    /// Assignment of a role to a credential.
    /// </summary>
    public class CredentialScopeAssignment
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Tenant Identifier.
        /// </summary>
        public string TenantId { get; set; } = String.Empty;

        /// <summary>
        /// Credential Identifier.
        /// </summary>
        public string CredentialId { get; set; } = String.Empty;

        /// <summary>
        /// Role Identifier.
        /// </summary>
        public string? RoleId { get; set; } = null;

        /// <summary>
        /// Role name.
        /// </summary>
        public string? RoleName { get; set; } = null;

        /// <summary>
        /// Tenant or Resource scope.
        /// </summary>
        public ResourceScopeEnum ResourceScope { get; set; } = ResourceScopeEnum.Tenant;

        /// <summary>
        /// Resource identifier when the scope is Resource.
        /// </summary>
        public string? ResourceId { get; set; } = null;

        /// <summary>
        /// Whether the assignment applies to child tenants.
        /// </summary>
        public bool InheritsToChildren { get; set; } = false;

        /// <summary>
        /// Whether the record is active.
        /// </summary>
        public bool Active { get; set; } = false;

        /// <summary>
        /// Whether the record is protected from deletion.
        /// </summary>
        public bool IsProtected { get; set; } = false;

        /// <summary>
        /// Creation timestamp, UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update timestamp, UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
