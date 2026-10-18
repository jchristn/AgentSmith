namespace Hugo.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Models;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Responses;

    /// <summary>Tenant-scoped transactional prompt document persistence. Thread-safe.</summary>
    public interface IPromptDocumentMethods
    {
        /// <summary>Maximum library documents per tenant, default 256, range 1 through 10000.</summary>
        int MaxDocumentsPerTenant { get; set; }

        /// <summary>Maximum total retained revision bytes per tenant, default 67108864, range 1048576 through 1073741824.</summary>
        int MaxStorageBytesPerTenant { get; set; }

        /// <summary>Maximum UTF-8 bytes per revision, default 262144, range 1024 through 1048576.</summary>
        int MaxDocumentBytes { get; set; }

        /// <summary>Create metadata and revision one atomically.</summary>
        Task<PromptDocument> CreateAsync(PromptDocument document, PromptDocumentRevision revision, CancellationToken token = default);

        /// <summary>Read a tenant-scoped document.</summary>
        Task<PromptDocument?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>List tenant metadata only; the caller applies visibility filtering.</summary>
        Task<List<PromptDocument>> ListAsync(string tenantId, CancellationToken token = default);

        /// <summary>Read an immutable revision in the tenant.</summary>
        Task<PromptDocumentRevision?> ReadRevisionAsync(string tenantId, string documentId, int revision, CancellationToken token = default);

        /// <summary>Read selected revision metadata without loading its Markdown body.</summary>
        Task<PromptDocumentRevisionInfo?> ReadRevisionMetadataAsync(string tenantId, string documentId, int revision, CancellationToken token = default);

        /// <summary>Page revision metadata without loading historical Markdown bodies.</summary>
        Task<EnumerationResult<PromptDocumentRevisionInfo>> ListRevisionMetadataAsync(string tenantId, string documentId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>List immutable revisions. Caller must project metadata for enumeration.</summary>
        Task<List<PromptDocumentRevision>> ListRevisionsAsync(string tenantId, string documentId, CancellationToken token = default);

        /// <summary>CAS metadata and optional revision, allocating the next revision atomically. False means a stale version or missing document.</summary>
        Task<bool> TryUpdateAsync(PromptDocument document, PromptDocumentRevision? revision, long expectedVersion, CancellationToken token = default);

        /// <summary>CAS document mutation with explicit public Latest revision authorization verified by the service.</summary>
        Task<bool> TryUpdateAsync(PromptDocument document, PromptDocumentRevision? revision, long expectedVersion, bool authorizedPublicRevisionChange, CancellationToken token = default);

        /// <summary>Delete detached metadata and revisions atomically; throws conflict while attached or stale.</summary>
        Task<bool> DeleteAsync(string tenantId, string id, long expectedVersion, CancellationToken token = default);

    }
}
