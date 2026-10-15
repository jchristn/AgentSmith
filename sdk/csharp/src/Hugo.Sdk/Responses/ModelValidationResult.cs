namespace Hugo.Sdk.Responses
{
    using System.Collections.Generic;

    /// <summary>
    /// Result of a model endpoint validation.
    /// </summary>
    public class ModelValidationResult
    {
        #region Public-Members

        /// <summary>
        /// Success.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// Kind.
        /// </summary>
        public string? Kind { get; set; } = null;

        /// <summary>
        /// Latency milliseconds.
        /// </summary>
        public long LatencyMs { get; set; } = 0;

        /// <summary>
        /// Dimensions.
        /// </summary>
        public int? Dimensions { get; set; } = null;

        /// <summary>
        /// Model.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Response preview.
        /// </summary>
        public string? ResponsePreview { get; set; } = null;

        /// <summary>
        /// Status code.
        /// </summary>
        public int? StatusCode { get; set; } = null;

        /// <summary>
        /// Error.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Problems.
        /// </summary>
        public List<string> Problems { get; set; } = new List<string>();

        #endregion
    }
}
