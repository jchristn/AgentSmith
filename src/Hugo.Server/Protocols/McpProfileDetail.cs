namespace Hugo.Server.Protocols
{
    using System.Collections.Generic;
    using Hugo.Core.Models;

    /// <summary>
    /// Result of hugo_admin_describe_profile.
    /// </summary>
    public class McpProfileDetail
    {
        /// <summary>
        /// Profile.
        /// </summary>
        public AgentProfile Profile { get; set; } = new AgentProfile();

        /// <summary>
        /// Bindings.
        /// </summary>
        public List<Binding> Bindings { get; set; } = new List<Binding>();
    }
}
