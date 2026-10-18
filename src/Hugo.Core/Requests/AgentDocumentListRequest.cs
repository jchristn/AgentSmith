namespace Hugo.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;

    /// <summary>AgentDocumentListRequest contract for managed profile documents.</summary>
    [System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
    public class AgentDocumentListRequest
    {
        /// <summary>Cursor; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public string? Cursor { get; set; } = null;

    }
}
