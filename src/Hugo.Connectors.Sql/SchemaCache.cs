namespace Hugo.Connectors.Sql
{
    using System;
    using System.Collections.Concurrent;
    using System.Linq;
    using Hugo.Connectors.Sql.Vendored.Tablix;

    /// <summary>
    /// In-process cache of full schema crawls keyed by resource session key and schema. Every discovery and
    /// intelligence tool (sql_describe_schema, sql_list_tables, sql_list_relationships, sql_get_intelligence,
    /// sql_get_agent_pack, the context tools, and sql_ask) reads the same crawl, and a <c>refresh</c> argument on any of
    /// them recrawls. Entries expire after the resource's <c>schemaCacheMinutes</c>. The persisted
    /// <c>connector_schema_cache</c> table described in the plan belongs to the server; this cache keeps repeated crawls
    /// cheap within one process. Thread-safe.
    /// </summary>
    public class SchemaCache
    {
        #region Public-Members

        /// <summary>
        /// Maximum number of entries kept. When exceeded, the entries closest to expiry are evicted. Minimum 1, maximum
        /// 100000, default 256.
        /// </summary>
        public int MaxEntries
        {
            get
            {
                return _MaxEntries;
            }
            set
            {
                _MaxEntries = Math.Clamp(value, 1, 100000);
            }
        }

        /// <summary>
        /// Number of entries currently held, including expired ones not yet evicted.
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

        private readonly ConcurrentDictionary<string, SchemaCacheEntry> _Entries = new ConcurrentDictionary<string, SchemaCacheEntry>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, string> _DefaultSchemas = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        private int _MaxEntries = 256;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Get a live entry.
        /// </summary>
        /// <param name="key">Cache key.</param>
        /// <returns>The entry, or null when absent or expired.</returns>
        public SchemaCacheEntry? Get(string key)
        {
            if (String.IsNullOrEmpty(key)) return null;
            if (!_Entries.TryGetValue(key, out SchemaCacheEntry? entry)) return null;
            if (entry.ExpiresUtc <= DateTime.UtcNow)
            {
                _Entries.TryRemove(key, out _);
                return null;
            }
            return entry;
        }

        /// <summary>
        /// Store a crawl. A non-positive TTL stores nothing.
        /// </summary>
        /// <param name="key">Cache key.</param>
        /// <param name="detail">Crawl result.</param>
        /// <param name="ttl">Time to live.</param>
        /// <exception cref="ArgumentNullException">Thrown when key or detail is null.</exception>
        public void Set(string key, SchemaDetail detail, TimeSpan ttl)
        {
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            ArgumentNullException.ThrowIfNull(detail);
            if (ttl <= TimeSpan.Zero) return;
            _Entries[key] = new SchemaCacheEntry(detail, DateTime.UtcNow.Add(ttl));
            Trim();
        }

        /// <summary>
        /// Remove an entry.
        /// </summary>
        /// <param name="key">Cache key.</param>
        public void Invalidate(string key)
        {
            if (String.IsNullOrEmpty(key)) return;
            _Entries.TryRemove(key, out _);
        }

        /// <summary>
        /// Remove every entry.
        /// </summary>
        public void Clear()
        {
            _Entries.Clear();
            _DefaultSchemas.Clear();
        }

        /// <summary>
        /// Remember the default schema a resource resolved to, so cached crawls can be served without opening a
        /// session when the caller names no schema.
        /// </summary>
        /// <param name="resourceKey">Resource session key.</param>
        /// <param name="schema">Default schema.</param>
        public void RememberDefaultSchema(string resourceKey, string schema)
        {
            if (String.IsNullOrEmpty(resourceKey) || String.IsNullOrEmpty(schema)) return;
            _DefaultSchemas[resourceKey] = schema;
            if (_DefaultSchemas.Count > _MaxEntries * 4) _DefaultSchemas.Clear();
        }

        /// <summary>
        /// The remembered default schema of a resource.
        /// </summary>
        /// <param name="resourceKey">Resource session key.</param>
        /// <returns>Schema, or null when unknown.</returns>
        public string? DefaultSchema(string resourceKey)
        {
            if (String.IsNullOrEmpty(resourceKey)) return null;
            return _DefaultSchemas.TryGetValue(resourceKey, out string? schema) ? schema : null;
        }

        #endregion

        #region Private-Methods

        private void Trim()
        {
            if (_Entries.Count <= _MaxEntries) return;
            DateTime now = DateTime.UtcNow;
            foreach (string expired in _Entries.Where(e => e.Value.ExpiresUtc <= now).Select(e => e.Key).ToList()) _Entries.TryRemove(expired, out _);
            int excess = _Entries.Count - _MaxEntries;
            if (excess <= 0) return;
            foreach (string key in _Entries.OrderBy(e => e.Value.ExpiresUtc).Take(excess).Select(e => e.Key).ToList()) _Entries.TryRemove(key, out _);
        }

        #endregion
    }
}
