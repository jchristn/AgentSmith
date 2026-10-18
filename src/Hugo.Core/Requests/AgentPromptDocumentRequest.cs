namespace Hugo.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;

    /// <summary>AgentPromptDocumentRequest contract for managed profile documents.</summary>
    [System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
    public class AgentPromptDocumentRequest
    {
        /// <summary>ExpectedVersion; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public long? ExpectedVersion { get; set; } = null;

        /// <summary>ExpectedDocumentSetVersion; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public long? ExpectedDocumentSetVersion { get; set; } = null;

        /// <summary>DocumentId; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public string? DocumentId { get; set; } = null;

        /// <summary>DeliveryMode; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public PromptDocumentDeliveryModeEnum? DeliveryMode { get; set; } = null;

        /// <summary>RevisionPolicy; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public PromptDocumentRevisionPolicyEnum? RevisionPolicy { get; set; } = null;

        /// <summary>PinnedRevision; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public int? PinnedRevision { get; set; } = null;

        /// <summary>DisplayOrder; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public int? DisplayOrder { get; set; } = null;

        /// <summary>Enabled; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public bool? Enabled { get; set; } = null;

        /// <summary>AllowPublicAccess; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public bool? AllowPublicAccess { get; set; } = null;

    }
}
