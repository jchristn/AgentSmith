namespace Hugo.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;

    /// <summary>PromptDocumentReferrer contract for managed profile documents.</summary>
    public class PromptDocumentReferrer
    {
        /// <summary>ProfileId; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public string ProfileId { get; set; } = String.Empty;

        /// <summary>ProfileName; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public string ProfileName { get; set; } = String.Empty;

        /// <summary>AttachmentId; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public string AttachmentId { get; set; } = String.Empty;

        /// <summary>DeliveryMode; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public PromptDocumentDeliveryModeEnum DeliveryMode { get; set; } = PromptDocumentDeliveryModeEnum.Reference;

        /// <summary>RevisionPolicy; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public PromptDocumentRevisionPolicyEnum RevisionPolicy { get; set; } = PromptDocumentRevisionPolicyEnum.Latest;

        /// <summary>PinnedRevision; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public int? PinnedRevision { get; set; } = null;

        /// <summary>Enabled; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>AllowPublicAccess; versions are JSON-safe integers, omitted request fields preserve existing values.</summary>
        public bool AllowPublicAccess { get; set; } = false;

    }
}
