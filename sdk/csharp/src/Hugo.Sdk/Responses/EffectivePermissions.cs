namespace Hugo.Sdk.Responses
{
    using System.Collections.Generic;
    using Hugo.Sdk.Enums;
    using Hugo.Sdk.Models;

    /// <summary>
    /// Effective permissions of a user or credential.
    /// </summary>
    public class EffectivePermissions
    {
        #region Public-Members

        /// <summary>
        /// Kind of principal.
        /// </summary>
        public PrincipalTypeEnum PrincipalType { get; set; } = PrincipalTypeEnum.User;

        /// <summary>
        /// Principal Identifier.
        /// </summary>
        public string? PrincipalId { get; set; } = null;

        /// <summary>
        /// Tenant Identifier.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Is admin.
        /// </summary>
        public bool IsAdmin { get; set; } = false;

        /// <summary>
        /// Is tenant admin.
        /// </summary>
        public bool IsTenantAdmin { get; set; } = false;

        /// <summary>
        /// Grants.
        /// </summary>
        public List<PermissionGrant> Grants { get; set; } = new List<PermissionGrant>();

        /// <summary>
        /// Resolved checks per resource type and operation.
        /// </summary>
        public List<PermissionCheck> HasPermission { get; set; } = new List<PermissionCheck>();

        #endregion
    }
}
