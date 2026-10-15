namespace Hugo.Sdk.Requests
{
    /// <summary>
    /// Create or update a tenant. Null properties are omitted.
    /// </summary>
    public class TenantRequest
    {
        #region Public-Members

        /// <summary>
        /// Name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Active.
        /// </summary>
        public bool? Active { get; set; } = null;

        /// <summary>
        /// Region.
        /// </summary>
        public string? Region { get; set; } = null;

        /// <summary>
        /// Parent tenant Identifier.
        /// </summary>
        public string? ParentTenantId { get; set; } = null;

        /// <summary>
        /// Default agent profile Identifier.
        /// </summary>
        public string? DefaultAgentProfileId { get; set; } = null;

        #endregion
    }
}
