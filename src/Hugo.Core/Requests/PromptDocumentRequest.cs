namespace Hugo.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;

    /// <summary>PromptDocumentRequest contract for managed profile documents.</summary>
    [System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
    public class PromptDocumentRequest
    {
        /// <summary>ExpectedVersion; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public long? ExpectedVersion { get; set; } = null;

        /// <summary>Name; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public string? Name { get; set; } = null;

        /// <summary>Description; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public string? Description { get; set; } = null;

        /// <summary>Markdown; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public string? Markdown { get; set; } = null;

        /// <summary>Visibility; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public VisibilityEnum? Visibility { get; set; } = null;

        /// <summary>Active; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public bool? Active { get; set; } = null;

    }
}
