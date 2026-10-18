namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>PromptDocument contract for reusable agent documents. Version tokens are independent of revision numbers.</summary>
    public class PromptDocument
    {
        /// <summary>Id.</summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>TenantId.</summary>
        public string TenantId { get; set; } = String.Empty;

        /// <summary>OwnerUserId.</summary>
        public string? OwnerUserId { get; set; } = null;

        /// <summary>Visibility.</summary>
        public string Visibility { get; set; } = "Private";

        /// <summary>Name.</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>Canonical name used for tenant uniqueness.</summary>
        public string NormalizedName { get; set; } = String.Empty;

        /// <summary>Description.</summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>Active.</summary>
        public bool Active { get; set; } = true;

        /// <summary>CurrentRevision.</summary>
        public int CurrentRevision { get; set; } = 1;

        /// <summary>Version.</summary>
        public long Version { get; set; } = 1;

        /// <summary>ContentBytes.</summary>
        public long ContentBytes { get; set; } = 0;

        /// <summary>ContentHash.</summary>
        public string ContentHash { get; set; } = String.Empty;

        /// <summary>CanRead.</summary>
        public bool CanRead { get; set; } = false;

        /// <summary>CanUpdate.</summary>
        public bool CanUpdate { get; set; } = false;

        /// <summary>CanDelete.</summary>
        public bool CanDelete { get; set; } = false;

        /// <summary>CanAdmin.</summary>
        public bool CanAdmin { get; set; } = false;

        /// <summary>HasReferrers.</summary>
        public bool HasReferrers { get; set; } = false;

        /// <summary>CreatedUtc.</summary>
        public DateTime CreatedUtc { get; set; } = default;

        /// <summary>LastUpdateUtc.</summary>
        public DateTime LastUpdateUtc { get; set; } = default;

    }
}
