namespace Test.Plugin
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// A connector shipped as a plugin: type plugin-echo, family plugecho, one Read tool plugecho_read.
    /// </summary>
    public class PluginEchoConnector : IConnector
    {
        /// <inheritdoc />
        public ConnectorDescriptor Descriptor { get; }

        /// <inheritdoc />
        public IReadOnlyList<IConnectorTool> Tools { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PluginEchoConnector()
        {
            ConnectorDescriptor d = new ConnectorDescriptor();
            d.Type = "plugin-echo";
            d.Family = "plugecho";
            d.DisplayName = "Plugin echo";
            d.Description = "A connector loaded from the plugin directory.";
            d.DefaultInstructions = "Plugin echo resources echo what you send.";
            Descriptor = d;
            Tools = new List<IConnectorTool> { new PluginEchoTool() };
        }

        /// <inheritdoc />
        public Task<ConnectorValidationResult> ValidateAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            return Task.FromResult(ConnectorValidationResult.Valid());
        }

        /// <inheritdoc />
        public Task<ConnectorHealthResult> TestConnectionAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            return Task.FromResult(ConnectorHealthResult.Ok("plugin ok", 1));
        }
    }
}
