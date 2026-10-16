namespace Test.Shared.Telemetry
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>Fixed JSON contract for quoted and nested logging redaction coverage.</summary>
    public class LoggingJsonRecord
    {
        /// <summary>Password field.</summary>
        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;

        /// <summary>API key field.</summary>
        [JsonPropertyName("apiKey")]
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>Nested sensitive fields.</summary>
        [JsonPropertyName("nested")]
        public Dictionary<string, string> Nested { get; set; } = new Dictionary<string, string>();
    }
}
