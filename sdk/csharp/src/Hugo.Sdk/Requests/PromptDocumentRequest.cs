namespace Hugo.Sdk.Requests
{
    using System;
    using Hugo.Sdk.Models;

    /// <summary>PromptDocumentRequest contract for reusable agent documents. Version tokens are independent of revision numbers.</summary>
    public class PromptDocumentRequest
    {
        /// <summary>ExpectedVersion. Null request properties leave stored values unchanged.</summary>
        public long? ExpectedVersion { get; set; } = null;

        /// <summary>Name. Null request properties leave stored values unchanged.</summary>
        public string? Name { get; set; } = null;

        /// <summary>Description. Null request properties leave stored values unchanged.</summary>
        public string? Description { get; set; } = null;

        /// <summary>Markdown. Null request properties leave stored values unchanged.</summary>
        public string? Markdown { get; set; } = null;

        /// <summary>Visibility. Null request properties leave stored values unchanged.</summary>
        public string? Visibility { get; set; } = null;

        /// <summary>Active. Null request properties leave stored values unchanged.</summary>
        public bool? Active { get; set; } = null;

    }
}
