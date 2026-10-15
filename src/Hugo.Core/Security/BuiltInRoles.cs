namespace Hugo.Core.Security
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Built-in roles (plan section 15.4). Seeded with a null tenant and protected flags, and refreshed at startup with
    /// stable identifiers. Reveal on Secret is granted only to TenantAdmin; All and Write never imply it. Thread-safe
    /// (immutable after static initialization).
    /// </summary>
    public static class BuiltInRoles
    {
        #region Public-Members

        /// <summary>Permit All on All within the tenant, including Reveal on Secret.</summary>
        public const string TenantAdmin = "TenantAdmin";

        /// <summary>Administers identity, access, sessions, and audit.</summary>
        public const string SecurityAdmin = "SecurityAdmin";

        /// <summary>Reads security surfaces and activity.</summary>
        public const string Auditor = "Auditor";

        /// <summary>Operates agents: runs profiles, chats, decides approvals, reads activity.</summary>
        public const string Operator = "Operator";

        /// <summary>Operator plus authoring resources, secrets (not reveal), profiles, bindings, prompts, models, and knowledge.</summary>
        public const string Builder = "Builder";

        /// <summary>Permit All on one assigned resource (resource scoped).</summary>
        public const string ResourceAdmin = "ResourceAdmin";

        /// <summary>Read, write, and delete on the assigned resource.</summary>
        public const string Editor = "Editor";

        /// <summary>Read on the assigned resource.</summary>
        public const string Viewer = "Viewer";

        /// <summary>Use one agent profile (resource scoped): execute it and work with conversations.</summary>
        public const string ProfileUser = "ProfileUser";

        /// <summary>Minimal tenant presence.</summary>
        public const string TenantMember = "TenantMember";

        /// <summary>Template marker for tenant-defined roles; carries no permissions.</summary>
        public const string Custom = "Custom";

        /// <summary>
        /// Every built-in role.
        /// </summary>
        public static IReadOnlyList<BuiltInRole> All
        {
            get
            {
                return _All;
            }
        }

        #endregion

        #region Private-Members

        private static readonly List<BuiltInRole> _All = Build();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Find a built-in role by name (case-insensitive).
        /// </summary>
        /// <param name="name">Role name.</param>
        /// <returns>Role or null.</returns>
        public static BuiltInRole? Find(string? name)
        {
            if (String.IsNullOrEmpty(name)) return null;
            return _All.FirstOrDefault(r => String.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        #endregion

        #region Private-Methods

        private static BuiltInRole Role(string name, string description)
        {
            BuiltInRole role = new BuiltInRole();
            role.Id = "rol_builtin_" + name.ToLowerInvariant();
            role.Name = name;
            role.Description = description;
            return role;
        }

        private static void Grant(BuiltInRole role, string suffix, List<string> resourceTypes, List<string> operations)
        {
            BuiltInPermission permission = new BuiltInPermission();
            permission.Name = role.Name + "." + suffix;
            permission.ResourceTypes = resourceTypes;
            permission.Operations = operations;
            role.Permissions.Add(permission);
        }

        private static List<string> L(params string[] values)
        {
            return new List<string>(values);
        }

        private static List<BuiltInRole> Build()
        {
            List<BuiltInRole> roles = new List<BuiltInRole>();

            BuiltInRole tenantAdmin = Role(TenantAdmin, "All permissions on all resources within the tenant, including revealing secrets.");
            Grant(tenantAdmin, "All", L("All"), L("All"));
            Grant(tenantAdmin, "Reveal", L("Secret"), L("Reveal"));
            roles.Add(tenantAdmin);

            BuiltInRole securityAdmin = Role(SecurityAdmin, "Administers users, credentials, sessions, roles, permissions, assignments, audit, and tenant metadata.");
            Grant(securityAdmin, "Admin", L("User", "Credential", "Session", "Role", "Permission", "Assignment", "Audit", "Tenant"), L("All"));
            Grant(securityAdmin, "Read", L("Tenant", "Settings", "RequestHistory"), L("Read"));
            roles.Add(securityAdmin);

            BuiltInRole auditor = Role(Auditor, "Read-only access to security surfaces, audit, request history, tool invocations, and approvals.");
            Grant(auditor, "Read", L("User", "Credential", "Session", "Role", "Permission", "Assignment", "Audit", "Tenant", "RequestHistory", "ToolInvocation", "Approval"), L("Read"));
            roles.Add(auditor);

            BuiltInRole operatorRole = Role(Operator, "Runs agent profiles, works with conversations, decides approvals, and reads resources and activity.");
            Grant(operatorRole, "Read", L("Resource", "Connector", "AgentProfile", "ToolInvocation", "RequestHistory", "Analytics", "Approval"), L("Read"));
            Grant(operatorRole, "Execute", L("AgentProfile"), L("Execute"));
            Grant(operatorRole, "Conversation", L("Conversation"), L("Create", "Read", "Update"));
            Grant(operatorRole, "Approval", L("Approval"), L("Update"));
            roles.Add(operatorRole);

            BuiltInRole builder = Role(Builder, "Operator plus creating and changing resources, secrets (without reveal), agent profiles, bindings, prompt templates, model endpoints, and knowledge.");
            Grant(builder, "Read", L("Resource", "Connector", "AgentProfile", "ToolInvocation", "RequestHistory", "Analytics", "Approval"), L("Read"));
            Grant(builder, "Execute", L("AgentProfile"), L("Execute"));
            Grant(builder, "Conversation", L("Conversation"), L("Create", "Read", "Update"));
            Grant(builder, "Approval", L("Approval"), L("Update"));
            Grant(builder, "Author", L("Resource", "Secret", "AgentProfile", "Binding", "PromptTemplate", "ModelEndpoint", "Knowledge"), L("Create", "Read", "Update", "Delete"));
            roles.Add(builder);

            BuiltInRole resourceAdmin = Role(ResourceAdmin, "All permissions on one specific resource (assign with Resource scope). Does not include reveal.");
            Grant(resourceAdmin, "All", L("All"), L("All"));
            roles.Add(resourceAdmin);

            BuiltInRole editor = Role(Editor, "Read, write, and delete the assigned resource (assign with Resource scope).");
            Grant(editor, "Write", L("All"), L("Read", "Write"));
            roles.Add(editor);

            BuiltInRole viewer = Role(Viewer, "Read the assigned resource (assign with Resource scope).");
            Grant(viewer, "Read", L("All"), L("Read"));
            roles.Add(viewer);

            BuiltInRole profileUser = Role(ProfileUser, "Use one agent profile (assign with Resource scope on the profile): execute it and work with conversations.");
            Grant(profileUser, "Execute", L("AgentProfile"), L("Read", "Execute"));
            Grant(profileUser, "Conversation", L("Conversation"), L("Create", "Read", "Update"));
            roles.Add(profileUser);

            BuiltInRole tenantMember = Role(TenantMember, "Minimal tenant presence: read the tenant and agent profiles.");
            Grant(tenantMember, "Read", L("Tenant", "AgentProfile"), L("Read"));
            roles.Add(tenantMember);

            roles.Add(Role(Custom, "Template marker for tenant-defined roles. Carries no permissions."));

            return roles;
        }

        #endregion
    }
}
