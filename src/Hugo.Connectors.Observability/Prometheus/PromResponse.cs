namespace Hugo.Connectors.Observability
{
    using System.Collections.Generic;

    /// <summary>
    /// Envelope of the Prometheus HTTP API (also used by Loki): <c>{"status":"success","data":...,"warnings":[...]}</c>.
    /// </summary>
    /// <typeparam name="T">Type of <see cref="Data"/>.</typeparam>
    public class PromResponse<T> where T : class
    {
        /// <summary>
        /// Status, <c>success</c> or <c>error</c>.
        /// </summary>
        public string? Status { get; set; } = null;

        /// <summary>
        /// Data.
        /// </summary>
        public T? Data { get; set; } = null;

        /// <summary>
        /// Error type when the status is <c>error</c>.
        /// </summary>
        public string? ErrorType { get; set; } = null;

        /// <summary>
        /// Error message when the status is <c>error</c>.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Warnings reported with a successful response.
        /// </summary>
        public List<string>? Warnings { get; set; } = null;
    }
}
