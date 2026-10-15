namespace Hugo.Core.Enums
{
    /// <summary>
    /// Audit event types (plan section 15.6).
    /// </summary>
    public enum AuditEventTypeEnum
    {
        /// <summary>
        /// Successful authentication (sampled).
        /// </summary>
        AuthSuccess,

        /// <summary>
        /// Failed authentication.
        /// </summary>
        AuthFailure,

        /// <summary>
        /// A session token was issued.
        /// </summary>
        SessionIssued,

        /// <summary>
        /// A session was revoked.
        /// </summary>
        SessionRevoked,

        /// <summary>
        /// A session was re-authenticated with the password.
        /// </summary>
        SessionReauthenticated,

        /// <summary>
        /// A credential was created.
        /// </summary>
        CredentialCreated,

        /// <summary>
        /// A credential secret was rotated.
        /// </summary>
        CredentialRotated,

        /// <summary>
        /// A credential was used (sampled).
        /// </summary>
        CredentialUsed,

        /// <summary>
        /// An authorization check denied a request.
        /// </summary>
        AuthorizationDenied,

        /// <summary>
        /// An authorization check was bypassed by an administrator flag.
        /// </summary>
        AuthorizationBypass,

        /// <summary>
        /// A role changed.
        /// </summary>
        RoleChanged,

        /// <summary>
        /// A permission or role permission map changed.
        /// </summary>
        PermissionChanged,

        /// <summary>
        /// A user role assignment or credential scope assignment changed.
        /// </summary>
        AssignmentChanged,

        /// <summary>
        /// A tenant, user, or other configuration record changed.
        /// </summary>
        ConfigurationChanged,

        /// <summary>
        /// A password was changed.
        /// </summary>
        PasswordChanged,

        /// <summary>
        /// A secret was created.
        /// </summary>
        SecretCreated,

        /// <summary>
        /// A secret's metadata or values changed.
        /// </summary>
        SecretUpdated,

        /// <summary>
        /// A secret's plaintext values were revealed.
        /// </summary>
        SecretRevealed,

        /// <summary>
        /// A secret was deleted.
        /// </summary>
        SecretDeleted,

        /// <summary>
        /// Secrets were re-encrypted under a new master key version.
        /// </summary>
        MasterKeyRotated,

        /// <summary>
        /// A prompt template changed.
        /// </summary>
        PromptTemplateChanged,

        /// <summary>
        /// Tenant lockdown changed.
        /// </summary>
        LockdownChanged,

        /// <summary>
        /// Server settings changed.
        /// </summary>
        SettingsChanged,

        /// <summary>
        /// A server restart was requested.
        /// </summary>
        RestartRequested,

        /// <summary>
        /// The server started after a restart.
        /// </summary>
        ServerRestarted,

        /// <summary>
        /// A resource, its settings, or its secret slot bindings changed.
        /// </summary>
        ResourceChanged,

        /// <summary>
        /// An agent profile changed.
        /// </summary>
        ProfileChanged,

        /// <summary>
        /// A binding, its tool rules, or its deny patterns changed.
        /// </summary>
        BindingChanged,

        /// <summary>
        /// An approval was approved, rejected, or cancelled.
        /// </summary>
        ApprovalDecided,

        /// <summary>
        /// Resource knowledge changed.
        /// </summary>
        KnowledgeChanged,

        /// <summary>
        /// A connector pinned an SSH host key on first use (D-028).
        /// </summary>
        HostKeyPinned,

        /// <summary>
        /// A connector saw a host key that does not match the pinned one.
        /// </summary>
        HostKeyMismatch,

        /// <summary>
        /// A model endpoint changed.
        /// </summary>
        ModelEndpointChanged,

        /// <summary>
        /// A connector plugin was loaded, refused, or unloaded.
        /// </summary>
        PluginLoaded,

        /// <summary>
        /// A connector learned and persisted a resource setting other than a host key.
        /// </summary>
        SettingLearned
    }
}
