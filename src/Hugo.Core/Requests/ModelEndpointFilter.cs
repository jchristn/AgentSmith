namespace Hugo.Core.Requests
{
    using System;
    using Hugo.Core.Enums;
    using Hugo.Core.Inference;

    /// <summary>
    /// Filters for listing model endpoints. Null fields do not filter.
    /// </summary>
    public class ModelEndpointFilter
    {
        /// <summary>
        /// Endpoint kind (Inference or Embedding).
        /// </summary>
        public ModelKindEnum? Kind { get; set; } = null;

        /// <summary>
        /// Provider, for example Ollama or Anthropic.
        /// </summary>
        public ModelProviderEnum? Provider { get; set; } = null;

        /// <summary>
        /// Last recorded health (Unknown, Healthy, or Unhealthy).
        /// </summary>
        public ModelHealthStatusEnum? Health { get; set; } = null;
    }
}
