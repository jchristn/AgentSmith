namespace Hugo.Connectors.Observability
{
    /// <summary>
    /// Error body of the Prometheus and Loki HTTP APIs: <c>{"status":"error","errorType":"bad_data","error":"..."}</c>.
    /// </summary>
    public class PromErrorBody
    {
        /// <summary>
        /// Status, <c>error</c> for failures.
        /// </summary>
        public string? Status { get; set; } = null;

        /// <summary>
        /// Error type (for example <c>bad_data</c>, <c>timeout</c>).
        /// </summary>
        public string? ErrorType { get; set; } = null;

        /// <summary>
        /// Error message.
        /// </summary>
        public string? Error { get; set; } = null;
    }
}
