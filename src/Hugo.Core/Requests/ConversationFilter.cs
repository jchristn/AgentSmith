namespace Hugo.Core.Requests
{
    using System;
    using Hugo.Core.Enums;

    /// <summary>
    /// Filters for listing conversations. Null fields do not filter. The visibility rule (own conversations unless the
    /// caller is a tenant administrator, P6.24) is applied by setting <see cref="UserId"/> or <see cref="CredentialId"/>.
    /// </summary>
    public class ConversationFilter
    {
        /// <summary>
        /// Owning user.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Starting credential.
        /// </summary>
        public string? CredentialId { get; set; } = null;

        /// <summary>
        /// Agent profile.
        /// </summary>
        public string? AgentProfileId { get; set; } = null;

        /// <summary>
        /// Source surface.
        /// </summary>
        public ConversationSourceEnum? Source { get; set; } = null;
    }
}
