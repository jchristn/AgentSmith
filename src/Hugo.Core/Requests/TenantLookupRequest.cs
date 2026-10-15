namespace Hugo.Core.Requests
{
    using System;

    /// <summary>
    /// Find the tenants an email can sign in to.
    /// </summary>
    public class TenantLookupRequest
    {
        /// <summary>
        /// Email address. The x-email header is used when omitted.
        /// </summary>
        public string? Email { get; set; } = null;
    }
}
