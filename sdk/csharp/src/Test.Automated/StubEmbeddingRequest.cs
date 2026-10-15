namespace Test.Automated
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// An OpenAI embeddings request with a list of inputs (the stub also accepts a single string input).
    /// </summary>
    public class StubEmbeddingRequest
    {
        #region Public-Members

        /// <summary>
        /// Inputs to embed.
        /// </summary>
        [JsonPropertyName("input")]
        public List<string> Input
        {
            get
            {
                return _Input;
            }
            set
            {
                _Input = value ?? new List<string>();
            }
        }

        #endregion

        #region Private-Members

        private List<string> _Input = new List<string>();

        #endregion
    }
}
