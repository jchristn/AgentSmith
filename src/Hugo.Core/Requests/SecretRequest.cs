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
        /// Name, unique per tenant; 1 to 256 characters. Required on create; null on update keeps the stored name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Description. Null on update keeps the stored description; an empty string clears it.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Kind. Null on create means Generic. The kind of an existing secret cannot change: null on update keeps it, and
        /// any other value must equal the stored kind.
        /// </summary>
        public SecretKindEnum? Kind { get; set; } = null;

        /// <summary>
        /// Field values by field name (create only; ignored by the metadata update). Accepted names depend on the kind.
        /// </summary>
        public Dictionary<string, string>? Values { get; set; } = null;

        /// <summary>
        /// Expiration (UTC), or null for none. The update replaces it, so null clears a stored expiry.
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;
    }
}
