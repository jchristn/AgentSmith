namespace Hugo.Server.Protocols
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using Hugo.Server.Routes;

    /// <summary>
    /// The static mapping of every MCP tool to the permission it requires (P7.9, plan section 15.3, Armada pattern).
    /// A tool that is not mapped fails closed: it is never listed and a call to it is refused with unmapped_tool. Each
    /// mapping is also recorded in the operation scope map as protocol MCP. Thread-safe.
    /// </summary>
    public class McpToolAuthorizationRegistry
    {
        #region Public-Members

        /// <summary>
        /// Protocol name used in the operation scope map.
        /// </summary>
        public static readonly string Protocol = "MCP";

        /// <summary>
        /// Number of mapped tools.
        /// </summary>
        public int Count
        {
            get
            {
                return _Entries.Count;
            }
        }

        #endregion

        #region Private-Members

        private readonly ConcurrentDictionary<string, McpToolAuthorization> _Entries = new ConcurrentDictionary<string, McpToolAuthorization>(StringComparer.Ordinal);
        private readonly OperationScopeMap? _Scopes;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="scopes">Operation scope map to record mappings in, or null.</param>
        public McpToolAuthorizationRegistry(OperationScopeMap? scopes = null)
        {
            _Scopes = scopes;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Map a tool. Mapping the same name again with the same permission is ignored.
        /// </summary>
        /// <param name="entry">Mapping.</param>
        /// <exception cref="ArgumentNullException">Thrown when entry is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the name is already mapped to a different permission.</exception>
        public void Map(McpToolAuthorization entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            McpToolAuthorization stored = _Entries.GetOrAdd(entry.Name, entry);
            if (!ReferenceEquals(stored, entry))
            {
                if (stored.ResourceType != entry.ResourceType || stored.Operation != entry.Operation || stored.Kind != entry.Kind)
                    throw new InvalidOperationException("MCP tool " + entry.Name + " is mapped twice with different permissions.");
                return;
            }
            if (_Scopes != null && _Scopes.FindProtocolOperation(Protocol, entry.Name) == null)
            {
                OperationScope scope = new OperationScope();
                scope.Method = Protocol;
                scope.Path = entry.Name;
                scope.ResourceType = entry.ResourceType;
                scope.Operation = entry.Operation;
                scope.Authorization = RouteAuthorizationEnum.Scoped;
                scope.Tag = "MCP";
                scope.Summary = entry.Summary;
                _Scopes.AddProtocolOperation(scope);
            }
        }

        /// <summary>
        /// The mapping of a tool, or null when it is not mapped (callers must then refuse the tool).
        /// </summary>
        /// <param name="name">Tool name.</param>
        /// <returns>Mapping or null.</returns>
        public McpToolAuthorization? Find(string? name)
        {
            if (String.IsNullOrEmpty(name)) return null;
            _Entries.TryGetValue(name, out McpToolAuthorization? entry);
            return entry;
        }

        /// <summary>
        /// Whether a tool is mapped.
        /// </summary>
        /// <param name="name">Tool name.</param>
        /// <returns>True when mapped.</returns>
        public bool IsMapped(string? name)
        {
            return Find(name) != null;
        }

        /// <summary>
        /// Every mapping, ordered by name.
        /// </summary>
        /// <returns>Mappings.</returns>
        public List<McpToolAuthorization> All()
        {
            return _Entries.Values.OrderBy(e => e.Name, StringComparer.Ordinal).ToList();
        }

        #endregion
    }
}
