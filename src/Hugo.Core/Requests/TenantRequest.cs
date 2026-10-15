namespace Hugo.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;

    /// <summary>
    /// Tenant create or update request.
    /// </summary>
    public class TenantRequest
    {
        /// <summary>
        /// Tenant name. Required on create.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Whether the tenant is active.
        /// </summary>
        public bool? Active { get; set; } = null;

        /// <summary>
        /// Region label, or null to leave unchanged.
        /// </summary>
        public string? Region { get; set; } = null;

        /// <summary>
        /// Parent tenant identifier, or null to leave unchanged.
        /// </summary>
        public string? ParentTenantId { get; set; } = null;

        /// <summary>
        /// Default agent profile identifier, or null to leave unchanged.
        /// </summary>
        public string? DefaultAgentProfileId { get; set; } = null;
    }
}
