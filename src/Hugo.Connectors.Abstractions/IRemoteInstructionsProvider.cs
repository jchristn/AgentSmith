namespace Hugo.Connectors.Abstractions
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Optional connector capability for resources that front a third party with its own guidance, such as an external
    /// MCP server's instructions or a remote agent's card. Hugo calls it when a caller asks for a resource's full
    /// guidance (hugo_describe_resource) and always wraps the returned text in an <see cref="UntrustedContent"/> fence, so
    /// the calling model can tell it apart from Hugo's own instructions. Compiled instructions never include this text.
    /// Implementations must be thread-safe.
    /// </summary>
    public interface IRemoteInstructionsProvider
    {
        /// <summary>
        /// Fetch the third party's own guidance for a resource, verbatim and unfenced.
        /// </summary>
        /// <param name="resource">Resource context (settings and secrets).</param>
        /// <param name="token">Cancellation token. Hugo cancels it after a bounded wait.</param>
        /// <returns>The remote text, or null when the remote side offers none.</returns>
        /// <exception cref="ConnectorException">Thrown when the remote side cannot be reached or answers with an error.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        Task<string?> GetRemoteInstructionsAsync(ConnectorResourceContext resource, CancellationToken token = default);
    }
}
