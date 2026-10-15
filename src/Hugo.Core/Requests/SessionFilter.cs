namespace Hugo.Core.Requests
{
    using System;
    using Hugo.Core.Enums;

    /// <summary>
    /// Filters for listing authentication sessions. Null fields do not filter.
    /// </summary>
    public class SessionFilter
    {
        /// <summary>
        /// Principal type (User, Credential, or Administrator).
        /// </summary>
        public PrincipalTypeEnum? PrincipalType { get; set; } = null;

        /// <summary>
        /// User identifier (sessions a user signed in with).
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Credential identifier (sessions exchanged from a credential).
        /// </summary>
        public string? CredentialId { get; set; } = null;
    }
}
