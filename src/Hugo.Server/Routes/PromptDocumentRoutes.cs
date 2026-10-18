namespace Hugo.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;
    using Hugo.Core.Responses;
    using Hugo.Core.Serialization;
    using Hugo.Core.Services;
    using Hugo.Server.Services;
    using WatsonWebserver.Core;

    /// <summary>Versioned document management and profile attachment REST contracts. Thread-safe.</summary>
    public class PromptDocumentRoutes
    {
        private readonly ServerContext _Server;
        private readonly PromptDocumentService _Documents;
        private readonly PromptDocumentDeliveryService _Delivery;
        private static readonly string _Base = "/v1.0/api/prompt-documents";
        private static readonly string _Profiles = "/v1.0/api/agent-profiles/{profileId}/documents";

        /// <summary>Initialize route dependencies.</summary>
        /// <param name="server">Server context.</param><param name="documents">Authorized management service.</param><param name="delivery">Shared delivery and preview service.</param>
        public PromptDocumentRoutes(ServerContext server, PromptDocumentService documents, PromptDocumentDeliveryService delivery)
        {
            _Server = server ?? throw new ArgumentNullException(nameof(server));
            _Documents = documents ?? throw new ArgumentNullException(nameof(documents));
            _Delivery = delivery ?? throw new ArgumentNullException(nameof(delivery));
        }

        /// <summary>Register every route with its explicit operation scope and typed OpenAPI contract.</summary>
        /// <param name="routes">Existing Watson route registrar.</param>
        public void Register(RouteRegistrar routes)
        {
            ArgumentNullException.ThrowIfNull(routes);
            // Handler authorization preserves resource-scoped list filtering and invisible-object 404 semantics.
            Add(routes, HttpMethod.GET, _Base, ListAsync, ResourceTypeEnum.PromptDocument, OperationTypeEnum.Read, "List visible prompt document metadata", null, typeof(EnumerationResult<PromptDocumentView>));
            Add(routes, HttpMethod.POST, _Base, CreateAsync, ResourceTypeEnum.PromptDocument, OperationTypeEnum.Create, "Create a managed Markdown document and revision one", typeof(PromptDocumentRequest), typeof(PromptDocumentView), new[] { 201 });
            Add(routes, HttpMethod.GET, _Base + "/{documentId}", ReadAsync, ResourceTypeEnum.PromptDocument, OperationTypeEnum.Read, "Read document metadata and capabilities", null, typeof(PromptDocumentView));
            Add(routes, HttpMethod.PUT, _Base + "/{documentId}", UpdateAsync, ResourceTypeEnum.PromptDocument, OperationTypeEnum.Update, "CAS update document metadata or immutable content revision", typeof(PromptDocumentRequest), typeof(PromptDocumentView));
            Add(routes, HttpMethod.DELETE, _Base + "/{documentId}", DeleteAsync, ResourceTypeEnum.PromptDocument, OperationTypeEnum.Delete, "Delete a detached document using expectedVersion query parameter", null, null, new[] { 204 });
            Add(routes, HttpMethod.GET, _Base + "/{documentId}/revisions", RevisionsAsync, ResourceTypeEnum.PromptDocument, OperationTypeEnum.Read, "List immutable revision metadata", null, typeof(EnumerationResult<PromptDocumentRevisionInfo>));
            Add(routes, HttpMethod.GET, _Base + "/{documentId}/revisions/{revision}", RevisionAsync, ResourceTypeEnum.PromptDocument, OperationTypeEnum.Read, "Read one authorized full Markdown revision", null, typeof(PromptDocumentRevision));
            Add(routes, HttpMethod.GET, _Base + "/{documentId}/referrers", ReferrersAsync, ResourceTypeEnum.PromptDocument, OperationTypeEnum.Read, "List authorized document referrers", null, typeof(PromptDocumentReferrers));
            Add(routes, HttpMethod.PUT, _Profiles + "/order", ReorderAsync, ResourceTypeEnum.AgentProfile, OperationTypeEnum.Update, "Atomically reorder exact attachment membership", typeof(AgentPromptDocumentOrderRequest), typeof(AgentPromptDocumentList));
            Add(routes, HttpMethod.POST, _Profiles + "/preview", PreviewAsync, ResourceTypeEnum.AgentProfile, OperationTypeEnum.Read, "Preview caller or public document composition without inference", typeof(PromptDocumentPreviewRequest), typeof(PromptDocumentPreview));
            Add(routes, HttpMethod.GET, _Profiles, AttachmentsAsync, ResourceTypeEnum.AgentProfile, OperationTypeEnum.Update, "List managed profile attachments and document set version", null, typeof(AgentPromptDocumentList));
            Add(routes, HttpMethod.POST, _Profiles, AttachAsync, ResourceTypeEnum.AgentProfile, OperationTypeEnum.Update, "Attach a readable document with set CAS", typeof(AgentPromptDocumentRequest), typeof(AgentPromptDocumentView), new[] { 201 });
            Add(routes, HttpMethod.PUT, _Profiles + "/{attachmentId}", UpdateAttachmentAsync, ResourceTypeEnum.AgentProfile, OperationTypeEnum.Update, "Update attachment mode, pin, order and publication with row/set CAS", typeof(AgentPromptDocumentRequest), typeof(AgentPromptDocumentView));
            Add(routes, HttpMethod.DELETE, _Profiles + "/{attachmentId}", DetachAsync, ResourceTypeEnum.AgentProfile, OperationTypeEnum.Update, "Detach using expectedVersion and expectedDocumentSetVersion query parameters", null, null, new[] { 204 });
        }

        private void Add(RouteRegistrar routes, HttpMethod method, string path, Func<HttpContextBase, Task> handler, ResourceTypeEnum type, OperationTypeEnum operation, string summary, Type? request, Type? response, int[]? statuses = null)
        {
            routes.Authenticated(method, path, http => GuardAsync(http, handler), summary, "Prompt Documents", request, response, type, operation, statuses);
        }

        private async Task GuardAsync(HttpContextBase http, Func<HttpContextBase, Task> handler)
        {
            RouteHelpers.Context(http).SuppressHistoryBodies = true;
            try { await handler(http).ConfigureAwait(false); }
            catch (PromptDocumentException error) { await RouteHelpers.ErrorAsync(http, error.StatusCode, error.Code, error.Message).ConfigureAwait(false); }
        }

        private async Task ListAsync(HttpContextBase http)
        {
            await RouteHelpers.JsonAsync(http, 200, await _Documents.ListAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), RouteHelpers.Enumeration(http), RouteHelpers.QueryBool(http, "active"), http.Token, RouteHelpers.QueryEnum<VisibilityEnum>(http, "visibility")).ConfigureAwait(false)).ConfigureAwait(false);
        }
        private async Task CreateAsync(HttpContextBase http)
        {
            await RouteHelpers.JsonAsync(http, 201, await _Documents.CreateAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), Body<PromptDocumentRequest>(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }
        private async Task ReadAsync(HttpContextBase http)
        {
            await RouteHelpers.JsonAsync(http, 200, await _Documents.ReadAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), DocumentId(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }
        private async Task UpdateAsync(HttpContextBase http)
        {
            await RouteHelpers.JsonAsync(http, 200, await _Documents.UpdateAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), DocumentId(http), Body<PromptDocumentRequest>(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }
        private async Task DeleteAsync(HttpContextBase http)
        {
            await _Documents.DeleteAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), DocumentId(http), QueryVersion(http, "expectedVersion"), http.Token).ConfigureAwait(false);
            await RouteHelpers.NoContentAsync(http).ConfigureAwait(false);
        }
        private async Task RevisionsAsync(HttpContextBase http)
        {
            await RouteHelpers.JsonAsync(http, 200, await _Documents.RevisionsAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), DocumentId(http), RouteHelpers.Enumeration(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }
        private async Task RevisionAsync(HttpContextBase http)
        {
            string? raw = RouteHelpers.Param(http, "revision");
            if (!Int32.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int revision) || revision < 1) throw new ValidationException("A positive integer revision is required.", new Dictionary<string, string> { { "revision", "A positive integer revision is required." } });
            await RouteHelpers.JsonAsync(http, 200, await _Documents.RevisionAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), DocumentId(http), revision, http.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }
        private async Task ReferrersAsync(HttpContextBase http)
        {
            await RouteHelpers.JsonAsync(http, 200, await _Documents.ReferrersAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), DocumentId(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }
        private async Task AttachmentsAsync(HttpContextBase http)
        {
            await RouteHelpers.JsonAsync(http, 200, await _Documents.AttachmentsAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), ProfileId(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }
        private async Task AttachAsync(HttpContextBase http)
        {
            await RouteHelpers.JsonAsync(http, 201, await _Documents.SaveAttachmentAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), ProfileId(http), null, Body<AgentPromptDocumentRequest>(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }
        private async Task UpdateAttachmentAsync(HttpContextBase http)
        {
            await RouteHelpers.JsonAsync(http, 200, await _Documents.SaveAttachmentAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), ProfileId(http), AttachmentId(http), Body<AgentPromptDocumentRequest>(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }
        private async Task DetachAsync(HttpContextBase http)
        {
            await _Documents.DetachAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), ProfileId(http), AttachmentId(http), QueryVersion(http, "expectedVersion"), QueryVersion(http, "expectedDocumentSetVersion"), http.Token).ConfigureAwait(false);
            await RouteHelpers.NoContentAsync(http).ConfigureAwait(false);
        }
        private async Task ReorderAsync(HttpContextBase http)
        {
            long version = await _Documents.ReorderAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), ProfileId(http), Body<AgentPromptDocumentOrderRequest>(http), http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, new AgentPromptDocumentList { DocumentSetVersion = version }).ConfigureAwait(false);
        }
        private async Task PreviewAsync(HttpContextBase http)
        {
            PromptDocumentPreviewRequest request = Body<PromptDocumentPreviewRequest>(http);
            await RouteHelpers.JsonAsync(http, 200, await _Delivery.PreviewAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), ProfileId(http), request.Audience, http.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private T Body<T>(HttpContextBase http) where T : class
        {
            long maximum = (long)_Server.Settings.PromptDocuments.MaxPromptDocumentBytes * 6 + 65536;
            if (http.Request.ContentLength > maximum) throw new PromptDocumentException("request_body_too_large", "Request body exceeds the configured document transport limit.", 413);
            string body = http.Request.DataAsString;
            if (body.Length > maximum || Encoding.UTF8.GetByteCount(body) > maximum) throw new PromptDocumentException("request_body_too_large", "Request body exceeds the configured document transport limit.", 413);
            if (String.IsNullOrWhiteSpace(body)) throw new RequestBodyException("A JSON request object is required.");
            try { return Serializer.Deserialize<T>(body) ?? throw new RequestBodyException("A JSON request object is required."); }
            catch (JsonException) { throw new RequestBodyException("Request contains invalid JSON, unknown fields, or unsupported field values."); }
        }
        private static long? QueryVersion(HttpContextBase http, string name)
        {
            string? raw = RouteHelpers.Query(http, name);
            if (raw == null) return null;
            if (!Int64.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out long version) || version > 9007199254740991L) throw new ValidationException("A JSON-safe integer version is required.", new Dictionary<string, string> { { name, "A JSON-safe integer version is required." } });
            return version;
        }
        private static string DocumentId(HttpContextBase http) { return RouteHelpers.Param(http, "documentId") ?? String.Empty; }
        private static string ProfileId(HttpContextBase http) { return RouteHelpers.Param(http, "profileId") ?? String.Empty; }
        private static string AttachmentId(HttpContextBase http) { return RouteHelpers.Param(http, "attachmentId") ?? String.Empty; }
    }
}
