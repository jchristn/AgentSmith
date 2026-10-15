namespace Hugo.Sdk.Requests
{
    using System;
    using Hugo.Sdk.Models;

    /// <summary>
    /// Map a permission to a role.
    /// </summary>
    public class RolePermissionRequest
    {
        #region Public-Members

        /// <summary>
        /// Role Identifier.
        /// </summary>
        public string RoleId { get; set; } = String.Empty;

        /// <summary>
        /// Permission Identifier.
        /// </summary>
        public string PermissionId { get; set; } = String.Empty;

        #endregion
    }
}
