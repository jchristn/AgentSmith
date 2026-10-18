namespace Hugo.Core.Enums
{
    /// <summary>
    /// Resource types used for authorization (plan section 15.3). Types for later phases are declared now so permissions can name them.
    /// </summary>
    public enum ResourceTypeEnum
    {
        /// <summary>
        /// Every resource type.
        /// </summary>
        All,

        /// <summary>
        /// Tenants.
        /// </summary>
        Tenant,

        /// <summary>
        /// System administration surfaces.
        /// </summary>
        Admin,

        /// <summary>
        /// Users.
        /// </summary>
        User,

        /// <summary>
        /// Credentials (access keys).
        /// </summary>
        Credential,

        /// <summary>
        /// Authentication sessions.
        /// </summary>
        Session,

        /// <summary>
        /// Roles.
        /// </summary>
        Role,

        /// <summary>
        /// Permissions and role permission maps.
        /// </summary>
        Permission,

        /// <summary>
        /// User role assignments and credential scope assignments.
        /// </summary>
        Assignment,

        /// <summary>
        /// Audit records.
        /// </summary>
        Audit,

        /// <summary>
        /// Server settings and restart.
        /// </summary>
        Settings,

        /// <summary>
        /// Secrets in the vault. Reveal is a separate operation.
        /// </summary>
        Secret,

        /// <summary>
        /// Configured resources (SSH hosts, buckets, databases, and so on).
        /// </summary>
        Resource,

        /// <summary>
        /// Connector descriptors.
        /// </summary>
        Connector,

        /// <summary>
        /// Agent profiles.
        /// </summary>
        AgentProfile,

        /// <summary>
        /// Bindings between agent profiles and resources.
        /// </summary>
        Binding,

        /// <summary>
        /// Prompt templates and their versions.
        /// </summary>
        PromptTemplate,

        /// <summary>
        /// Inference and embedding model endpoints.
        /// </summary>
        ModelEndpoint,

        /// <summary>
        /// Conversations and messages.
        /// </summary>
        Conversation,

        /// <summary>
        /// Tool invocation records.
        /// </summary>
        ToolInvocation,

        /// <summary>
        /// Approvals.
        /// </summary>
        Approval,

        /// <summary>
        /// Captured HTTP request history.
        /// </summary>
        RequestHistory,

        /// <summary>
        /// Knowledge notes.
        /// </summary>
        Knowledge,

        /// <summary>
        /// Connector plugins.
        /// </summary>
        Plugin,

        /// <summary>
        /// Analytics views.
        /// </summary>
        Analytics,

        /// <summary>Reusable prompt documents.</summary>
        PromptDocument
    }
}
