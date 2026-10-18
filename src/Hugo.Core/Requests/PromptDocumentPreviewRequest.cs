namespace Hugo.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;

    /// <summary>PromptDocumentPreviewRequest contract for managed profile documents.</summary>
    [System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
    public class PromptDocumentPreviewRequest
    {
        /// <summary>Audience; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public string Audience { get; set; } = "Authenticated";

    }
}
