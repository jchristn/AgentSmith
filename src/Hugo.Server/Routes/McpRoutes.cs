namespace Hugo.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using Hugo.Server.Protocols;
    using WatsonWebserver.Core;

    /// <summary>
    /// MCP routes (P7.1): POST, GET, and DELETE on /mcp and /mcp/{profileName}, relayed by <see cref="McpProxy"/> to the
    /// internal Voltaic endpoint. They are registered before authentication because the proxy authenticates with the
    /// same schemes as REST itself and answers failures with 401 and a bearer challenge, as MCP clients expect.
    /// </summary>
    public class McpRoutes
    {
        #region Private-Members

        private readonly HugoMcpServer _Server;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="server">MCP server.</param>
        /// <exception cref="ArgumentNullException">Thrown when server is null.</exception>
        public McpRoutes(HugoMcpServer server)
        {
            _Server = server ?? throw new ArgumentNullException(nameof(server));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="routes">Registrar.</param>
        /// <exception cref="ArgumentNullException">Thrown when routes is null.</exception>
        public void Register(RouteRegistrar routes)
        {
            if (routes == null) throw new ArgumentNullException(nameof(routes));
            string auth = " (authenticated by the MCP proxy with the REST schemes; 401 with a bearer challenge on failure)";
            routes.Anonymous(HttpMethod.POST, "/mcp", DefaultAsync, "MCP Streamable HTTP endpoint for the caller's resolved profile (D-018)" + auth, "MCP");
            routes.Anonymous(HttpMethod.GET, "/mcp", DefaultAsync, "MCP server-to-client event stream of a session" + auth, "MCP");
            routes.Anonymous(HttpMethod.DELETE, "/mcp", DefaultAsync, "End an MCP session" + auth, "MCP");
            routes.Anonymous(HttpMethod.POST, "/mcp/{profileName}", ProfileAsync, "MCP Streamable HTTP endpoint for a named profile" + auth, "MCP");
            routes.Anonymous(HttpMethod.GET, "/mcp/{profileName}", ProfileAsync, "MCP server-to-client event stream of a session on a named profile" + auth, "MCP");
            routes.Anonymous(HttpMethod.DELETE, "/mcp/{profileName}", ProfileAsync, "End an MCP session on a named profile" + auth, "MCP");
        }

        #endregion

        #region Private-Methods

        private async Task DefaultAsync(HttpContextBase http)
        {
            await RelayAsync(http, null).ConfigureAwait(false);
        }

        private async Task ProfileAsync(HttpContextBase http)
        {
            await RelayAsync(http, RouteHelpers.Param(http, "profileName")).ConfigureAwait(false);
        }

        private async Task RelayAsync(HttpContextBase http, string? profile)
        {
            McpProxy? proxy = _Server.Proxy;
            if (proxy == null)
            {
                await RouteHelpers.ErrorAsync(http, 404, "McpDisabled", "The MCP surface is disabled or not started on this server.").ConfigureAwait(false);
                return;
            }
            await proxy.HandleAsync(http, profile).ConfigureAwait(false);
        }

        #endregion
    }
}
