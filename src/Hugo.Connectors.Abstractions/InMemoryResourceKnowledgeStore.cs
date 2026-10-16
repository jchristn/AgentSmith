namespace Hugo.Connectors.Abstractions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Knowledge store held in memory, for tests and tools. Versions remain monotonic across deletion and recreation
    /// for the lifetime of this store, preventing stale optimistic writes from matching a recreated entry. Thread-safe.
    /// </summary>
    public class InMemoryResourceKnowledgeStore : IVersionedResourceKnowledgeStore
    {
        private readonly Dictionary<string, ResourceKnowledgeEntry> _Entries = new Dictionary<string, ResourceKnowledgeEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _Versions = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly object _Lock = new object();

        /// <inheritdoc />
        public Task<ResourceKnowledgeEntry?> GetAsync(string scope, string key, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            lock (_Lock)
            {
                return Task.FromResult(_Entries.TryGetValue(Id(scope, key), out ResourceKnowledgeEntry? e) ? Copy(e) : null);
            }
        }

        /// <inheritdoc />
        public Task<List<ResourceKnowledgeEntry>> ListAsync(string? scope, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
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
            token.ThrowIfCancellationRequested();
            lock (_Lock)
            {
                string id = Id(entry.Scope, entry.Key);
                int version = checked((_Versions.TryGetValue(id, out int previousVersion) ? previousVersion : 0) + 1);
                ResourceKnowledgeEntry stored = Copy(entry);
                stored.Version = version;
                stored.LastUpdateUtc = DateTime.UtcNow;
                _Entries[id] = stored;
                _Versions[id] = stored.Version;
                return Task.FromResult(Copy(stored));
            }
        }

        /// <inheritdoc />
        public Task<ResourceKnowledgeEntry?> TryPutAsync(ResourceKnowledgeEntry entry, int expectedVersion, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (expectedVersion < 0) throw new ArgumentOutOfRangeException(nameof(expectedVersion));
            token.ThrowIfCancellationRequested();
            lock (_Lock)
            {
                string id = Id(entry.Scope, entry.Key);
                int version = _Entries.TryGetValue(id, out ResourceKnowledgeEntry? existing) ? existing.Version : 0;
                if (version != expectedVersion) return Task.FromResult<ResourceKnowledgeEntry?>(null);
                ResourceKnowledgeEntry stored = Copy(entry);
                stored.Version = checked((_Versions.TryGetValue(id, out int previousVersion) ? previousVersion : 0) + 1);
                stored.LastUpdateUtc = DateTime.UtcNow;
                _Entries[id] = stored;
                _Versions[id] = stored.Version;
                return Task.FromResult<ResourceKnowledgeEntry?>(Copy(stored));
            }
        }

        /// <inheritdoc />
        public Task<bool> DeleteAsync(string scope, string key, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            lock (_Lock)
            {
                return Task.FromResult(_Entries.Remove(Id(scope, key)));
            }
        }

        private static string Id(string scope, string key)
        {
            string normalizedScope = scope ?? String.Empty;
            string normalizedKey = String.IsNullOrEmpty(key) || key == "_root" ? String.Empty : key;
            return normalizedScope.Length + ":" + normalizedScope + normalizedKey;
        }

        private static ResourceKnowledgeEntry Copy(ResourceKnowledgeEntry e)
        {
            return new ResourceKnowledgeEntry
            {
                Scope = e.Scope ?? String.Empty,
                Key = String.IsNullOrEmpty(e.Key) || e.Key == "_root" ? String.Empty : e.Key,
                Content = e.Content ?? String.Empty,
                Source = e.Source,
                UpdatedBy = e.UpdatedBy ?? String.Empty,
                Version = e.Version,
                LastUpdateUtc = e.LastUpdateUtc
            };
        }
    }
}
