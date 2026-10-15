namespace Hugo.Sdk.Requests
{
    using System;

    /// <summary>
    /// Model endpoint list filter.
    /// </summary>
    public class ModelEndpointFilter
    {
        #region Public-Members

        /// <summary>
        /// Inference or Embedding.
        /// </summary>
        public string? Kind { get; set; } = null;

        /// <summary>
        /// Provider, for example OpenAICompatible or Ollama.
        /// </summary>
        public string? Provider { get; set; } = null;

        /// <summary>
        /// Healthy, Unhealthy, or Unknown.
        /// </summary>
        public string? Health { get; set; } = null;

        #endregion
    }
}
