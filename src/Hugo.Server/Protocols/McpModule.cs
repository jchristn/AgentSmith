namespace Hugo.Server.Protocols
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Server.Modules;
    using Hugo.Server.Routes;

    /// <summary>
    /// The MCP surface (Phase 7): maps the tools, registers the meta tool provider, the /mcp routes, and starts the
    /// internal Voltaic endpoint. Runs after the integration module.
    /// </summary>
    public class McpModule : IServerModule
    {
        #region Public-Members

        /// <summary>
        /// Module name.
        /// </summary>
        public string Name
        {
            get
            {
                return "McpModule";
            }
        }

        /// <summary>
        /// The MCP server, or null before initialization.
        /// </summary>
        public HugoMcpServer? Server
        {
            get
            {
                return _Server;
            }
        }

        #endregion

        #region Private-Members

        private HugoMcpServer? _Server = null;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task InitializeAsync(ServerContext context, CancellationToken token)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            _Server = new HugoMcpServer(context);
            _Server.Initialize();
            context.Register(_Server);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void RegisterRoutes(ServerContext context, RouteRegistrar routes)
        {
            if (routes == null) throw new ArgumentNullException(nameof(routes));
            new McpRoutes(_Server ?? throw new InvalidOperationException("McpModule was not initialized.")).Register(routes);
        }

        /// <inheritdoc />
        public async Task StartAsync(ServerContext context, CancellationToken token)
        {
            if (_Server != null) await _Server.StartAsync(token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken token)
        {
            _Server?.Dispose();
            return Task.CompletedTask;
        }

        #endregion
    }
}
