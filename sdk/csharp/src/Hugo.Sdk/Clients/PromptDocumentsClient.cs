namespace Hugo.Sdk.Clients
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Sdk.Enumeration;
    using Hugo.Sdk.Http;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Requests;
    using Hugo.Sdk.Responses;

    /// <summary>Versioned Markdown library. Paths are managed documents, never server filesystem paths.</summary>
    public class PromptDocumentsClient : ClientBase
    {
        /// <summary>Construct using the shared transport.</summary>
        /// <param name="transport">Authenticated transport.</param>
        public PromptDocumentsClient(HugoTransport transport) : base(transport) { }

        /// <summary>List document metadata without content.</summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Server response.</returns>
        /// <exception cref="HugoException">The request failed, including a stale version conflict.</exception>
        public Task<EnumerationResult<PromptDocument>> ListAsync(EnumerationQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<PromptDocument>>("/v1.0/api/prompt-documents", QueryBuilder.From(query), token);
        }

        /// <summary>List with an explicit active/inactive state filter.</summary>
        /// <param name="query">Paging and name filter.</param>
        /// <param name="active">True for active, false for inactive, null for both.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Readable metadata page.</returns>
        public Task<EnumerationResult<PromptDocument>> ListAsync(EnumerationQuery? query, bool? active, CancellationToken token = default)
        {
            Dictionary<string, string?> parameters = QueryBuilder.From(query);
            parameters["active"] = active.HasValue ? active.Value.ToString().ToLowerInvariant() : null;
            return GetAsync<EnumerationResult<PromptDocument>>("/v1.0/api/prompt-documents", parameters, token);
        }

        /// <summary>GetAsync; uses current authorization and concurrency rules.</summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Server response.</returns>
        /// <exception cref="HugoException">The request failed, including a stale version conflict.</exception>
        public Task<PromptDocument> GetAsync(string id, CancellationToken token = default)
        {
            return GetAsync<PromptDocument>("/v1.0/api/prompt-documents/" + Segment(id), null, token);
        }

        /// <summary>CreateAsync; uses current authorization and concurrency rules.</summary>
        /// <param name="request">request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Server response.</returns>
        /// <exception cref="HugoException">The request failed, including a stale version conflict.</exception>
        public Task<PromptDocument> CreateAsync(PromptDocumentRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            return PostAsync<PromptDocument>("/v1.0/api/prompt-documents", request, token);
        }

        /// <summary>UpdateAsync; uses current authorization and concurrency rules.</summary>
        /// <param name="id">id.</param>
        /// <param name="request">request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Server response.</returns>
        /// <exception cref="HugoException">The request failed, including a stale version conflict.</exception>
        public Task<PromptDocument> UpdateAsync(string id, PromptDocumentRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            return PutAsync<PromptDocument>("/v1.0/api/prompt-documents/" + Segment(id), request, token);
        }

        /// <summary>DeleteAsync; uses current authorization and concurrency rules.</summary>
        /// <param name="id">id.</param>
        /// <param name="expectedVersion">expectedVersion.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="HugoException">The request failed, including a stale version conflict.</exception>
        public Task DeleteAsync(string id, long expectedVersion, CancellationToken token = default)
        {
            return DeleteAsync("/v1.0/api/prompt-documents/" + Segment(id), new Dictionary<string, string?> { { "expectedVersion", expectedVersion.ToString(CultureInfo.InvariantCulture) } }, token);
        }

        /// <summary>ListRevisionsAsync; uses current authorization and concurrency rules.</summary>
        /// <param name="id">id.</param>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Server response.</returns>
        /// <exception cref="HugoException">The request failed, including a stale version conflict.</exception>
        public Task<EnumerationResult<PromptDocumentRevision>> ListRevisionsAsync(string id, EnumerationQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<PromptDocumentRevision>>("/v1.0/api/prompt-documents/" + Segment(id) + "/revisions", QueryBuilder.From(query), token);
        }

        /// <summary>GetRevisionAsync; uses current authorization and concurrency rules.</summary>
        /// <param name="id">id.</param>
        /// <param name="revision">revision.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Server response.</returns>
        /// <exception cref="HugoException">The request failed, including a stale version conflict.</exception>
        public Task<PromptDocumentRevision> GetRevisionAsync(string id, int revision, CancellationToken token = default)
        {
            return GetAsync<PromptDocumentRevision>("/v1.0/api/prompt-documents/" + Segment(id) + "/revisions/" + revision.ToString(CultureInfo.InvariantCulture), null, token);
        }

        /// <summary>GetReferrersAsync; uses current authorization and concurrency rules.</summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Server response.</returns>
        /// <exception cref="HugoException">The request failed, including a stale version conflict.</exception>
        public Task<PromptDocumentReferrers> GetReferrersAsync(string id, CancellationToken token = default)
        {
            return GetAsync<PromptDocumentReferrers>("/v1.0/api/prompt-documents/" + Segment(id) + "/referrers", null, token);
        }

    }
}
