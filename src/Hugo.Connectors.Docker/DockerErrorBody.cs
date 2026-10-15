namespace Hugo.Connectors.Docker
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The error body the Docker daemon returns (<c>{"message": "..."}</c>). Not thread-safe.
    /// </summary>
    public class DockerErrorBody
    {
        /// <summary>
        /// Error message, or null.
        /// </summary>
        [JsonPropertyName("message")]
        public string? Message { get; set; } = null;
    }
}
