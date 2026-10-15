namespace Hugo.Server.Protocols
{
    using System.Collections.Generic;
    using Hugo.Core.Models;
    using Hugo.Core.Responses;

    /// <summary>
    /// Result of hugo_admin_describe_resource.
    /// </summary>
    public class McpResourceDetail
    {
        /// <summary>
        /// Resource (secret values are never included).
        /// </summary>
        public Resource Resource { get; set; } = new Resource();

        /// <summary>
        /// Profiles and bindings that use it.
        /// </summary>
        public List<ResourceReferrer> Referrers { get; set; } = new List<ResourceReferrer>();
    }
}
