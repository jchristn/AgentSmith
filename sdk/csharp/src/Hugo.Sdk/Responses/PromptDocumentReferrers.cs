namespace Hugo.Sdk.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Sdk.Models;

    /// <summary>PromptDocumentReferrers contract for reusable agent documents. Version tokens are independent of revision numbers.</summary>
    public class PromptDocumentReferrers
    {
        /// <summary>Items.</summary>
        public List<PromptDocumentReferrer> Items { get; set; } = new List<PromptDocumentReferrer>();

        /// <summary>HasOtherReferrers.</summary>
        public bool HasOtherReferrers { get; set; } = false;

    }
}
