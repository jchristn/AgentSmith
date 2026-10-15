namespace Hugo.Sdk.Requests
{
    /// <summary>
    /// Create or update a user. Null properties are omitted (and keep stored values on update).
    /// </summary>
    public class UserRequest
    {
        #region Public-Members

        /// <summary>
        /// Email.
        /// </summary>
        public string? Email { get; set; } = null;

        /// <summary>
        /// Initial password (8 to 256 characters) on create.
        /// </summary>
        public string? Password { get; set; } = null;

        /// <summary>
        /// First name.
        /// </summary>
        public string? FirstName { get; set; } = null;

        /// <summary>
        /// Last name.
        /// </summary>
        public string? LastName { get; set; } = null;

        /// <summary>
        /// Is tenant admin.
        /// </summary>
        public bool? IsTenantAdmin { get; set; } = null;

        /// <summary>
        /// System administrator flag (system administrators only).
        /// </summary>
        public bool? IsAdmin { get; set; } = null;

        /// <summary>
        /// Active.
        /// </summary>
        public bool? Active { get; set; } = null;

        /// <summary>
        /// Must change password.
        /// </summary>
        public bool? MustChangePassword { get; set; } = null;

        /// <summary>
        /// Preferred locale.
        /// </summary>
        public string? PreferredLocale { get; set; } = null;

        /// <summary>
        /// Default agent profile Identifier.
        /// </summary>
        public string? DefaultAgentProfileId { get; set; } = null;

        #endregion
    }
}
