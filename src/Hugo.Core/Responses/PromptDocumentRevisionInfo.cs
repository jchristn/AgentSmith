namespace Hugo.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;

    /// <summary>PromptDocumentRevisionInfo contract for managed profile documents.</summary>
    public class PromptDocumentRevisionInfo
    {
        /// <summary>Id; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>DocumentId; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public string DocumentId { get; set; } = String.Empty;

        /// <summary>Revision; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public int Revision { get; set; } = 1;

        /// <summary>Name; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>Description; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>ContentBytes; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public int ContentBytes { get; set; } = 0;

        /// <summary>ContentHash; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public string ContentHash { get; set; } = String.Empty;

        /// <summary>CreatedByUserId; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public string? CreatedByUserId { get; set; } = null;

        /// <summary>CreatedUtc; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    }
}
