namespace Hugo.Connectors.Cryptography
{
    using System;

    /// <summary>Typed cryptographic output contract.</summary>
    public class CryptoResponse
    {
        /// <summary>Applied cipher or hash algorithm.</summary>
        public string Algorithm { get; set; } = String.Empty;

        /// <summary>Encoded result data; empty for verification.</summary>
        public string Data { get; set; } = String.Empty;

        /// <summary>Encoding of data, nonce, and tag.</summary>
        public string Encoding { get; set; } = String.Empty;

        /// <summary>Separate random GCM nonce or CBC IV.</summary>
        public string? Nonce { get; set; } = null;

        /// <summary>Separate GCM authentication tag.</summary>
        public string? Tag { get; set; } = null;

        /// <summary>Whether the expected digest matches, or null for other operations.</summary>
        public bool? Matches { get; set; } = null;

        /// <summary>Security limitation of an explicitly selected legacy cipher.</summary>
        public string? Warning { get; set; } = null;

    }
}
