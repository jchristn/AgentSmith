namespace Hugo.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;

    /// <summary>
    /// Token issuance request. Supply tenant, email, and password for a user session, or access key and secret key for a credential session. Header equivalents are accepted.
    /// </summary>
    public class TokenRequest
    {
        /// <summary>
        /// Tenant identifier for user login. Defaults to the Default tenant.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// User email.
        /// </summary>
        public string? Email { get; set; } = null;

        /// <summary>
        /// User password.
        /// </summary>
        public string? Password { get; set; } = null;

        /// <summary>
        /// Credential access key.
        /// </summary>
        public string? AccessKey { get; set; } = null;

        /// <summary>
        /// Credential secret key.
        /// </summary>
        public string? SecretKey { get; set; } = null;
    }
}
