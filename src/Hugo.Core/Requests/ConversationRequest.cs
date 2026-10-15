namespace Hugo.Core.Requests
{
    using System;

    /// <summary>
    /// Create or update a conversation. On create, a null profile resolves per D-018 (credential, user default, tenant
    /// default). On update only the title changes.
    /// </summary>
    public class ConversationRequest
    {
        /// <summary>
        /// Agent profile identifier or name (create only).
        /// </summary>
        public string? AgentProfileId { get; set; } = null;

        /// <summary>
        /// Title, at most 256 characters. Null keeps the current title (or lets one be generated).
        /// </summary>
        public string? Title { get; set; } = null;
    }
}
