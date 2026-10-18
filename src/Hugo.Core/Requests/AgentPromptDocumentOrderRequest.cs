namespace Hugo.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;

    /// <summary>AgentPromptDocumentOrderRequest contract for managed profile documents.</summary>
    [System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
    public class AgentPromptDocumentOrderRequest
    {
        /// <summary>ExpectedDocumentSetVersion; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public long? ExpectedDocumentSetVersion { get; set; } = null;

        /// <summary>AttachmentIds; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public List<string> AttachmentIds { get; set; } = new List<string>();

    }
}
