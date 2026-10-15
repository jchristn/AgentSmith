namespace Hugo.Sdk.Requests
{
    using System;

    /// <summary>
    /// Describe uploaded key material without storing it.
    /// </summary>
    public class SecretParseRequest
    {
        #region Public-Members

        /// <summary>
        /// File content (SSH key, service account JSON, kubeconfig, or PEM certificate).
        /// </summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>
        /// Passphrase.
        /// </summary>
        public string? Passphrase { get; set; } = null;

        /// <summary>
        /// File name.
        /// </summary>
        public string? FileName { get; set; } = null;

        #endregion
    }
}
