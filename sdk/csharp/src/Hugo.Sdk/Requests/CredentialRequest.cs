namespace Hugo.Sdk.Requests
{
    using System;
    using Hugo.Sdk.Enums;
    using Hugo.Sdk.Models;

    /// <summary>
    /// Create or update a credential.
    /// </summary>
    public class CredentialRequest
    {
        #region Public-Members

        /// <summary>
        /// Name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Owning user; defaults to the caller.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Auth mode.
        /// </summary>
        public CredentialAuthModeEnum? AuthMode { get; set; } = null;

        /// <summary>
        /// Expires UTC.
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;

        /// <summary>
        /// Active.
        /// </summary>
        public bool? Active { get; set; } = null;

        /// <summary>
        /// Role to assign on create, for example a built-in role name.
        /// </summary>
        public string? RoleName { get; set; } = null;

        /// <summary>
        /// Agent profile the credential is bound to, or null.
        /// </summary>
        public string? AgentProfileId { get; set; } = null;

        /// <summary>
        /// Whether the credential may only use its bound agent profile.
        /// </summary>
        public bool? LockToProfile { get; set; } = null;

        /// <summary>
        /// Whether the credential carries its owner's administrator or tenant administrator bypass. Default false: a
        /// credential's access comes only from its role assignments. Only a signed-in tenant or system administrator
        /// can set it (403 BypassRequiresAdministrator otherwise); every change is audited as CredentialBypassChanged.
        /// </summary>
        public bool? InheritOwnerBypass { get; set; } = null;

        #endregion
    }
}
