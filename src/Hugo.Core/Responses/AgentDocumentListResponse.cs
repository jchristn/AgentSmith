namespace Hugo.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;

    /// <summary>AgentDocumentListResponse contract for managed profile documents.</summary>
    public class AgentDocumentListResponse
    {
        /// <summary>DocumentSetVersion; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public long DocumentSetVersion { get; set; } = 0;

        /// <summary>Items; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public List<AgentPromptDocumentView> Items { get; set; } = new List<AgentPromptDocumentView>();

        /// <summary>NextCursor; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public string? NextCursor { get; set; } = null;

    }
}
