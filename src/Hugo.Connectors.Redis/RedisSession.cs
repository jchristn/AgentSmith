namespace Hugo.Connectors.Redis
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Hugo.Connectors.Abstractions;
    using StackExchange.Redis;

    /// <summary>
    /// A pooled connection to one Redis resource: the multiplexer plus the options it was built from. StackExchange.Redis
    /// multiplexers are designed to be shared, so one session serves every concurrent call on the resource. Thread-safe.
    /// </summary>
    public class RedisSession : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// The multiplexer. Never null.
        /// </summary>
        public IConnectionMultiplexer Multiplexer { get; }

        /// <summary>
        /// Options the connection was built from. Never null.
        /// </summary>
        public RedisConnectionOptions Options { get; }

        /// <summary>
        /// Whether the server is a Redis Cluster.
        /// </summary>
        public bool IsCluster
        {
            get
            {
                return Multiplexer.GetServers().Any(s => s.ServerType == ServerType.Cluster);
            }
        }

        #endregion

        #region Private-Members

        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="multiplexer">Connected multiplexer. The session owns it.</param>
        /// <param name="options">Options.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public RedisSession(IConnectionMultiplexer multiplexer, RedisConnectionOptions options)
        {
            Multiplexer = multiplexer ?? throw new ArgumentNullException(nameof(multiplexer));
            Options = options ?? throw new ArgumentNullException(nameof(options));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The configured database.
        /// </summary>
        /// <returns>Database.</returns>
        public IDatabase Database()
        {
            return Multiplexer.GetDatabase(IsCluster ? 0 : Options.Database);
        }

        /// <summary>
        /// Database index commands run against (0 on a cluster).
        /// </summary>
        /// <returns>Index.</returns>
        public int DatabaseIndex()
        {
            return IsCluster ? 0 : Options.Database;
        }

        /// <summary>
        /// Connected primaries (one for standalone and Sentinel, every primary for a cluster), ordered by endpoint.
        /// </summary>
        /// <returns>Servers.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>connection_failed</c> when no primary is connected.</exception>
        public List<IServer> Primaries()
        {
            List<IServer> servers = Multiplexer.GetServers()
                .Where(s => s.IsConnected && !s.IsReplica && s.ServerType != ServerType.Sentinel)
                .OrderBy(s => Format(s), StringComparer.Ordinal)
                .ToList();
            if (!servers.Any()) throw new ConnectorException(ConnectorErrorCodes.ConnectionFailed, "No connected Redis primary is available at " + Options.EndpointsText() + ".");
            return servers;
        }

        /// <summary>
        /// Servers a server-level tool reports on: the one named by <paramref name="node"/>, or every connected primary.
        /// </summary>
        /// <param name="node">Endpoint as <c>host:port</c>, or null.</param>
        /// <returns>Servers.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_argument</c> when the node is unknown.</exception>
        public List<IServer> SelectServers(string? node)
        {
            if (String.IsNullOrWhiteSpace(node)) return Primaries();
            string wanted = node.Trim();
            IServer? match = Multiplexer.GetServers().FirstOrDefault(s => String.Equals(Format(s), wanted, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                string known = String.Join(", ", Multiplexer.GetServers().Select(Format).OrderBy(s => s, StringComparer.Ordinal));
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Node '" + wanted + "' is not part of this resource. Known nodes: " + known + ".");
            }
            if (!match.IsConnected) throw new ConnectorException(ConnectorErrorCodes.ConnectionFailed, "Node '" + wanted + "' is not connected.");
            return new List<IServer> { match };
        }

        /// <summary>
        /// Endpoint of a server as <c>host:port</c>.
        /// </summary>
        /// <param name="server">Server.</param>
        /// <returns>Text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when server is null.</exception>
        public static string Format(IServer server)
        {
            ArgumentNullException.ThrowIfNull(server);
            string text = server.EndPoint.ToString() ?? String.Empty;
            if (text.StartsWith("Unspecified/", StringComparison.Ordinal)) text = text.Substring("Unspecified/".Length);
            return text;
        }

        /// <summary>
        /// Close the multiplexer.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Close the multiplexer.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                try
                {
                    Multiplexer.Dispose();
                }
                catch (ObjectDisposedException)
                {
                }
            }
            _Disposed = true;
        }

        #endregion
    }
}
