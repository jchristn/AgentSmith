namespace Hugo.Core.Responses
{
    using System;

    /// <summary>
    /// A tenant an email can sign in to.
    /// </summary>
    public class TenantLookupEntry
    {
        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string TenantId { get; set; } = String.Empty;

        /// <summary>
        /// Tenant name.
        /// </summary>
        public string Name { get; set; } = String.Empty;
    }
}
