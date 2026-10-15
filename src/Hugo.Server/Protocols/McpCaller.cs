namespace Hugo.Server.Protocols
{
    using System;
    using Hugo.Core.Models;
    using Hugo.Core.Security;

    /// <summary>
    /// An authenticated MCP caller with its resolved agent profile (D-018). The proxy builds it once per HTTP request and
    /// hands it to the internal endpoint's handlers.
    /// </summary>
    public class McpCaller
    {
        #region Public-Members

        /// <summary>
        /// Authenticated principal (with the user or credential record attached).
        /// </summary>
        public RequestContext Principal
        {
            get
            {
                return _Principal;
            }
            set
            {
                _Principal = value ?? throw new ArgumentNullException(nameof(Principal));
            }
        }

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string TenantId { get; set; } = String.Empty;

        /// <summary>
        /// Resolved agent profile.
        /// </summary>
        public AgentProfile Profile
        {
            get
            {
                return _Profile;
            }
            set
            {
                _Profile = value ?? throw new ArgumentNullException(nameof(Profile));
            }
        }

        /// <summary>
        /// How the profile was resolved: Explicit (path or x-hugo-profile), Credential, User, or Tenant.
        /// </summary>
        public string ProfileSource { get; set; } = String.Empty;

        /// <summary>
        /// The profile the request named explicitly (path or x-hugo-profile), or null.
        /// </summary>
        public string? ExplicitProfile { get; set; } = null;

        #endregion

        #region Private-Members

        private RequestContext _Principal = new RequestContext();
        private AgentProfile _Profile = new AgentProfile();

        #endregion

        #region Public-Methods

        /// <summary>
        /// The principal string an MCP session is bound to: principal type, identifier, and profile, so a session opened
        /// for one profile cannot be reused for another.
        /// </summary>
        /// <returns>Principal key.</returns>
        public string SessionPrincipal()
        {
            return _Principal.PrincipalType + ":" + (_Principal.PrincipalId ?? "admin") + ":" + TenantId + ":" + _Profile.Id;
        }

        #endregion
    }
}
