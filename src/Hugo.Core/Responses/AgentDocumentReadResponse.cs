namespace Hugo.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;

    /// <summary>AgentDocumentReadResponse contract for managed profile documents.</summary>
    public class AgentDocumentReadResponse
    {
        /// <summary>DocumentId; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public string DocumentId { get; set; } = String.Empty;

        /// <summary>Name; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>Revision; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public int Revision { get; set; } = 1;

        /// <summary>ContentHash; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public string ContentHash { get; set; } = String.Empty;

        /// <summary>Markdown; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public string Markdown { get; set; } = String.Empty;

        /// <summary>Complete; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public bool Complete { get; set; } = true;

        /// <summary>ReturnedBytes; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public int ReturnedBytes { get; set; } = 0;

        /// <summary>NextCursor; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public string? NextCursor { get; set; } = null;

    }
}
