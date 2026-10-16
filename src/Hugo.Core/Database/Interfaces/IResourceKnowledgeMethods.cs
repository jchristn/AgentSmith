namespace Hugo.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Models;

    /// <summary>
    /// Resource knowledge data access (resource_knowledge and resource_knowledge_versions). Every write records a
    /// version row in the same transaction. Implementations must be thread-safe.
    /// </summary>
    public interface IResourceKnowledgeMethods
    {
        /// <summary>
        /// Read one entry.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="resourceId">Resource identifier.</param>
        /// <param name="scope">Scope.</param>
        /// <param name="key">Key; empty or _root for the scope root (stored as _root).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entry or null.</returns>
        Task<ResourceKnowledge?> ReadAsync(string tenantId, string resourceId, string scope, string key, CancellationToken token = default);

        /// <summary>
        /// List entries of a resource, optionally in one scope, ordered by scope then key.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="resourceId">Resource identifier.</param>
        /// <param name="scope">Scope, or null for all.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entries.</returns>
        Task<List<ResourceKnowledge>> ListAsync(string tenantId, string resourceId, string? scope, CancellationToken token = default);

        /// <summary>
        /// Insert or update an entry, incrementing its version and recording a version row.
        /// </summary>
        /// <param name="entry">Entry (tenant, resource, scope, key, content, source, updated by).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored entry with its new version.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entry is null.</exception>
        Task<ResourceKnowledge> UpsertAsync(ResourceKnowledge entry, CancellationToken token = default);

        /// <summary>Write an entry only when its current version matches; zero requires an absent entry.</summary>
        /// <param name="entry">Entry to store.</param>
        /// <param name="expectedVersion">Current version expected by the caller, or zero for a new entry.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The written entry, or null when another writer changed it.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entry is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when expectedVersion is negative.</exception>
        Task<ResourceKnowledge?> TryUpsertAsync(ResourceKnowledge entry, int expectedVersion, CancellationToken token = default);

        /// <summary>
        /// Delete an entry and record a deletion version row.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="resourceId">Resource identifier.</param>
        /// <param name="scope">Scope.</param>
        /// <param name="key">Key.</param>
        /// <param name="deletedBy">Who deleted it.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the entry existed.</returns>
        Task<bool> DeleteAsync(string tenantId, string resourceId, string scope, string key, string? deletedBy, CancellationToken token = default);

        /// <summary>
        /// Version history of one entry, newest first.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="resourceId">Resource identifier.</param>
        /// <param name="scope">Scope.</param>
        /// <param name="key">Key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Versions.</returns>
        Task<List<ResourceKnowledgeVersion>> ListVersionsAsync(string tenantId, string resourceId, string scope, string key, CancellationToken token = default);
    }
}
