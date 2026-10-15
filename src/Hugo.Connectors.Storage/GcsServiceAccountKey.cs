namespace Hugo.Connectors.Storage
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The fields of a Google Cloud service account key file that Hugo checks during validation. Never log instances.
    /// </summary>
    public class GcsServiceAccountKey
    {
        #region Public-Members

        /// <summary>
        /// Key type; must be <c>service_account</c>.
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = null;

        /// <summary>
        /// Project identifier.
        /// </summary>
        [JsonPropertyName("project_id")]
        public string? ProjectId { get; set; } = null;

        /// <summary>
        /// Service account email.
        /// </summary>
        [JsonPropertyName("client_email")]
        public string? ClientEmail { get; set; } = null;

        /// <summary>
        /// PEM private key.
        /// </summary>
        [JsonPropertyName("private_key")]
        public string? PrivateKey { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse key JSON.
        /// </summary>
        /// <param name="json">JSON text.</param>
        /// <returns>The key, or null when the text is not a JSON object.</returns>
        public static GcsServiceAccountKey? TryParse(string? json)
        {
            if (String.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonSerializer.Deserialize<GcsServiceAccountKey>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        #endregion
    }
}
