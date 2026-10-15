namespace Hugo.Sdk.Requests
{
    using System;

    /// <summary>
    /// Authentication session list filter.
    /// </summary>
    public class SessionFilter
    {
        #region Public-Members

        /// <summary>
        /// Administrator, User, or Credential.
        /// </summary>
        public string? PrincipalType { get; set; } = null;

        /// <summary>
        /// User identifier.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Credential identifier.
        /// </summary>
        public string? CredentialId { get; set; } = null;

        #endregion
    }
}
