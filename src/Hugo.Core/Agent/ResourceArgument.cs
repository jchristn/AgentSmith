namespace Hugo.Core.Agent
{
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The <c>resource</c> argument shared by every family tool (D-017), read from a model's arguments for events.
    /// </summary>
    public class ResourceArgument
    {
        #region Public-Members

        /// <summary>
        /// Resource alias.
        /// </summary>
        [JsonPropertyName("resource")]
        public string? Resource { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read the resource alias from arguments JSON. Returns null when the JSON is invalid, not an object, or the
        /// value is not a string.
        /// </summary>
        /// <param name="argumentsJson">Arguments JSON.</param>
        /// <returns>Alias or null.</returns>
        public static string? Read(string? argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson)) return null;
            try
            {
                ResourceArgument? parsed = JsonSerializer.Deserialize<ResourceArgument>(argumentsJson);
                return string.IsNullOrWhiteSpace(parsed?.Resource) ? null : parsed.Resource;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        #endregion
    }
}
