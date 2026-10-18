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

    /// <summary>Documents attached to an agent, independent of reusable library content.</summary>
    public class AgentPromptDocumentsClient : ClientBase
    {
        /// <summary>Construct using the shared transport.</summary>
        /// <param name="transport">Authenticated transport.</param>
        public AgentPromptDocumentsClient(HugoTransport transport) : base(transport) { }

        /// <summary>List attachments and their set version.</summary>
        /// <param name="profileId">profileId.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Server response.</returns>
        /// <exception cref="HugoException">The request failed, including a stale version conflict.</exception>
        public Task<AgentPromptDocumentList> ListAsync(string profileId, CancellationToken token = default)
        {
            return GetAsync<AgentPromptDocumentList>("/v1.0/api/agent-profiles/" + Segment(profileId) + "/documents", null, token);
        }

        /// <summary>CreateAsync; preserves document versions and profile permissions.</summary>
        /// <param name="profileId">profileId.</param>
        /// <param name="request">request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Server response.</returns>
        /// <exception cref="HugoException">The request failed, including a stale version conflict.</exception>
        public Task<AgentPromptDocument> CreateAsync(string profileId, AgentPromptDocumentRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            string path = "/v1.0/api/agent-profiles/" + Segment(profileId) + "/documents";
            return PostAsync<AgentPromptDocument>(path, request, token);
        }

        /// <summary>UpdateAsync; preserves document versions and profile permissions.</summary>
        /// <param name="profileId">profileId.</param>
        /// <param name="attachmentId">attachmentId.</param>
        /// <param name="request">request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Server response.</returns>
        /// <exception cref="HugoException">The request failed, including a stale version conflict.</exception>
        public Task<AgentPromptDocument> UpdateAsync(string profileId, string attachmentId, AgentPromptDocumentRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            string path = "/v1.0/api/agent-profiles/" + Segment(profileId) + "/documents" + "/" + Segment(attachmentId);
            return PutAsync<AgentPromptDocument>(path, request, token);
        }

        /// <summary>DeleteAsync; preserves document versions and profile permissions.</summary>
        /// <param name="profileId">profileId.</param>
        /// <param name="attachmentId">attachmentId.</param>
        /// <param name="expectedVersion">expectedVersion.</param>
        /// <param name="expectedDocumentSetVersion">expectedDocumentSetVersion.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="HugoException">The request failed, including a stale version conflict.</exception>
        public Task DeleteAsync(string profileId, string attachmentId, long expectedVersion, long expectedDocumentSetVersion, CancellationToken token = default)
        {
            string path = "/v1.0/api/agent-profiles/" + Segment(profileId) + "/documents" + "/" + Segment(attachmentId);
            return DeleteAsync(path, new Dictionary<string, string?> { { "expectedVersion", expectedVersion.ToString(CultureInfo.InvariantCulture) }, { "expectedDocumentSetVersion", expectedDocumentSetVersion.ToString(CultureInfo.InvariantCulture) } }, token);
        }

        /// <summary>ReorderAsync; preserves document versions and profile permissions.</summary>
        /// <param name="profileId">profileId.</param>
        /// <param name="request">request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Server response.</returns>
        /// <exception cref="HugoException">The request failed, including a stale version conflict.</exception>
        public Task<AgentPromptDocumentList> ReorderAsync(string profileId, AgentPromptDocumentOrderRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            string path = "/v1.0/api/agent-profiles/" + Segment(profileId) + "/documents" + "/order";
            return PutAsync<AgentPromptDocumentList>(path, request, token);
        }

        /// <summary>PreviewAsync; preserves document versions and profile permissions.</summary>
        /// <param name="profileId">profileId.</param>
        /// <param name="request">request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Server response.</returns>
        /// <exception cref="HugoException">The request failed, including a stale version conflict.</exception>
        public Task<PromptDocumentPreview> PreviewAsync(string profileId, PromptDocumentPreviewRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            string path = "/v1.0/api/agent-profiles/" + Segment(profileId) + "/documents" + "/preview";
            return PostAsync<PromptDocumentPreview>(path, request, token);
        }

    }
}
