namespace Hugo.Sdk.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Sdk.Models;

    /// <summary>AgentPromptDocumentList contract for reusable agent documents. Version tokens are independent of revision numbers.</summary>
    public class AgentPromptDocumentList
    {
        /// <summary>DocumentSetVersion.</summary>
        public long DocumentSetVersion { get; set; } = 0;

        /// <summary>Items.</summary>
        public List<AgentPromptDocument> Items { get; set; } = new List<AgentPromptDocument>();

    }
}
