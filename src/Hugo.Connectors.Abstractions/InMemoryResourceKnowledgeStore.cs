namespace Hugo.Connectors.Abstractions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Knowledge store held in memory, for tests and tools. Thread-safe.
    /// </summary>
    public class InMemoryResourceKnowledgeStore : IResourceKnowledgeStore
    {
        private readonly Dictionary<string, ResourceKnowledgeEntry> _Entries = new Dictionary<string, ResourceKnowledgeEntry>(StringComparer.Ordinal);
        private readonly object _Lock = new object();

        /// <inheritdoc />
        public Task<ResourceKnowledgeEntry?> GetAsync(string scope, string key, CancellationToken token = default)
        {
            lock (_Lock)
            {
                return Task.FromResult(_Entries.TryGetValue(Id(scope, key), out ResourceKnowledgeEntry? e) ? Copy(e) : null);
            }
        }

        /// <inheritdoc />
        public Task<List<ResourceKnowledgeEntry>> ListAsync(string? scope, CancellationToken token = default)
        {
            lock (_Lock)
            {
                return Task.FromResult(_Entries.Values
                    .Where(e => scope == null || String.Equals(e.Scope, scope, StringComparison.Ordinal))
                    .OrderBy(e => e.Scope, StringComparer.Ordinal).ThenBy(e => e.Key, StringComparer.Ordinal)
                    .Select(Copy).ToList());
            }
        }

        /// <inheritdoc />
        public Task<ResourceKnowledgeEntry> PutAsync(ResourceKnowledgeEntry entry, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(entry);
            lock (_Lock)
            {
                string id = Id(entry.Scope, entry.Key);
                int version = _Entries.TryGetValue(id, out ResourceKnowledgeEntry? existing) ? existing.Version + 1 : 1;
                ResourceKnowledgeEntry stored = Copy(entry);
                stored.Version = version;
                stored.LastUpdateUtc = DateTime.UtcNow;
                _Entries[id] = stored;
                return Task.FromResult(Copy(stored));
            }
        }

        /// <inheritdoc />
        public Task<bool> DeleteAsync(string scope, string key, CancellationToken token = default)
        {
            lock (_Lock)
            {
                return Task.FromResult(_Entries.Remove(Id(scope, key)));
            }
        }

        private static string Id(string scope, string key)
        {
            return (scope ?? String.Empty) + "\u0001" + (key ?? String.Empty);
        }

        private static ResourceKnowledgeEntry Copy(ResourceKnowledgeEntry e)
        {
            return new ResourceKnowledgeEntry
            {
                Scope = e.Scope ?? String.Empty,
                Key = e.Key ?? String.Empty,
                Content = e.Content ?? String.Empty,
                Source = e.Source,
                UpdatedBy = e.UpdatedBy ?? String.Empty,
                Version = e.Version,
                LastUpdateUtc = e.LastUpdateUtc
            };
        }
    }
}
