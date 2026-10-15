namespace Hugo.Connectors.Abstractions
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A service family Hugo can connect to. One implementation per connector type. Implementations must be thread-safe
    /// and must have a public parameterless constructor so plugins can be discovered.
    /// </summary>
    public interface IConnector
    {
        /// <summary>
        /// Static description.
        /// </summary>
        ConnectorDescriptor Descriptor { get; }

        /// <summary>
        /// Tools. Stable for the life of the process.
        /// </summary>
        IReadOnlyList<IConnectorTool> Tools { get; }

        /// <summary>
        /// Validate settings and secret slots beyond what the schema expresses (cross-field rules). The server has
        /// already checked required values, kinds, ranges, enums, and patterns.
        /// </summary>
        /// <param name="resource">Resource to validate.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Validation result.</returns>
        Task<ConnectorValidationResult> ValidateAsync(ConnectorResourceContext resource, CancellationToken token = default);

        /// <summary>
        /// Open a connection and run a cheap, read-only probe.
        /// </summary>
        /// <param name="resource">Resource to test.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Health result. Never throws for connectivity failures.</returns>
        Task<ConnectorHealthResult> TestConnectionAsync(ConnectorResourceContext resource, CancellationToken token = default);
    }
}
