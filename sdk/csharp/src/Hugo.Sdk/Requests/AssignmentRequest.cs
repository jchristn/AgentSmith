namespace Hugo.Sdk.Requests
{
    using Hugo.Sdk.Enums;
    using Hugo.Sdk.Models;

    /// <summary>
    /// Create or update a user role assignment or a credential scope assignment.
    /// </summary>
    public class AssignmentRequest
    {
        #region Public-Members

        /// <summary>
        /// User identifier (user role assignments).
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Credential identifier (credential scope assignments).
        /// </summary>
        public string? CredentialId { get; set; } = null;

        /// <summary>
        /// Role Identifier.
        /// </summary>
        public string? RoleId { get; set; } = null;

        /// <summary>
        /// Role name, as an alternative to RoleId.
        /// </summary>
        public string? RoleName { get; set; } = null;

        /// <summary>
        /// Resource scope.
        /// </summary>
        public ResourceScopeEnum? ResourceScope { get; set; } = null;

        /// <summary>
        /// Resource Identifier.
        /// </summary>
        public string? ResourceId { get; set; } = null;

        /// <summary>
        /// Inherits to children.
        /// </summary>
        public bool? InheritsToChildren { get; set; } = null;

        /// <summary>
        /// Active.
        /// </summary>
        public bool? Active { get; set; } = null;

        #endregion
    }
}
