namespace Hugo.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;

    /// <summary>AgentPromptDocumentView contract for managed profile documents.</summary>
    public class AgentPromptDocumentView : AgentPromptDocument
    {
        /// <summary>Name; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>Description; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>Revision; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public int Revision { get; set; } = 1;

        /// <summary>ResolvedRevision; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public int ResolvedRevision { get; set; } = 1;

        /// <summary>ContentBytes; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public int ContentBytes { get; set; } = 0;

        /// <summary>ContentHash; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public string ContentHash { get; set; } = String.Empty;

        /// <summary>DocumentSetVersion; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public long DocumentSetVersion { get; set; } = 0;

    }
}
