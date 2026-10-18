namespace Hugo.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;

    /// <summary>AgentPromptDocumentList contract for managed profile documents.</summary>
    public class AgentPromptDocumentList
    {
        /// <summary>DocumentSetVersion; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public long DocumentSetVersion { get; set; } = 0;

        /// <summary>Items; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public List<AgentPromptDocumentView> Items { get; set; } = new List<AgentPromptDocumentView>();

    }
}
