namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>
    /// A resource or model endpoint that references a secret.
    /// </summary>
    public class SecretReferrer
    {
        #region Public-Members

        /// <summary>
        /// Referrer type, for example Resource or ModelEndpoint.
        /// </summary>
        public string ReferrerType { get; set; } = String.Empty;

        /// <summary>
        /// Referrer Identifier.
        /// </summary>
        public string ReferrerId { get; set; } = String.Empty;

        /// <summary>
        /// Referrer name.
        /// </summary>
        public string? ReferrerName { get; set; } = null;

        /// <summary>
        /// Detail.
        /// </summary>
        public string? Detail { get; set; } = null;

        #endregion
    }
}
