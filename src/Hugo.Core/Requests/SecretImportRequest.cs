namespace Hugo.Core.Requests
{
    using System;

    /// <summary>
    /// Parse an uploaded file (SSH private key, GCP service account JSON, kubeconfig, or PEM certificate and key) and
    /// return what it contains, without storing anything. The dashboard shows the result before the secret is saved.
    /// </summary>
    public class SecretImportRequest
    {
        /// <summary>
        /// File content as text (PEM, OpenSSH, JSON, or YAML). Required; at most 1 MiB.
        /// </summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>
        /// Optional passphrase for an encrypted private key, used only to validate it.
        /// </summary>
        public string? Passphrase { get; set; } = null;

        /// <summary>
        /// Optional file name, used as a hint for detection.
        /// </summary>
        public string? FileName { get; set; } = null;
    }
}
