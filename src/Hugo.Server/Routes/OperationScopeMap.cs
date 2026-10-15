namespace Hugo.Server.Routes
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Single source of truth mapping every route and MCP tool to the permission it requires. Populated as routes
    /// register; tests assert every registered route has an entry. Thread-safe.
    /// </summary>
    public class OperationScopeMap
    {
        #region Private-Members

        private readonly ConcurrentDictionary<string, OperationScope> _Scopes = new ConcurrentDictionary<string, OperationScope>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, OperationScope> _Protocol = new ConcurrentDictionary<string, OperationScope>(StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Record a scope.
        /// </summary>
        /// <param name="scope">Scope.</param>
        /// <exception cref="ArgumentNullException">Thrown when scope is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the same method and path are registered twice.</exception>
        public void Add(OperationScope scope)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            if (!_Scopes.TryAdd(scope.Method + " " + scope.Path, scope))
            {
                throw new InvalidOperationException("Route registered twice: " + scope.Method + " " + scope.Path);
            }
        }

        /// <summary>
        /// Find a scope.
        /// </summary>
        /// <param name="method">Method.</param>
        /// <param name="path">Path template.</param>
        /// <returns>Scope or null.</returns>
        public OperationScope? Find(string method, string path)
        {
            _Scopes.TryGetValue(method + " " + path, out OperationScope? scope);
            return scope;
        }

        /// <summary>
        /// All scopes ordered by path then method.
        /// </summary>
        /// <returns>Scopes.</returns>
        public List<OperationScope> All()
        {
            return _Scopes.Values.OrderBy(s => s.Path, StringComparer.Ordinal).ThenBy(s => s.Method, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Record the scope of a protocol operation that is not an HTTP route (an MCP tool, an A2A method). Method names
        /// the protocol (for example MCP) and Path the operation (for example the tool name). Protocol operations are
        /// kept apart from routes, so <see cref="All"/> stays the HTTP route inventory.
        /// </summary>
        /// <param name="scope">Scope.</param>
        /// <exception cref="ArgumentNullException">Thrown when scope is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the same protocol and operation are registered twice.</exception>
        public void AddProtocolOperation(OperationScope scope)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            if (!_Protocol.TryAdd(scope.Method + " " + scope.Path, scope))
            {
                throw new InvalidOperationException("Protocol operation registered twice: " + scope.Method + " " + scope.Path);
            }
        }

        /// <summary>
        /// Find the scope of a protocol operation.
        /// </summary>
        /// <param name="protocol">Protocol (for example MCP).</param>
        /// <param name="operation">Operation (for example a tool name).</param>
        /// <returns>Scope or null.</returns>
        public OperationScope? FindProtocolOperation(string protocol, string operation)
        {
            _Protocol.TryGetValue(protocol + " " + operation, out OperationScope? scope);
            return scope;
        }

        /// <summary>
        /// Protocol operation scopes, optionally of one protocol, ordered by protocol then operation.
        /// </summary>
        /// <param name="protocol">Protocol to filter by, or null for all.</param>
        /// <returns>Scopes.</returns>
        public List<OperationScope> ProtocolOperations(string? protocol = null)
        {
            return _Protocol.Values
                .Where(s => protocol == null || String.Equals(s.Method, protocol, StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.Method, StringComparer.Ordinal).ThenBy(s => s.Path, StringComparer.Ordinal).ToList();
        }

        #endregion
    }
}
