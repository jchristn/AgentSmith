namespace Hugo.Core.Models
{
    using System;
    using Hugo.Core.Enums;
    using Hugo.Core.Helpers;

    /// <summary>PromptDocumentRevision persistence record. Not thread-safe.</summary>
    public class PromptDocumentRevision
    {
        /// <summary>Immutable revision identifier.</summary>
        public string Id { get; set; } = IdGenerator.NewPromptDocumentRevisionId();

        /// <summary>Owning tenant.</summary>
        public string TenantId { get; set; } = String.Empty;

        /// <summary>Parent document.</summary>
        public string DocumentId { get; set; } = String.Empty;

        /// <summary>Immutable revision number, starting at 1.</summary>
        public int Revision { get; set; } = 1;

        /// <summary>Filename snapshot.</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>Description snapshot.</summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>Canonical UTF-8 Markdown, with LF newlines.</summary>
        public string Markdown { get; set; } = String.Empty;

        /// <summary>Canonical UTF-8 byte count.</summary>
        public int ContentBytes { get; set; } = 0;

        /// <summary>Lowercase SHA-256 digest of canonical UTF-8 content.</summary>
        public string ContentHash { get; set; } = String.Empty;

        /// <summary>Author identifier, when applicable.</summary>
        public string? CreatedByUserId { get; set; } = null;

        /// <summary>Creation time in UTC.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    }
}
