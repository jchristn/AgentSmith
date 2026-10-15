namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Security;

    /// <summary>
    /// Cached grant set for one principal.
    /// </summary>
    public class CachedGrants
    {
        /// <summary>
        /// Grants.
        /// </summary>
        public List<PermissionGrant> Grants { get; set; } = new List<PermissionGrant>();

        /// <summary>
        /// Expiry (UTC).
        /// </summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow;
    }
}
