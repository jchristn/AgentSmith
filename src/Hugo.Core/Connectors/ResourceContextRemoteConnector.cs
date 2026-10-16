namespace Hugo.Core.Connectors
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Preserves optional remote-instruction capability while adding shared resource context tools.
    /// Thread-safe when the wrapped connector is thread-safe.
    /// </summary>
    public sealed class ResourceContextRemoteConnector : ResourceContextConnector, IRemoteInstructionsProvider
    {
        private readonly IRemoteInstructionsProvider _Provider;

        /// <summary>
        /// Instantiate a wrapper for a connector that provides remote instructions.
        /// </summary>
        /// <param name="inner">Connector implementing the optional remote-instruction capability.</param>
        /// <exception cref="ArgumentNullException">Thrown when inner is null.</exception>
        /// <exception cref="ArgumentException">Thrown when inner does not provide remote instructions.</exception>
        public ResourceContextRemoteConnector(IConnector inner) : base(inner)
        {
            _Provider = inner as IRemoteInstructionsProvider ?? throw new ArgumentException("Connector must provide remote instructions.", nameof(inner));
        }

        /// <inheritdoc />
        public Task<string?> GetRemoteInstructionsAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            return _Provider.GetRemoteInstructionsAsync(resource, token);
        }
    }
}
