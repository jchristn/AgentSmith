namespace Hugo.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;

    /// <summary>
    /// Effective permissions of a user or credential.
    /// </summary>
    public class EffectivePermissionsResponse
    {
        /// <summary>
        /// Principal type.
        /// </summary>
        public PrincipalTypeEnum PrincipalType { get; set; } = PrincipalTypeEnum.User;

        /// <summary>
        /// Principal identifier.
        /// </summary>
        public string PrincipalId { get; set; } = String.Empty;

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string TenantId { get; set; } = String.Empty;

        /// <summary>
        /// System administrator bypass.
        /// </summary>
        public bool IsAdmin { get; set; } = false;

        /// <summary>
        /// Tenant administrator bypass.
        /// </summary>
        public bool IsTenantAdmin { get; set; } = false;

        /// <summary>
        /// Expanded grants.
        /// </summary>
        public List<EffectiveGrant> Grants { get; set; } = new List<EffectiveGrant>();

        /// <summary>
        /// Tenant-level permission matrix.
        /// </summary>
        public List<PermissionCheck> HasPermission { get; set; } = new List<PermissionCheck>();
    }
}
