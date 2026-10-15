namespace Hugo.Sdk.Requests
{
    using System.Collections.Generic;
    using Hugo.Sdk.Enums;
    using Hugo.Sdk.Models;

    /// <summary>
    /// Create or update a permission.
    /// </summary>
    public class PermissionRequest
    {
        #region Public-Members

        /// <summary>
        /// Name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Permission type.
        /// </summary>
        public PermissionTypeEnum? PermissionType { get; set; } = null;

        /// <summary>
        /// Resource types, for example Secret or All.
        /// </summary>
        public List<string>? ResourceTypes { get; set; } = null;

        /// <summary>
        /// Operations, for example Read or All.
        /// </summary>
        public List<string>? OperationTypes { get; set; } = null;

        /// <summary>
        /// Active.
        /// </summary>
        public bool? Active { get; set; } = null;

        #endregion
    }
}
