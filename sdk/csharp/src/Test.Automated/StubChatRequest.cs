namespace Test.Automated
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The parts of an OpenAI chat completions request the stub model reads.
    /// </summary>
    public class StubChatRequest
    {
        #region Public-Members

        /// <summary>
        /// Model name.
        /// </summary>
        [JsonPropertyName("model")]
        public string Model { get; set; } = String.Empty;

        /// <summary>
        /// Whether the caller asked for a streamed response.
        /// </summary>
        [JsonPropertyName("stream")]
        public bool Stream { get; set; } = false;

        #endregion
    }
}
