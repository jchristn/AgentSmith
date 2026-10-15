namespace Hugo.Core.Models
{
    using System;
    using Hugo.Core.Enums;
    using Hugo.Core.Helpers;

    /// <summary>
    /// A conversation between a principal and an agent profile (conversations, plan 7.5 and 13.4). Dashboard chat, A2A
    /// contexts, OpenAI-compatible sessions, and delegated asks all map onto one. Not thread-safe.
    /// </summary>
    public class Conversation
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix cnv_).
        /// </summary>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                _Id = String.IsNullOrWhiteSpace(value) ? IdGenerator.New(Constants.ConversationIdPrefix) : value;
            }
        }

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string TenantId
        {
            get
            {
                return _TenantId;
            }
            set
            {
                _TenantId = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Owning user: the signed-in user, or the owner of the credential that started the conversation. Null for a
        /// credential without an owner.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Credential that started the conversation, when a credential (not a session) was used.
        /// </summary>
        public string? CredentialId { get; set; } = null;

        /// <summary>
        /// Agent profile the conversation talks to.
        /// </summary>
        public string AgentProfileId
        {
            get
            {
                return _AgentProfileId;
            }
            set
            {
                _AgentProfileId = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Title. Null until set by the caller or generated after the first exchange (P6.16). At most 256 characters.
        /// </summary>
        public string? Title
        {
            get
            {
                return _Title;
            }
            set
            {
                _Title = value == null ? null : (value.Length > 256 ? value.Substring(0, 256) : value);
            }
        }

        /// <summary>
        /// Surface that started the conversation.
        /// </summary>
        public ConversationSourceEnum Source { get; set; } = ConversationSourceEnum.Dashboard;

        /// <summary>
        /// A2A context identifier, for conversations started over A2A.
        /// </summary>
        public string? A2AContextId { get; set; } = null;

        /// <summary>
        /// Caller-chosen session key (the OpenAI-compatible <c>user</c> field), unique per tenant, profile, and principal.
        /// </summary>
        public string? ExternalKey { get; set; } = null;

        /// <summary>
        /// Messages stored (all roles, including compacted ones).
        /// </summary>
        public int MessageCount { get; set; } = 0;

        /// <summary>
        /// Time of the latest message (UTC), or null when there is none.
        /// </summary>
        public DateTime? LastMessageUtc { get; set; } = null;

        /// <summary>
        /// Input tokens used by every turn of the conversation.
        /// </summary>
        public long TotalInputTokens { get; set; } = 0;

        /// <summary>
        /// Output tokens used by every turn of the conversation.
        /// </summary>
        public long TotalOutputTokens { get; set; } = 0;

        /// <summary>
        /// Creation time (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update time (UTC).
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.New(Constants.ConversationIdPrefix);
        private string _TenantId = String.Empty;
        private string _AgentProfileId = String.Empty;
        private string? _Title = null;

        #endregion
    }
}
