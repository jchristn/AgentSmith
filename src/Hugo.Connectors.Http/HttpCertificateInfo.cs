namespace Hugo.Connectors.Http
{
    using System;

    /// <summary>
    /// The TLS certificate a server presented, as reported by <c>http_health_check</c>.
    /// </summary>
    public class HttpCertificateInfo
    {
        /// <summary>
        /// Host and port the certificate was presented for.
        /// </summary>
        public string Endpoint { get; set; } = String.Empty;

        /// <summary>
        /// Subject distinguished name.
        /// </summary>
        public string Subject { get; set; } = String.Empty;

        /// <summary>
        /// Issuer distinguished name.
        /// </summary>
        public string Issuer { get; set; } = String.Empty;

        /// <summary>
        /// Start of validity (UTC).
        /// </summary>
        public DateTime NotBefore { get; set; } = DateTime.MinValue;

        /// <summary>
        /// End of validity (UTC).
        /// </summary>
        public DateTime NotAfter { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Whole days until <see cref="NotAfter"/>; negative when expired.
        /// </summary>
        public int DaysRemaining { get; set; } = 0;

        /// <summary>
        /// SHA-256 thumbprint in hex.
        /// </summary>
        public string Sha256Thumbprint { get; set; } = String.Empty;

        /// <summary>
        /// Whether the platform validated the certificate for the host without errors.
        /// </summary>
        public bool Valid { get; set; } = false;

        /// <summary>
        /// Validation errors (for example <c>RemoteCertificateChainErrors</c>), or <c>None</c>.
        /// </summary>
        public string Errors { get; set; } = "None";
    }
}
