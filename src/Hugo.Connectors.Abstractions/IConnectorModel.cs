namespace Hugo.Connectors.Abstractions
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Access to a language model for connectors that need one (for example to generate durable context or to translate
    /// a question into a query). The server binds it to the calling profile's inference endpoint, meters it, and records
    /// it in telemetry. Prompts must not contain secrets. Implementations are thread-safe.
    /// </summary>
    public interface IConnectorModel
    {
        /// <summary>
        /// Complete a single-turn request.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Response.</returns>
        /// <exception cref="ConnectorException">Thrown with code <c>upstream_error</c>, <c>rate_limited</c>, or <c>timeout</c> when the model call fails.</exception>
        Task<ConnectorModelResponse> CompleteAsync(ConnectorModelRequest request, CancellationToken token = default);
    }
}
