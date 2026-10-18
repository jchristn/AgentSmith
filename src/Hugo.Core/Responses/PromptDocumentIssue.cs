namespace Hugo.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;

    /// <summary>PromptDocumentIssue contract for managed profile documents.</summary>
    public class PromptDocumentIssue
    {
        /// <summary>Code; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public string Code { get; set; } = String.Empty;

        /// <summary>Message; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public string Message { get; set; } = String.Empty;

        /// <summary>AttachmentId; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public string? AttachmentId { get; set; } = null;

    }
}
