namespace Hugo.Connectors.Abstractions
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Durable knowledge about one resource, persisted by the server (tenant and resource scoped) and shown to operators
    /// in the dashboard. Implementations are thread-safe.
    /// </summary>
    public interface IResourceKnowledgeStore
    {
        /// <summary>
        /// Read one entry.
        /// </summary>
        /// <param name="scope">Scope.</param>
        /// <param name="key">Key (empty for the scope root).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The entry, or null.</returns>
        Task<ResourceKnowledgeEntry?> GetAsync(string scope, string key, CancellationToken token = default);

        /// <summary>
        /// List entries in a scope, or every entry when scope is null.
        /// </summary>
        /// <param name="scope">Scope, or null for all.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entries ordered by scope then key.</returns>
        Task<List<ResourceKnowledgeEntry>> ListAsync(string? scope, CancellationToken token = default);

        /// <summary>
        /// Create or replace an entry. The store sets <see cref="ResourceKnowledgeEntry.Version"/> and
        /// <see cref="ResourceKnowledgeEntry.LastUpdateUtc"/>, and scrubs secret values from the content.
        /// </summary>
        /// <param name="entry">Entry.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored entry.</returns>
        Task<ResourceKnowledgeEntry> PutAsync(ResourceKnowledgeEntry entry, CancellationToken token = default);

        /// <summary>
        /// Delete an entry.
        /// </summary>
        /// <param name="scope">Scope.</param>
        /// <param name="key">Key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when deleted.</returns>
        Task<bool> DeleteAsync(string scope, string key, CancellationToken token = default);
    }
}
