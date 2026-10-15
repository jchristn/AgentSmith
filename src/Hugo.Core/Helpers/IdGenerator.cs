namespace Hugo.Core.Helpers
{
    using System;

    /// <summary>
    /// Generates prefixed, K-sortable PrettyId identifiers (prefix plus 32 random characters). Prefixes are defined in
    /// <see cref="Constants"/>. Thread-safe.
    /// </summary>
    public static class IdGenerator
    {
        #region Private-Members

        private static readonly PrettyId.IdGenerator _Generator = new PrettyId.IdGenerator();
        private static readonly int _RandomLength = 32;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Generate an identifier with an arbitrary prefix.
        /// </summary>
        /// <param name="prefix">Prefix including the trailing underscore.</param>
        /// <returns>Identifier.</returns>
        /// <exception cref="ArgumentNullException">Thrown when prefix is null or empty.</exception>
        public static string New(string prefix)
        {
            if (String.IsNullOrEmpty(prefix)) throw new ArgumentNullException(nameof(prefix));
            return _Generator.GenerateKSortable(prefix, _RandomLength);
        }

        /// <summary>Generate a tenant identifier.</summary>
        /// <returns>Identifier.</returns>
        public static string NewTenantId() { return New(Constants.TenantIdPrefix); }

        /// <summary>Generate a user identifier.</summary>
        /// <returns>Identifier.</returns>
        public static string NewUserId() { return New(Constants.UserIdPrefix); }

        /// <summary>Generate a credential identifier.</summary>
        /// <returns>Identifier.</returns>
        public static string NewCredentialId() { return New(Constants.CredentialIdPrefix); }

        /// <summary>Generate an authentication session identifier.</summary>
        /// <returns>Identifier.</returns>
        public static string NewAuthSessionId() { return New(Constants.AuthSessionIdPrefix); }

        /// <summary>Generate a role identifier.</summary>
        /// <returns>Identifier.</returns>
        public static string NewRoleId() { return New(Constants.RoleIdPrefix); }

        /// <summary>Generate a permission identifier.</summary>
        /// <returns>Identifier.</returns>
        public static string NewPermissionId() { return New(Constants.PermissionIdPrefix); }

        /// <summary>Generate a role permission map identifier.</summary>
        /// <returns>Identifier.</returns>
        public static string NewRolePermissionMapId() { return New(Constants.RolePermissionMapIdPrefix); }

        /// <summary>Generate a user role assignment identifier.</summary>
        /// <returns>Identifier.</returns>
        public static string NewUserRoleAssignmentId() { return New(Constants.UserRoleAssignmentIdPrefix); }

        /// <summary>Generate a credential scope assignment identifier.</summary>
        /// <returns>Identifier.</returns>
        public static string NewCredentialScopeAssignmentId() { return New(Constants.CredentialScopeAssignmentIdPrefix); }

        /// <summary>Generate an audit record identifier.</summary>
        /// <returns>Identifier.</returns>
        public static string NewAuditRecordId() { return New(Constants.AuditRecordIdPrefix); }

        /// <summary>Generate a request history entry identifier.</summary>
        /// <returns>Identifier.</returns>
        public static string NewRequestHistoryEntryId() { return New(Constants.RequestHistoryEntryIdPrefix); }

        /// <summary>Generate a request history header row identifier.</summary>
        /// <returns>Identifier.</returns>
        public static string NewRequestHistoryHeaderId() { return New(Constants.RequestHistoryHeaderIdPrefix); }

        /// <summary>Generate a prompt template identifier.</summary>
        /// <returns>Identifier.</returns>
        public static string NewPromptTemplateId() { return New(Constants.PromptTemplateIdPrefix); }

        /// <summary>Generate a prompt template variable identifier.</summary>
        /// <returns>Identifier.</returns>
        public static string NewPromptTemplateVariableId() { return New(Constants.PromptTemplateVariableIdPrefix); }

        /// <summary>Generate a prompt template version identifier.</summary>
        /// <returns>Identifier.</returns>
        public static string NewPromptTemplateVersionId() { return New(Constants.PromptTemplateVersionIdPrefix); }

        /// <summary>Generate a secret identifier.</summary>
        /// <returns>Identifier.</returns>
        public static string NewSecretId() { return New(Constants.SecretIdPrefix); }

        /// <summary>Generate a secret value (encrypted field) identifier.</summary>
        /// <returns>Identifier.</returns>
        public static string NewSecretValueId() { return New(Constants.SecretValueIdPrefix); }

        /// <summary>Generate a master key version identifier.</summary>
        /// <returns>Identifier.</returns>
        public static string NewMasterKeyVersionId() { return New(Constants.MasterKeyVersionIdPrefix); }

        /// <summary>Generate a runtime setting row identifier.</summary>
        /// <returns>Identifier.</returns>
        public static string NewClusterSettingId() { return New(Constants.ClusterSettingIdPrefix); }

        /// <summary>Generate an identifier for a scalar child row.</summary>
        /// <returns>Identifier.</returns>
        public static string NewChildRowId() { return New(Constants.ChildRowIdPrefix); }

        /// <summary>New resource identifier.</summary>
        /// <returns>Identifier.</returns>
        public static string NewResourceId() { return New(Constants.ResourceIdPrefix); }

        /// <summary>New resource setting identifier.</summary>
        /// <returns>Identifier.</returns>
        public static string NewResourceSettingId() { return New(Constants.ResourceSettingIdPrefix); }

        /// <summary>New resource secret slot binding identifier.</summary>
        /// <returns>Identifier.</returns>
        public static string NewResourceSecretBindingId() { return New(Constants.ResourceSecretBindingIdPrefix); }

        /// <summary>New agent profile identifier.</summary>
        /// <returns>Identifier.</returns>
        public static string NewAgentProfileId() { return New(Constants.AgentProfileIdPrefix); }

        /// <summary>New binding identifier.</summary>
        /// <returns>Identifier.</returns>
        public static string NewBindingId() { return New(Constants.BindingIdPrefix); }

        /// <summary>New binding tool rule identifier.</summary>
        /// <returns>Identifier.</returns>
        public static string NewBindingToolRuleId() { return New(Constants.BindingToolRuleIdPrefix); }

        /// <summary>New tool invocation identifier.</summary>
        /// <returns>Identifier.</returns>
        public static string NewToolInvocationId() { return New(Constants.ToolInvocationIdPrefix); }

        /// <summary>New approval identifier.</summary>
        /// <returns>Identifier.</returns>
        public static string NewApprovalId() { return New(Constants.ApprovalIdPrefix); }

        /// <summary>New model endpoint identifier.</summary>
        /// <returns>Identifier.</returns>
        public static string NewModelEndpointId() { return New(Constants.ModelEndpointIdPrefix); }

        /// <summary>New model endpoint health check record identifier.</summary>
        /// <returns>Identifier.</returns>
        public static string NewModelHealthCheckId() { return New(Constants.ModelHealthCheckIdPrefix); }

        /// <summary>New plugin record identifier.</summary>
        /// <returns>Identifier.</returns>
        public static string NewPluginId() { return New(Constants.PluginIdPrefix); }

        /// <summary>New resource knowledge identifier.</summary>
        /// <returns>Identifier.</returns>
        public static string NewResourceKnowledgeId() { return New(Constants.ResourceKnowledgeIdPrefix); }

        /// <summary>New resource knowledge version identifier.</summary>
        /// <returns>Identifier.</returns>
        public static string NewResourceKnowledgeVersionId() { return New(Constants.ResourceKnowledgeVersionIdPrefix); }

        /// <summary>New connector schema cache identifier.</summary>
        /// <returns>Identifier.</returns>
        public static string NewConnectorSchemaCacheId() { return New(Constants.ConnectorSchemaCacheIdPrefix); }

        #endregion
    }
}
