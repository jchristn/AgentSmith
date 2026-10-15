namespace Hugo.Connectors.Agents.Mcp
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// The tool list discovered on one external MCP server at one point in time, before the resource's include and exclude
    /// filters. Immutable after construction, so it is safe to share between threads.
    /// </summary>
    public class McpToolCatalog
    {
        #region Public-Members

        /// <summary>
        /// Discovered tools in server order. Never null.
        /// </summary>
        public IReadOnlyList<McpRemoteTool> Tools { get; }

        /// <summary>
        /// When the list was fetched (UTC).
        /// </summary>
        public DateTime FetchedUtc { get; }

        /// <summary>
        /// Whether pagination stopped at the page cap before the server's list ended.
        /// </summary>
        public bool Incomplete { get; }

        #endregion

        #region Private-Members

        private readonly Dictionary<string, McpRemoteTool> _ByName;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="tools">Tools. Null is treated as empty. Later duplicates of a name are ignored.</param>
        /// <param name="fetchedUtc">Fetch time (UTC).</param>
        /// <param name="incomplete">Whether the page cap was reached.</param>
        public McpToolCatalog(IEnumerable<McpRemoteTool>? tools, DateTime fetchedUtc, bool incomplete)
        {
            _ByName = new Dictionary<string, McpRemoteTool>(StringComparer.Ordinal);
            List<McpRemoteTool> list = new List<McpRemoteTool>();
            foreach (McpRemoteTool tool in tools ?? Enumerable.Empty<McpRemoteTool>())
            {
                if (tool == null || String.IsNullOrEmpty(tool.Name) || _ByName.ContainsKey(tool.Name)) continue;
                _ByName[tool.Name] = tool;
                list.Add(tool);
            }
            Tools = list;
            FetchedUtc = fetchedUtc;
            Incomplete = incomplete;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Find a tool by exact name.
        /// </summary>
        /// <param name="name">Remote tool name.</param>
        /// <returns>The tool, or null when the server did not list it.</returns>
        public McpRemoteTool? Find(string? name)
        {
            if (String.IsNullOrEmpty(name)) return null;
            return _ByName.TryGetValue(name, out McpRemoteTool? tool) ? tool : null;
        }

        /// <summary>
        /// Whether the list is older than a refresh interval.
        /// </summary>
        /// <param name="refreshInterval">Refresh interval.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <returns>True when stale.</returns>
        public bool IsStale(TimeSpan refreshInterval, DateTime nowUtc)
        {
            return nowUtc - FetchedUtc >= refreshInterval;
        }

        #endregion
    }
}
