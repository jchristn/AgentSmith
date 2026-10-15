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

        #endregion
    }
}
