namespace Hugo.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Models;

    /// <summary>Tenant-scoped transactional prompt document persistence. Thread-safe.</summary>
    public interface IAgentPromptDocumentMethods
    {
        /// <summary>Maximum stored attachments per profile, default 32, range 1 through 128.</summary>
        int MaxAttachmentsPerProfile { get; set; }

        /// <summary>List attachments in stable display order.</summary>
        Task<List<AgentPromptDocument>> ListAsync(string tenantId, string profileId, CancellationToken token = default);

        /// <summary>Read one profile attachment.</summary>
        Task<AgentPromptDocument?> ReadAsync(string tenantId, string profileId, string id, CancellationToken token = default);

        /// <summary>List tenant-scoped document referrers.</summary>
        Task<List<AgentPromptDocument>> ReferrersAsync(string tenantId, string documentId, CancellationToken token = default);

        /// <summary>Create attachment and CAS the profile set.</summary>
        Task<AgentPromptDocument> CreateAsync(AgentPromptDocument attachment, long expectedSetVersion, CancellationToken token = default);

        /// <summary>Update attachment with row and profile-set CAS.</summary>
        Task<AgentPromptDocument> UpdateAsync(AgentPromptDocument attachment, long expectedVersion, long expectedSetVersion, CancellationToken token = default);

        /// <summary>Detach using row and set CAS.</summary>
        Task<bool> DeleteAsync(string tenantId, string profileId, string id, long expectedVersion, long expectedSetVersion, CancellationToken token = default);

        /// <summary>Atomically reorder exact attachment membership; returns the new profile set version.</summary>
        Task<long> ReorderAsync(string tenantId, string profileId, List<string> attachmentIds, long expectedSetVersion, CancellationToken token = default);

    }
}
