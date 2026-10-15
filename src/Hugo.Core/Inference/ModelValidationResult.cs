namespace Hugo.Core.Inference
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Outcome of validating a model endpoint (P6.5): a tiny prompt for inference endpoints, or a short string
    /// embedded for embedding endpoints.
    /// </summary>
    public class ModelValidationResult
    {
        #region Public-Members

        /// <summary>
        /// Whether the endpoint answered successfully.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// Endpoint kind that was validated.
        /// </summary>
        public ModelKindEnum Kind { get; set; } = ModelKindEnum.Inference;

        /// <summary>
        /// Round-trip latency in milliseconds (0 when the call was not attempted).
        /// </summary>
        public long LatencyMs { get; set; } = 0;

        /// <summary>
        /// Embedding dimensions, for embedding endpoints that answered.
        /// </summary>
        public int? Dimensions { get; set; } = null;

        /// <summary>
        /// Model reported by the provider, or the configured model.
        /// </summary>
        public string Model { get; set; } = String.Empty;

        /// <summary>
        /// First characters of the reply, for inference endpoints.
        /// </summary>
        public string? ResponsePreview { get; set; } = null;

        /// <summary>
        /// HTTP status when the endpoint answered with an error.
        /// </summary>
        public int? StatusCode { get; set; } = null;

        /// <summary>
        /// Failure description (never contains the API key), or null on success.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Configuration problems found before contacting the endpoint. Never null.
        /// </summary>
        public List<string> Problems
        {
            get
            {
                return _Problems;
            }
            set
            {
                _Problems = value ?? new List<string>();
            }
        }

        #endregion

        #region Private-Members

        private List<string> _Problems = new List<string>();

        #endregion
    }
}
