namespace Hugo.Core
{
    /// <summary>
    /// Product-wide constants that are not operator-configurable (identifier prefixes, protocol header names, and
    /// product identity). Configurable values live in settings classes.
    /// </summary>
    public static class Constants
    {
        /// <summary>Prompt document identifier prefix.</summary>
        public static readonly string PromptDocumentIdPrefix = "pdc_";
        /// <summary>Prompt document revision identifier prefix.</summary>
        public static readonly string PromptDocumentRevisionIdPrefix = "pdv_";
        /// <summary>Agent document attachment identifier prefix.</summary>
        public static readonly string AgentPromptDocumentIdPrefix = "pda_";

        #region Product

        /// <summary>
        /// Product name.
        /// </summary>
        public const string ProductName = "Hugo";

        /// <summary>
        /// Product version. Mirrors src/Directory.Build.props; never change without explicit approval.
        /// </summary>
        public const string ProductVersion = "0.1.0-alpha";

        /// <summary>
        /// Repository URL.
        /// </summary>
        public const string RepositoryUrl = "https://github.com/jchristn/AgentSmith";

        /// <summary>
        /// Prefix reserved for built-in prompt templates.
        /// </summary>
        public const string BuiltInPrefix = "builtin/";

        /// <summary>
        /// Identifier of the default tenant seeded on first boot. A fixed, well-known identifier so that login without a
        /// tenant header resolves to it.
        /// </summary>
        public const string DefaultTenantId = "default";

        /// <summary>
        /// Name of the default tenant seeded on first boot.
        /// </summary>
        public const string DefaultTenantName = "default";

        #endregion

        #region Identifier-Prefixes

        /// <summary>Account identifier prefix (optional entity).</summary>
        public const string AccountIdPrefix = "acc_";

        /// <summary>Administrator identifier prefix.</summary>
        public const string AdministratorIdPrefix = "adm_";

        /// <summary>Tenant identifier prefix.</summary>
        public const string TenantIdPrefix = "ten_";

        /// <summary>User identifier prefix.</summary>
        public const string UserIdPrefix = "usr_";

        /// <summary>Credential identifier prefix.</summary>
        public const string CredentialIdPrefix = "crd_";

        /// <summary>Authentication session identifier prefix.</summary>
        public const string AuthSessionIdPrefix = "ses_";

        /// <summary>Role identifier prefix.</summary>
        public const string RoleIdPrefix = "rol_";

        /// <summary>Permission identifier prefix.</summary>
        public const string PermissionIdPrefix = "prm_";

        /// <summary>Role permission map identifier prefix.</summary>
        public const string RolePermissionMapIdPrefix = "rpm_";

        /// <summary>User role assignment identifier prefix.</summary>
        public const string UserRoleAssignmentIdPrefix = "ura_";

        /// <summary>Credential scope assignment identifier prefix.</summary>
        public const string CredentialScopeAssignmentIdPrefix = "csa_";

        /// <summary>Audit record identifier prefix.</summary>
        public const string AuditRecordIdPrefix = "aud_";

        /// <summary>Request history entry identifier prefix.</summary>
        public const string RequestHistoryEntryIdPrefix = "req_";

        /// <summary>Request history header row identifier prefix.</summary>
        public const string RequestHistoryHeaderIdPrefix = "rhh_";

        /// <summary>Approval identifier prefix.</summary>
        public const string ApprovalIdPrefix = "apr_";

        /// <summary>Resource action identifier prefix.</summary>
        public const string ResourceActionIdPrefix = "act_";

        /// <summary>Plugin record identifier prefix.</summary>
        public const string PluginIdPrefix = "plg_";

        /// <summary>Resource identifier prefix.</summary>
        public const string ResourceIdPrefix = "res_";

        /// <summary>Resource setting identifier prefix.</summary>
        public const string ResourceSettingIdPrefix = "rst_";

        /// <summary>Resource secret slot binding identifier prefix.</summary>
        public const string ResourceSecretBindingIdPrefix = "rss_";

        /// <summary>Secret identifier prefix.</summary>
        public const string SecretIdPrefix = "sec_";

        /// <summary>Secret value (one encrypted field) identifier prefix.</summary>
        public const string SecretValueIdPrefix = "scv_";

        /// <summary>Master key version (canary row) identifier prefix.</summary>
        public const string MasterKeyVersionIdPrefix = "mkv_";

        /// <summary>Agent profile identifier prefix.</summary>
        public const string AgentProfileIdPrefix = "agp_";

        /// <summary>Binding (profile to resource) identifier prefix.</summary>
        public const string BindingIdPrefix = "bnd_";

        /// <summary>Binding tool rule identifier prefix.</summary>
        public const string BindingToolRuleIdPrefix = "btr_";

        /// <summary>Prompt template identifier prefix.</summary>
        public const string PromptTemplateIdPrefix = "tpl_";

