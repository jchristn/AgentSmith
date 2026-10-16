namespace Hugo.Core.Connectors
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Knowledge;

    /// <summary>
    /// Adds connector-independent durable context tools without changing connector identity or connection behavior.
    /// Thread-safe when the wrapped connector is thread-safe.
    /// </summary>
    public class ResourceContextConnector : IConnector
    {
        private readonly IConnector _Inner;

        /// <inheritdoc />
        public ConnectorDescriptor Descriptor => _Inner.Descriptor;

        /// <inheritdoc />
        public IReadOnlyList<IConnectorTool> Tools { get; }

        /// <summary>
        /// Decorate a connector. Resource context tool names are reserved by Hugo.
        /// </summary>
        /// <param name="inner">Connector.</param>
        /// <exception cref="ArgumentNullException">Thrown when inner is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown for reserved tool names.</exception>
        public ResourceContextConnector(IConnector inner)
        {
            _Inner = inner ?? throw new ArgumentNullException(nameof(inner));
            List<IConnectorTool> tools = (inner.Tools ?? Array.Empty<IConnectorTool>()).ToList();
            if (tools.Any(t => t.Descriptor.Name == "resource_get_context" || t.Descriptor.Name == "resource_update_context"))
                throw new InvalidOperationException("Connector declares a reserved resource context tool name.");
            tools.Add(new ResourceGetContextTool());
            tools.Add(new ResourceUpdateContextTool());
            Tools = tools.AsReadOnly();
        }

        /// <inheritdoc />
        public Task<ConnectorValidationResult> ValidateAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            return _Inner.ValidateAsync(resource, token);
        }

        /// <inheritdoc />
        public Task<ConnectorHealthResult> TestConnectionAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            return _Inner.TestConnectionAsync(resource, token);
        }
    }
}
