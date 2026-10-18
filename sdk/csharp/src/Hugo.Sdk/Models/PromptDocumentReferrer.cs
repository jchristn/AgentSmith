namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>PromptDocumentReferrer contract for reusable agent documents. Version tokens are independent of revision numbers.</summary>
    public class PromptDocumentReferrer
    {
        /// <summary>ProfileId.</summary>
        public string ProfileId { get; set; } = String.Empty;

        /// <summary>ProfileName.</summary>
        public string ProfileName { get; set; } = String.Empty;

        /// <summary>AttachmentId.</summary>
        public string AttachmentId { get; set; } = String.Empty;

        /// <summary>DeliveryMode.</summary>
        public string DeliveryMode { get; set; } = String.Empty;

        /// <summary>RevisionPolicy.</summary>
        public string RevisionPolicy { get; set; } = String.Empty;

        /// <summary>PinnedRevision.</summary>
        public int? PinnedRevision { get; set; } = null;

        /// <summary>Enabled.</summary>
        public bool Enabled { get; set; } = false;

        /// <summary>AllowPublicAccess.</summary>
        public bool AllowPublicAccess { get; set; } = false;

    }
}
