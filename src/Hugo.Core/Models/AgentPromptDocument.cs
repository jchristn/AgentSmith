namespace Hugo.Core.Models
{
    using System;
    using Hugo.Core.Enums;
    using Hugo.Core.Helpers;

    /// <summary>AgentPromptDocument persistence record. Not thread-safe.</summary>
    public class AgentPromptDocument
    {
        /// <summary>Attachment identifier.</summary>
        public string Id { get; set; } = IdGenerator.NewAgentPromptDocumentId();

        /// <summary>Owning tenant.</summary>
        public string TenantId { get; set; } = String.Empty;

        /// <summary>Agent profile identifier.</summary>
        public string AgentProfileId { get; set; } = String.Empty;

        /// <summary>Library document identifier.</summary>
        public string DocumentId { get; set; } = String.Empty;

        /// <summary>Inline or Reference. Default Reference.</summary>
        public PromptDocumentDeliveryModeEnum DeliveryMode { get; set; } = PromptDocumentDeliveryModeEnum.Reference;

        /// <summary>Latest or Pinned. Default Latest.</summary>
        public PromptDocumentRevisionPolicyEnum RevisionPolicy { get; set; } = PromptDocumentRevisionPolicyEnum.Latest;

        /// <summary>Required immutable revision for Pinned; otherwise null.</summary>
        public int? PinnedRevision { get; set; } = null;

        /// <summary>Stable display order.</summary>
        public int DisplayOrder { get; set; } = 0;

        /// <summary>Whether delivery is enabled.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Explicit public delivery opt-in, default false.</summary>
        public bool AllowPublicAccess { get; set; } = false;

        /// <summary>Attachment concurrency version.</summary>
        public long Version { get; set; } = 1;

        /// <summary>Creation time in UTC.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Last mutation time in UTC.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

    }
}
