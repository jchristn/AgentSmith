namespace Hugo.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;

    /// <summary>
    /// Credential create or update request.
    /// </summary>
    public class CredentialRequest
    {
        /// <summary>
        /// Credential name. Required on create.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Owning user. Defaults to the caller's user.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Authentication mode.
        /// </summary>
        public CredentialAuthModeEnum? AuthMode { get; set; } = null;

        /// <summary>
        /// Expiration (UTC).
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;

        /// <summary>
        /// Whether the credential is active.
        /// </summary>
        public bool? Active { get; set; } = null;

        /// <summary>
        /// Optional built-in or custom role to grant at tenant scope on create.
        /// </summary>
        public string? RoleName { get; set; } = null;
    }
}
