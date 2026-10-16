namespace Hugo.Connectors.Abstractions
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Knowledge store supporting atomic optimistic writes across all writers and processes.
    /// </summary>
    public interface IVersionedResourceKnowledgeStore : IResourceKnowledgeStore
    {
        /// <summary>
        /// Store an entry only when its current version matches the supplied version. No history or audit change is
        /// made on a conflict. Implementations retain permission checks and secret scrubbing.
        /// </summary>
        /// <param name="entry">New entry content and attribution.</param>
        /// <param name="expectedVersion">Current version, or zero to require that the entry does not exist.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Stored entry, or null on a version conflict.</returns>
        Task<ResourceKnowledgeEntry?> TryPutAsync(ResourceKnowledgeEntry entry, int expectedVersion, CancellationToken token = default);
    }
}