        /// <summary>Prompt template variable identifier prefix.</summary>
        public const string PromptTemplateVariableIdPrefix = "tpa_";

        /// <summary>Prompt template version identifier prefix.</summary>
        public const string PromptTemplateVersionIdPrefix = "tpv_";

        /// <summary>Model endpoint identifier prefix.</summary>
        public const string ModelEndpointIdPrefix = "mep_";

        /// <summary>Model endpoint health check record identifier prefix.</summary>
        public const string ModelHealthCheckIdPrefix = "mhc_";

        /// <summary>Conversation identifier prefix.</summary>
        public const string ConversationIdPrefix = "cnv_";

        /// <summary>Message identifier prefix.</summary>
        public const string MessageIdPrefix = "msg_";

        /// <summary>Tool invocation identifier prefix.</summary>
        public const string ToolInvocationIdPrefix = "tiv_";

        /// <summary>A2A task identifier prefix.</summary>
        public const string A2ATaskIdPrefix = "a2t_";

        /// <summary>Knowledge note identifier prefix.</summary>
        public const string KnowledgeNoteIdPrefix = "kno_";

        /// <summary>
        /// Resource knowledge entry identifier prefix.
        /// </summary>
        public const string ResourceKnowledgeIdPrefix = "rkn_";

        /// <summary>
        /// Resource knowledge version identifier prefix.
        /// </summary>
        public const string ResourceKnowledgeVersionIdPrefix = "rkv_";

        /// <summary>
        /// Key of a knowledge scope's root entry, as stored and as sent over the API. Inputs accept this value or an
        /// empty key; connectors see the root as the empty key (the connector contract).
        /// </summary>
        public const string KnowledgeRootKey = "_root";

        /// <summary>
        /// Connector schema cache entry identifier prefix.
        /// </summary>
        public const string ConnectorSchemaCacheIdPrefix = "csc_";

        /// <summary>Signed-request nonce identifier prefix.</summary>
        public const string NonceIdPrefix = "non_";

        /// <summary>Runtime setting row identifier prefix.</summary>
        public const string ClusterSettingIdPrefix = "cls_";

        /// <summary>Generic scalar child row identifier prefix.</summary>
        public const string ChildRowIdPrefix = "chr_";

        #endregion

        #region Headers

        /// <summary>
        /// Header carrying a bearer token alternative.
        /// </summary>
        public const string HeaderToken = "x-token";

        /// <summary>
        /// Header carrying the system administrator API key.
        /// </summary>
        public const string HeaderApiKey = "x-api-key";

        /// <summary>
        /// Header carrying a tenant identifier (Hugo and Garrison name).
        /// </summary>
        public const string HeaderTenantId = "x-tenant-id";

        /// <summary>
        /// Header carrying a tenant identifier (AUTHENTICATION.md name). Accepted as an alias of <see cref="HeaderTenantId"/>.
        /// </summary>
        public const string HeaderTenantGuid = "x-tenant-guid";

        /// <summary>
        /// Header carrying a user email for token issuance.
        /// </summary>
        public const string HeaderEmail = "x-email";

        /// <summary>
        /// Header carrying a user password for token issuance.
        /// </summary>
        public const string HeaderPassword = "x-password";

        /// <summary>
        /// Header carrying a credential access key.
        /// </summary>
        public const string HeaderAccessKey = "x-access-key";

        /// <summary>
        /// Header carrying a credential secret key.
        /// </summary>
        public const string HeaderSecretKey = "x-secret-key";

        /// <summary>
        /// Header carrying the signed-request timestamp.
        /// </summary>
        public const string HeaderDate = "x-date";

        /// <summary>
        /// Header carrying the signed-request nonce.
        /// </summary>
        public const string HeaderNonce = "x-nonce";

        /// <summary>
        /// Header carrying the signed-request signature.
        /// </summary>
        public const string HeaderSignature = "x-signature";

        /// <summary>
        /// Header carrying a request identifier.
        /// </summary>
        public const string HeaderRequestId = "x-request-id";

        /// <summary>
        /// Header selecting an agent profile.
        /// </summary>
        public const string HeaderProfile = "x-hugo-profile";

        /// <summary>
        /// Header selecting a conversation.
        /// </summary>
        public const string HeaderConversation = "x-hugo-conversation";

        #endregion

        #region Key-Derivation

        /// <summary>
        /// HKDF info string for the secrets vault data key.
        /// </summary>
        public const string KeyPurposeSecrets = "hugo-secrets-v1";

        /// <summary>
        /// HKDF info string for the session token key.
        /// </summary>
        public const string KeyPurposeSessions = "hugo-sessions-v1";

        /// <summary>
        /// HKDF info string for the credential secret key.
        /// </summary>
        public const string KeyPurposeCredentials = "hugo-credentials-v1";

        #endregion
    }
}
