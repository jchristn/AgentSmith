namespace Hugo.Core.Instructions
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Profiles;

    /// <summary>
    /// Supplies the third party's own guidance for a bound resource (an external MCP server's instructions, a remote
    /// agent's card description and skills) to the <see cref="InstructionCompiler"/>, which fences it as untrusted
    /// content inside that binding's section (P13.28, plan section 9.2). The source is connector-agnostic: the host
    /// decides which connectors offer such text. Implementations must be thread-safe.
    /// </summary>
    public interface IThirdPartyInstructionSource
    {
        /// <summary>
        /// The third party's guidance for a bound resource, already passed through the secret scrubber and not yet fenced.
        /// Implementations answer from cached data when they can and otherwise wait only briefly, so compilation is never
        /// held up by a slow or unreachable remote; they return null instead of throwing when the text is unavailable.
        /// </summary>
        /// <param name="item">Bound resource.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Scrubbed, unfenced remote text, or null when there is none or it is not available yet.</returns>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        Task<string?> GetAsync(BoundResource item, CancellationToken token = default);
    }
}
