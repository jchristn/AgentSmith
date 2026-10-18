namespace Hugo.Connectors.Cryptography
{
    using System;

    /// <summary>Typed cryptographic input contract.</summary>
    public class CryptoRequest
    {
        /// <summary>Input bytes encoded using inputEncoding.</summary>
        public string Data { get; set; } = String.Empty;

        /// <summary>Input encoding: base64, hex, or utf8 for plaintext and hashes.</summary>
        public string InputEncoding { get; set; } = "base64";

        /// <summary>Output encoding: base64, hex, or utf8 for decryption.</summary>
        public string OutputEncoding { get; set; } = "base64";

        /// <summary>Cipher envelope: separate or combined (nonce/IV, ciphertext, then GCM tag).</summary>
        public string Format { get; set; } = "separate";

        /// <summary>Separate GCM nonce or CBC IV, encoded using inputEncoding.</summary>
        public string? Nonce { get; set; } = null;

        /// <summary>Separate GCM authentication tag, encoded using inputEncoding.</summary>
        public string? Tag { get; set; } = null;

        /// <summary>Optional GCM associated data encoded using inputEncoding.</summary>
        public string? AssociatedData { get; set; } = null;

        /// <summary>Hash algorithm: SHA256, SHA384, or SHA512.</summary>
        public string HashAlgorithm { get; set; } = "SHA256";

        /// <summary>Expected digest encoded using outputEncoding for verification.</summary>
        public string? ExpectedHash { get; set; } = null;

    }
}
