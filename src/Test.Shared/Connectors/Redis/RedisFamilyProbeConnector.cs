namespace Test.Shared.Connectors.Redis
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// A test connector of another type in an existing family, exposing given tools. Registering it next to the real
    /// connector proves the registry's family rule (same tool name, same schema).
    /// </summary>
    public class RedisFamilyProbeConnector : IConnector
    {
        /// <inheritdoc />
        public ConnectorDescriptor Descriptor { get; }

        /// <inheritdoc />
        public IReadOnlyList<IConnectorTool> Tools { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="type">Connector type.</param>
        /// <param name="family">Family.</param>
        /// <param name="tools">Tools.</param>
        /// <exception cref="ArgumentNullException">Thrown when tools is null.</exception>
        public RedisFamilyProbeConnector(string type, string family, IReadOnlyList<IConnectorTool> tools)
        {
            Descriptor = new ConnectorDescriptor { Type = type, Family = family, DisplayName = type };
            Tools = tools ?? throw new ArgumentNullException(nameof(tools));
        }

        /// <inheritdoc />
        public Task<ConnectorValidationResult> ValidateAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            return Task.FromResult(ConnectorValidationResult.Valid());
        }

        /// <inheritdoc />
        public Task<ConnectorHealthResult> TestConnectionAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            return Task.FromResult(ConnectorHealthResult.Ok("probe", 0));
        }
    }
}
