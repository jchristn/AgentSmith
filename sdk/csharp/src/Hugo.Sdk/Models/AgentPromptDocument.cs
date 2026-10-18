namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>AgentPromptDocument contract for reusable agent documents. Version tokens are independent of revision numbers.</summary>
    public class AgentPromptDocument
    {
        /// <summary>Id.</summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>Owning tenant.</summary>
        public string TenantId { get; set; } = String.Empty;

        /// <summary>Selected revision, matching Revision.</summary>
        public int ResolvedRevision { get; set; } = 0;

        /// <summary>Creation time in UTC.</summary>
        public DateTime CreatedUtc { get; set; } = default;

        /// <summary>Latest mutation time in UTC.</summary>
        public DateTime LastUpdateUtc { get; set; } = default;

        /// <summary>AgentProfileId.</summary>
        public string AgentProfileId { get; set; } = String.Empty;

        /// <summary>DocumentId.</summary>
        public string DocumentId { get; set; } = String.Empty;

        /// <summary>DeliveryMode.</summary>
        public string DeliveryMode { get; set; } = "Reference";

        /// <summary>RevisionPolicy.</summary>
        public string RevisionPolicy { get; set; } = "Latest";

        /// <summary>PinnedRevision.</summary>
        public int? PinnedRevision { get; set; } = null;

        /// <summary>DisplayOrder.</summary>
        public int DisplayOrder { get; set; } = 0;

        /// <summary>Enabled.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>AllowPublicAccess.</summary>
        public bool AllowPublicAccess { get; set; } = false;

        /// <summary>Version.</summary>
        public long Version { get; set; } = 1;

        /// <summary>DocumentSetVersion.</summary>
        public long DocumentSetVersion { get; set; } = 0;

        /// <summary>Name.</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>Description.</summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>Revision.</summary>
        public int Revision { get; set; } = 0;

        /// <summary>ContentBytes.</summary>
        public long ContentBytes { get; set; } = 0;

        /// <summary>ContentHash.</summary>
        public string ContentHash { get; set; } = String.Empty;

    }
}
