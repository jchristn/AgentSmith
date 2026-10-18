namespace Hugo.Sdk.Requests
{
    using System;
    using Hugo.Sdk.Models;

    /// <summary>AgentPromptDocumentRequest contract for reusable agent documents. Version tokens are independent of revision numbers.</summary>
    public class AgentPromptDocumentRequest
    {
        /// <summary>ExpectedVersion. Null request properties leave stored values unchanged.</summary>
        public long? ExpectedVersion { get; set; } = null;

        /// <summary>ExpectedDocumentSetVersion. Null request properties leave stored values unchanged.</summary>
        public long? ExpectedDocumentSetVersion { get; set; } = null;

        /// <summary>DocumentId. Null request properties leave stored values unchanged.</summary>
        public string? DocumentId { get; set; } = null;

        /// <summary>DeliveryMode. Null request properties leave stored values unchanged.</summary>
        public string? DeliveryMode { get; set; } = null;

        /// <summary>RevisionPolicy. Null request properties leave stored values unchanged.</summary>
        public string? RevisionPolicy { get; set; } = null;

        /// <summary>PinnedRevision. Null request properties leave stored values unchanged.</summary>
        public int? PinnedRevision { get; set; } = null;

        /// <summary>DisplayOrder. Null request properties leave stored values unchanged.</summary>
        public int? DisplayOrder { get; set; } = null;

        /// <summary>Enabled. Null request properties leave stored values unchanged.</summary>
        public bool? Enabled { get; set; } = null;

        /// <summary>AllowPublicAccess. Null request properties leave stored values unchanged.</summary>
        public bool? AllowPublicAccess { get; set; } = null;

    }
}
