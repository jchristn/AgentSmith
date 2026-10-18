namespace Hugo.Core.Models
{
    using System;
    using Hugo.Core.Enums;
    using Hugo.Core.Helpers;

    /// <summary>PromptDocument persistence record. Not thread-safe.</summary>
    public class PromptDocument
    {
        /// <summary>Immutable document identifier.</summary>
        public string Id { get; set; } = IdGenerator.NewPromptDocumentId();

        /// <summary>Owning tenant identifier.</summary>
        public string TenantId { get; set; } = String.Empty;

        /// <summary>Owning user identifier, when applicable.</summary>
        public string? OwnerUserId { get; set; } = null;

        /// <summary>Owner or tenant visibility. Default Private.</summary>
        public VisibilityEnum Visibility { get; set; } = VisibilityEnum.Private;

        /// <summary>Display filename, 1 through 128 characters.</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>NFC invariant uppercase name used for uniqueness.</summary>
        public string NormalizedName { get; set; } = String.Empty;

        /// <summary>Required retrieval description, 1 through 1024 characters.</summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>Whether the document is available.</summary>
        public bool Active { get; set; } = true;

        /// <summary>Current immutable revision, starting at 1.</summary>
        public int CurrentRevision { get; set; } = 1;

        /// <summary>Metadata concurrency version, starting at 1.</summary>
        public long Version { get; set; } = 1;

        /// <summary>Creation time in UTC.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Last mutation time in UTC.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

    }
}
