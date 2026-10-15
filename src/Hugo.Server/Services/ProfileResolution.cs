namespace Hugo.Server.Services
{
    using System;
    using Hugo.Core.Models;

    /// <summary>
    /// The profile a request acts as, and how it was chosen (D-018). Not thread-safe.
    /// </summary>
    public class ProfileResolution
    {
        /// <summary>
        /// Profile.
        /// </summary>
        public AgentProfile Profile { get; set; } = new AgentProfile();

        /// <summary>
        /// Source: Explicit (path, header, or body), Credential, User, or Tenant.
        /// </summary>
        public string Source { get; set; } = String.Empty;
    }
}
