namespace Hugo.Server.Protocols
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Result of hugo_list_resources.
    /// </summary>
    public class McpResourceList
    {
        /// <summary>
        /// Profile name.
        /// </summary>
        public string Profile { get; set; } = String.Empty;

        /// <summary>
        /// Bound resources, by alias.
        /// </summary>
        public List<McpBoundResource> Resources { get; set; } = new List<McpBoundResource>();
    }
}
