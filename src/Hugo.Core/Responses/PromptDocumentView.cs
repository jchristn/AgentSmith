namespace Hugo.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;

    /// <summary>PromptDocumentView contract for managed profile documents.</summary>
    public class PromptDocumentView : PromptDocument
    {
        /// <summary>ContentBytes; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public int ContentBytes { get; set; } = 0;

        /// <summary>ContentHash; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public string ContentHash { get; set; } = String.Empty;

        /// <summary>CanRead; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public bool CanRead { get; set; } = false;

        /// <summary>CanUpdate; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public bool CanUpdate { get; set; } = false;

        /// <summary>CanDelete; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public bool CanDelete { get; set; } = false;

        /// <summary>CanAdmin; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public bool CanAdmin { get; set; } = false;

        /// <summary>HasReferrers; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public bool HasReferrers { get; set; } = false;

    }
}
