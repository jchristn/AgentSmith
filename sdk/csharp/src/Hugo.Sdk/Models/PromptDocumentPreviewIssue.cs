namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>PromptDocumentPreviewIssue contract for reusable agent documents. Version tokens are independent of revision numbers.</summary>
    public class PromptDocumentPreviewIssue
    {
        /// <summary>Code.</summary>
        public string Code { get; set; } = String.Empty;

        /// <summary>Message.</summary>
        public string Message { get; set; } = String.Empty;

        /// <summary>AttachmentId.</summary>
        public string? AttachmentId { get; set; } = null;

    }
}
