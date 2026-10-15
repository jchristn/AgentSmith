namespace Hugo.Connectors.Agents.Mcp
{
    using System;
    using System.Collections.Concurrent;
    using System.Linq;

    /// <summary>
    /// Discovered tool lists keyed by resource session key, read synchronously by
    /// <see cref="Tools.McpCallToolTool.Classify"/> (classification must be fast and side-effect free, so it cannot
    /// contact the server). Bounded: when a put exceeds the capacity, the oldest lists are dropped. Thread-safe.
    /// </summary>
    public class McpCatalogCache
    {
        #region Public-Members

        /// <summary>
        /// Number of cached lists.
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

        private readonly ConcurrentDictionary<string, McpToolCatalog> _Entries = new ConcurrentDictionary<string, McpToolCatalog>(StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read a cached list.
        /// </summary>
        /// <param name="key">Session key of the resource.</param>
        /// <returns>The list, or null when none is cached.</returns>
        public McpToolCatalog? Get(string key)
        {
            if (String.IsNullOrEmpty(key)) return null;
            return _Entries.TryGetValue(key, out McpToolCatalog? catalog) ? catalog : null;
        }

        /// <summary>
        /// Store a list, dropping the oldest lists beyond the capacity.
        /// </summary>
        /// <param name="key">Session key of the resource.</param>
        /// <param name="catalog">List.</param>
        /// <param name="capacity">Most lists kept. Minimum 1.</param>
        /// <exception cref="ArgumentNullException">Thrown when key or catalog is null.</exception>
        public void Put(string key, McpToolCatalog catalog, int capacity)
        {
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            ArgumentNullException.ThrowIfNull(catalog);
            _Entries[key] = catalog;
            int max = Math.Max(1, capacity);
            int excess = _Entries.Count - max;
            if (excess <= 0) return;
            foreach (string old in _Entries.Where(e => !String.Equals(e.Key, key, StringComparison.Ordinal)).OrderBy(e => e.Value.FetchedUtc).Take(excess).Select(e => e.Key).ToList())
                _Entries.TryRemove(old, out McpToolCatalog? _);
        }

        /// <summary>
        /// Remove a cached list.
        /// </summary>
        /// <param name="key">Session key of the resource.</param>
        public void Remove(string key)
        {
            if (String.IsNullOrEmpty(key)) return;
            _Entries.TryRemove(key, out McpToolCatalog? _);
        }

        #endregion
    }
}
