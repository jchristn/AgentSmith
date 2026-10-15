namespace Hugo.Server.Protocols
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Result of hugo_whoami.
    /// </summary>
    public class McpWhoami
    {
        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string TenantId { get; set; } = String.Empty;

        /// <summary>
        /// Tenant name.
        /// </summary>
        public string TenantName { get; set; } = String.Empty;

        /// <summary>
        /// Principal type (User, Credential, Administrator).
        /// </summary>
        public string PrincipalType { get; set; } = String.Empty;

        /// <summary>
        /// Principal identifier, or null for the administrator API key.
        /// </summary>
        public string? PrincipalId { get; set; } = null;

        /// <summary>
        /// Principal display name.
        /// </summary>
        public string? PrincipalName { get; set; } = null;

        /// <summary>
        /// Agent profile identifier.
        /// </summary>
        public string ProfileId { get; set; } = String.Empty;

        /// <summary>
        /// Agent profile name.
        /// </summary>
        public string Profile { get; set; } = String.Empty;

        /// <summary>
        /// How the profile was chosen: Explicit, Credential, User, or Tenant.
        /// </summary>
        public string ProfileSource { get; set; } = String.Empty;

        /// <summary>
        /// The persona's name (for example Agent Smith).
        /// </summary>
        public string PersonaName { get; set; } = String.Empty;

        /// <summary>
        /// How the persona addresses the operator (for example Mr. Anderson).
        /// </summary>
        public string UserAddress { get; set; } = String.Empty;

        /// <summary>
        /// One-line access summary.
        /// </summary>
        public string AccessSummary { get; set; } = String.Empty;

        /// <summary>
        /// Bound resources.
        /// </summary>
        public List<McpBoundResource> Resources { get; set; } = new List<McpBoundResource>();

        /// <summary>
        /// Whether ask_agent_smith is available.
        /// </summary>
        public bool AskAgentSmith { get; set; } = false;

        /// <summary>
        /// Whether management tools are available.
        /// </summary>
        public bool ManagementTools { get; set; } = false;
    }
}
