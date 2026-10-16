namespace Hugo.Core.Agent
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Supplies fresh, authorized reference context immediately before each agent model call. Implementations must
    /// be thread-safe, fence untrusted data, and avoid caching mutable resource knowledge across calls.
    /// </summary>
    public interface IAgentContextProvider
    {
        /// <summary>
        /// Fetch the current context snapshot for this turn. The runtime includes it transiently in this model call,
        /// including forced final answers and compaction; it is never appended to conversation history.
        /// </summary>
        /// <param name="request">Authenticated turn request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Fresh context, or an empty string when disabled or unavailable by policy.</returns>
        Task<string> GetContextAsync(AgentTurnRequest request, CancellationToken token);
    }
}
