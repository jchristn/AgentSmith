namespace Hugo.Sdk.Requests
{
    using System;
    using Hugo.Sdk.Models;

    /// <summary>PromptDocumentPreviewRequest contract for reusable agent documents. Version tokens are independent of revision numbers.</summary>
    public class PromptDocumentPreviewRequest
    {
        /// <summary>Audience. Null request properties leave stored values unchanged.</summary>
        public string Audience { get; set; } = "Authenticated";

    }
}
