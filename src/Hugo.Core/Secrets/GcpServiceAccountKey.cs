namespace Hugo.Core.Secrets
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The fields of a Google Cloud service account key file that the importer reads.
    /// </summary>
    public class GcpServiceAccountKey
    {
        /// <summary>
        /// Key type; service_account for a service account key.
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = null;

        /// <summary>
        /// Project identifier.
        /// </summary>
        [JsonPropertyName("project_id")]
        public string? ProjectId { get; set; } = null;

        /// <summary>
        /// Private key identifier.
        /// </summary>
        [JsonPropertyName("private_key_id")]
        public string? PrivateKeyId { get; set; } = null;

        /// <summary>
        /// PEM private key.
        /// </summary>
        [JsonPropertyName("private_key")]
        public string? PrivateKey { get; set; } = null;

        /// <summary>
        /// Service account email.
        /// </summary>
        [JsonPropertyName("client_email")]
        public string? ClientEmail { get; set; } = null;

        /// <summary>
        /// Client identifier.
        /// </summary>
        [JsonPropertyName("client_id")]
        public string? ClientId { get; set; } = null;
    }
}
