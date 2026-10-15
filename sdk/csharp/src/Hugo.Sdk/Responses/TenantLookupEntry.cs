namespace Hugo.Sdk.Responses
{
    using System;
    using Hugo.Sdk.Models;

    /// <summary>
    /// A tenant an email can sign in to.
    /// </summary>
    public class TenantLookupEntry
    {
        #region Public-Members

        /// <summary>
        /// Tenant Identifier.
        /// </summary>
        public string TenantId { get; set; } = String.Empty;

        /// <summary>
        /// Name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        #endregion
    }
}
