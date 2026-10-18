namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>PromptDocumentRevision contract for reusable agent documents. Version tokens are independent of revision numbers.</summary>
    public class PromptDocumentRevision
    {
        /// <summary>Id.</summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>Tenant identifier on explicit revision reads; omitted by metadata lists.</summary>
        public string? TenantId { get; set; } = null;

        /// <summary>DocumentId.</summary>
        public string DocumentId { get; set; } = String.Empty;

        /// <summary>Revision.</summary>
        public int Revision { get; set; } = 1;

        /// <summary>Name.</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>Description.</summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>Markdown.</summary>
        public string? Markdown { get; set; } = null;

        /// <summary>ContentBytes.</summary>
        public long ContentBytes { get; set; } = 0;

        /// <summary>ContentHash.</summary>
        public string ContentHash { get; set; } = String.Empty;

        /// <summary>CreatedByUserId.</summary>
        public string? CreatedByUserId { get; set; } = null;

        /// <summary>CreatedUtc.</summary>
        public DateTime CreatedUtc { get; set; } = default;

    }
}
