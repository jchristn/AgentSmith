namespace Hugo.Sdk.Requests
{
    using System;
    using System.Collections.Generic;
    using Hugo.Sdk.Models;

    /// <summary>AgentPromptDocumentOrderRequest contract for reusable agent documents. Version tokens are independent of revision numbers.</summary>
    public class AgentPromptDocumentOrderRequest
    {
        /// <summary>ExpectedDocumentSetVersion. Null request properties leave stored values unchanged.</summary>
        public long? ExpectedDocumentSetVersion { get; set; } = null;

        /// <summary>AttachmentIds. Null request properties leave stored values unchanged.</summary>
        public List<string> AttachmentIds { get; set; } = new List<string>();

    }
}
