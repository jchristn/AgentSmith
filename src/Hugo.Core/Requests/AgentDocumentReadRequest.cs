namespace Hugo.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;

    /// <summary>AgentDocumentReadRequest contract for managed profile documents.</summary>
    [System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
    public class AgentDocumentReadRequest
    {
        /// <summary>DocumentId; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public string? DocumentId { get; set; } = null;

        /// <summary>Name; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public string? Name { get; set; } = null;

        /// <summary>Revision; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public int? Revision { get; set; } = null;

        /// <summary>Cursor; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public string? Cursor { get; set; } = null;

        /// <summary>MaxBytes; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public int? MaxBytes { get; set; } = null;

    }
}
