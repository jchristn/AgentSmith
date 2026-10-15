namespace Hugo.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Create a secret, or update its metadata. Values are write-only and never returned.
    /// </summary>
    public class SecretRequest
    {
        /// <summary>
        /// Name, unique per tenant. Required; 1 to 256 characters.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Description, or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Kind. Default Generic. The kind of an existing secret cannot change.
        /// </summary>
        public SecretKindEnum Kind { get; set; } = SecretKindEnum.Generic;

        /// <summary>
        /// Field values by field name (create only; ignored by the metadata update). Accepted names depend on the kind.
        /// </summary>
        public Dictionary<string, string>? Values { get; set; } = null;

        /// <summary>
        /// Expiration (UTC), or null.
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;
    }
}
