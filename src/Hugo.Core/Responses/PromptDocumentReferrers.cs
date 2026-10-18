namespace Hugo.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;

    /// <summary>PromptDocumentReferrers contract for managed profile documents.</summary>
    public class PromptDocumentReferrers
    {
        /// <summary>Items; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public List<PromptDocumentReferrer> Items { get; set; } = new List<PromptDocumentReferrer>();

        /// <summary>HasOtherReferrers; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public bool HasOtherReferrers { get; set; } = false;

    }
}
