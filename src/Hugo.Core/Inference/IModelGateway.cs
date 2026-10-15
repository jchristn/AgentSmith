namespace Hugo.Core.Inference
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// The only way the agent runtime reaches a model (D-021). Applies the per-endpoint concurrency gate, retries
    /// with backoff (honoring Retry-After), the circuit breaker, and the fallback endpoint. Implementations must be
    /// thread-safe.
    /// </summary>
    public interface IModelGateway
    {
        /// <summary>
        /// Chat or tool chat, streaming when requested and supported.
        /// </summary>
        /// <param name="call">Call.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Aggregated result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when call is null.</exception>
        /// <exception cref="ModelEndpointException">Thrown when every endpoint failed.</exception>
        /// <exception cref="OperationCanceledException">Thrown when cancelled.</exception>
        Task<ModelChatResult> ChatAsync(ModelChatCall call, CancellationToken token);

        /// <summary>
        /// Embed texts.
        /// </summary>
        /// <param name="call">Call.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Vectors.</returns>
        /// <exception cref="ArgumentNullException">Thrown when call is null.</exception>
        /// <exception cref="ModelEndpointException">Thrown when every endpoint failed.</exception>
        /// <exception cref="OperationCanceledException">Thrown when cancelled.</exception>
        Task<ModelEmbedResult> EmbedAsync(ModelEmbedCall call, CancellationToken token);

        /// <summary>
        /// Validate an endpoint by sending a tiny prompt or embedding a short string. Bypasses the circuit breaker
        /// and retries, and never throws for endpoint failures.
        /// </summary>
        /// <param name="endpoint">Endpoint.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Validation result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when endpoint is null.</exception>
        /// <exception cref="OperationCanceledException">Thrown when cancelled.</exception>
        Task<ModelValidationResult> ValidateAsync(ModelEndpointConfig endpoint, CancellationToken token);
    }
}
