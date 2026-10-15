namespace Hugo.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;
    using Hugo.Core.Responses;
    using Hugo.Core.Security;
    using Hugo.Core.Services;
    using Hugo.Server.Services;
    using WatsonWebserver.Core;

    /// <summary>
    /// Resource routes (P3.7, P3.25): CRUD, duplicate, referrers, connection tests of saved and unsaved settings, an
    /// administrator direct invoke, and resource knowledge. The tenant comes from the request context (flat paths).
    /// Knowledge paths: GET /knowledge lists every entry; GET /knowledge/{scope} lists a scope; PUT and DELETE
    /// /knowledge/{scope} write the scope's root entry (empty key); GET, PUT, and DELETE /knowledge/{scope}/{key} work on
    /// one entry; GET .../versions returns history. The key _root addresses the root entry in a {key} path.
    /// </summary>
    public class ResourceRoutes
    {
        #region Private-Members

        private static readonly string _Base = "/v1.0/api/resources";
        private static readonly string _RootKey = "_root";
        private readonly ServerContext _Server;
        private readonly ResourceService _Resources;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="server">Server context.</param>
        /// <param name="resources">Resource service.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public ResourceRoutes(ServerContext server, ResourceService resources)
        {
            _Server = server ?? throw new ArgumentNullException(nameof(server));
            _Resources = resources ?? throw new ArgumentNullException(nameof(resources));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="routes">Registrar.</param>
        /// <exception cref="ArgumentNullException">Thrown when routes is null.</exception>
        public void Register(RouteRegistrar routes)
        {
            if (routes == null) throw new ArgumentNullException(nameof(routes));
            routes.Scoped(HttpMethod.GET, _Base, ResourceTypeEnum.Resource, OperationTypeEnum.Read, ListAsync, "List resources (filter with connectorType)", "Resources", null, typeof(EnumerationResult<Resource>));
            routes.Scoped(HttpMethod.POST, _Base, ResourceTypeEnum.Resource, OperationTypeEnum.Create, CreateAsync, "Create a resource (settings validated against the connector schema; secrets by slot)", "Resources", typeof(ResourceRequest), typeof(Resource));
            routes.Scoped(HttpMethod.POST, _Base + "/test", ResourceTypeEnum.Resource, OperationTypeEnum.Create, TestUnsavedAsync, "Validate and test unsaved resource settings (nothing is stored)", "Resources", typeof(ResourceRequest), typeof(ResourceTestResponse));
            routes.Scoped(HttpMethod.GET, _Base + "/{id}", ResourceTypeEnum.Resource, OperationTypeEnum.Read, ReadAsync, "Read a resource", "Resources", null, typeof(Resource));
            routes.Scoped(HttpMethod.PUT, _Base + "/{id}", ResourceTypeEnum.Resource, OperationTypeEnum.Update, UpdateAsync, "Update a resource", "Resources", typeof(ResourceRequest), typeof(Resource));
            routes.Scoped(HttpMethod.DELETE, _Base + "/{id}", ResourceTypeEnum.Resource, OperationTypeEnum.Delete, DeleteAsync, "Delete a resource with its bindings, settings, secret slots, schema cache, and knowledge", "Resources");
            routes.Scoped(HttpMethod.POST, _Base + "/{id}/test", ResourceTypeEnum.Resource, OperationTypeEnum.Read, TestAsync, "Test a saved resource's connection and record the result", "Resources", null, typeof(ResourceTestResponse));
            routes.Scoped(HttpMethod.POST, _Base + "/{id}/duplicate", ResourceTypeEnum.Resource, OperationTypeEnum.Create, DuplicateAsync, "Copy a resource with its settings and secret slots under a new name", "Resources", typeof(CloneRequest), typeof(Resource));
            routes.Scoped(HttpMethod.GET, _Base + "/{id}/referrers", ResourceTypeEnum.Resource, OperationTypeEnum.Read, ReferrersAsync, "Profiles and bindings that use a resource", "Resources", null, typeof(List<ResourceReferrer>));
            routes.Scoped(HttpMethod.POST, _Base + "/{id}/invoke", ResourceTypeEnum.Resource, OperationTypeEnum.Execute, InvokeAsync, "Invoke a tool on a resource directly, without a profile (access mode is the resource ceiling or ReadWriteWithApproval); 202 while approval is pending", "Resources", typeof(ToolCallRequest), typeof(ToolInvocationOutcome));
            routes.Scoped(HttpMethod.GET, _Base + "/{id}/knowledge", ResourceTypeEnum.Knowledge, OperationTypeEnum.Read, KnowledgeListAsync, "List a resource's knowledge entries", "Knowledge", null, typeof(List<ResourceKnowledge>));
            routes.Scoped(HttpMethod.GET, _Base + "/{id}/knowledge/{scope}", ResourceTypeEnum.Knowledge, OperationTypeEnum.Read, KnowledgeScopeAsync, "List the knowledge entries of one scope", "Knowledge", null, typeof(List<ResourceKnowledge>));
            routes.Scoped(HttpMethod.PUT, _Base + "/{id}/knowledge/{scope}", ResourceTypeEnum.Knowledge, OperationTypeEnum.Update, KnowledgePutAsync, "Write a scope's root knowledge entry (operator edit; scrubbed and audited)", "Knowledge", typeof(KnowledgeRequest), typeof(ResourceKnowledge));
            routes.Scoped(HttpMethod.DELETE, _Base + "/{id}/knowledge/{scope}", ResourceTypeEnum.Knowledge, OperationTypeEnum.Delete, KnowledgeDeleteAsync, "Delete a scope's root knowledge entry", "Knowledge");
            routes.Scoped(HttpMethod.GET, _Base + "/{id}/knowledge/{scope}/{key}", ResourceTypeEnum.Knowledge, OperationTypeEnum.Read, KnowledgeReadAsync, "Read one knowledge entry (key _root for the scope root)", "Knowledge", null, typeof(ResourceKnowledge));
            routes.Scoped(HttpMethod.PUT, _Base + "/{id}/knowledge/{scope}/{key}", ResourceTypeEnum.Knowledge, OperationTypeEnum.Update, KnowledgePutAsync, "Write one knowledge entry (operator edit; scrubbed and audited)", "Knowledge", typeof(KnowledgeRequest), typeof(ResourceKnowledge));
            routes.Scoped(HttpMethod.DELETE, _Base + "/{id}/knowledge/{scope}/{key}", ResourceTypeEnum.Knowledge, OperationTypeEnum.Delete, KnowledgeDeleteAsync, "Delete one knowledge entry", "Knowledge");
            routes.Scoped(HttpMethod.GET, _Base + "/{id}/knowledge/{scope}/{key}/versions", ResourceTypeEnum.Knowledge, OperationTypeEnum.Read, KnowledgeVersionsAsync, "Version history of one knowledge entry, newest first", "Knowledge", null, typeof(List<ResourceKnowledgeVersion>));
        }

        #endregion

        #region Private-Methods

        private async Task ListAsync(HttpContextBase http)
        {
            EnumerationResult<Resource> page = await _Resources.EnumerateAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), RouteHelpers.Enumeration(http), RouteHelpers.Query(http, "connectorType"), http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, page).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase http)
        {
            Resource created = await _Resources.CreateAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), RouteHelpers.RequiredBody<ResourceRequest>(http), http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 201, created).ConfigureAwait(false);
        }

        private async Task TestUnsavedAsync(HttpContextBase http)
        {
            ResourceTestResponse result = await _Resources.TestUnsavedAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), RouteHelpers.RequiredBody<ResourceRequest>(http), http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, result).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase http)
        {
            await RouteHelpers.JsonAsync(http, 200, await _Resources.ReadAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), RouteHelpers.Param(http, "id")!, http.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase http)
        {
            Resource updated = await _Resources.UpdateAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), RouteHelpers.Param(http, "id")!, RouteHelpers.RequiredBody<ResourceRequest>(http), http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, updated).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase http)
        {
            await _Resources.DeleteAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), RouteHelpers.Param(http, "id")!, http.Token).ConfigureAwait(false);
            await RouteHelpers.NoContentAsync(http).ConfigureAwait(false);
        }

        private async Task TestAsync(HttpContextBase http)
        {
            ResourceTestResponse result = await _Resources.TestAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), RouteHelpers.Param(http, "id")!, http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, result).ConfigureAwait(false);
        }

        private async Task DuplicateAsync(HttpContextBase http)
        {
            CloneRequest request = RouteHelpers.RequiredBody<CloneRequest>(http);
            Resource copy = await _Resources.DuplicateAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), RouteHelpers.Param(http, "id")!, request.Name, http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 201, copy).ConfigureAwait(false);
        }

        private async Task ReferrersAsync(HttpContextBase http)
        {
            await RouteHelpers.JsonAsync(http, 200, await _Resources.ReferrersAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), RouteHelpers.Param(http, "id")!, http.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task InvokeAsync(HttpContextBase http)
        {
            RequestContext ctx = RouteHelpers.Context(http);
            ToolCallRequest body = RouteHelpers.RequiredBody<ToolCallRequest>(http);
            InvocationRequest request = new InvocationRequest();
            request.Principal = ctx;
            request.TenantId = RouteHelpers.Tenant(http);
            request.ResourceId = RouteHelpers.Param(http, "id");
            request.ToolName = body.ToolName;
            request.ArgumentsJson = body.Arguments.HasValue ? body.Arguments.Value.GetRawText() : "{}";
            request.Surface = InvocationSurfaceEnum.Rest;
            request.WaitMode = ApprovalWaitModeEnum.NoWait;
            request.AdminResourceInvoke = true;
            request.CorrelationId = ctx.RequestId;
            ToolInvocationOutcome outcome = await _Server.Get<ToolInvocationService>().InvokeAsync(request, http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, outcome.Decision == InvocationDecisionEnum.ApprovalRequired ? 202 : 200, outcome).ConfigureAwait(false);
        }

        private async Task<Resource> ResourceAsync(HttpContextBase http)
        {
            return await _Resources.ReadAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), RouteHelpers.Param(http, "id")!, http.Token).ConfigureAwait(false);
        }

        private async Task KnowledgeListAsync(HttpContextBase http)
        {
            Resource resource = await ResourceAsync(http).ConfigureAwait(false);
            List<ResourceKnowledge> entries = await _Server.Database.ResourceKnowledge.ListAsync(resource.TenantId, resource.Id, null, http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, entries).ConfigureAwait(false);
        }

        private async Task KnowledgeScopeAsync(HttpContextBase http)
        {
            Resource resource = await ResourceAsync(http).ConfigureAwait(false);
            List<ResourceKnowledge> entries = await _Server.Database.ResourceKnowledge.ListAsync(resource.TenantId, resource.Id, RouteHelpers.Param(http, "scope") ?? String.Empty, http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, entries).ConfigureAwait(false);
        }

        private async Task KnowledgeReadAsync(HttpContextBase http)
        {
            Resource resource = await ResourceAsync(http).ConfigureAwait(false);
            ResourceKnowledge entry = await _Server.Database.ResourceKnowledge.ReadAsync(resource.TenantId, resource.Id, RouteHelpers.Param(http, "scope") ?? String.Empty, Key(http), http.Token).ConfigureAwait(false) ?? throw new NotFoundException("Knowledge entry not found.");
            await RouteHelpers.JsonAsync(http, 200, entry).ConfigureAwait(false);
        }

        private async Task KnowledgeVersionsAsync(HttpContextBase http)
        {
            Resource resource = await ResourceAsync(http).ConfigureAwait(false);
            List<ResourceKnowledgeVersion> versions = await _Server.Database.ResourceKnowledge.ListVersionsAsync(resource.TenantId, resource.Id, RouteHelpers.Param(http, "scope") ?? String.Empty, Key(http), http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, versions).ConfigureAwait(false);
        }

        private async Task KnowledgePutAsync(HttpContextBase http)
        {
            RequestContext ctx = RouteHelpers.Context(http);
            Resource resource = await ResourceAsync(http).ConfigureAwait(false);
            KnowledgeRequest request = RouteHelpers.RequiredBody<KnowledgeRequest>(http);
            string scope = RouteHelpers.Param(http, "scope") ?? String.Empty;
            if (String.IsNullOrWhiteSpace(scope) || scope.Length > 128) throw new ValidationException("The scope must be 1 to 128 characters.", new Dictionary<string, string> { { "scope", "Invalid scope." } });
            if (String.IsNullOrWhiteSpace(request.Content)) throw new ValidationException("Content is required.", new Dictionary<string, string> { { "content", "Required." } });
            ServerResourceKnowledgeStore store = Store(ctx, resource);
            ResourceKnowledgeEntry entry = new ResourceKnowledgeEntry();
            entry.Scope = scope;
            entry.Key = Key(http);
            entry.Content = request.Content;
            entry.Source = request.Source ?? KnowledgeSourceEnum.Operator;
            await store.PutAsync(entry, http.Token).ConfigureAwait(false);
            ResourceKnowledge stored = await _Server.Database.ResourceKnowledge.ReadAsync(resource.TenantId, resource.Id, entry.Scope, entry.Key, http.Token).ConfigureAwait(false) ?? throw new NotFoundException("Knowledge entry not found.");
            await RouteHelpers.JsonAsync(http, 200, stored).ConfigureAwait(false);
        }

        private async Task KnowledgeDeleteAsync(HttpContextBase http)
        {
            RequestContext ctx = RouteHelpers.Context(http);
            Resource resource = await ResourceAsync(http).ConfigureAwait(false);
            bool deleted = await Store(ctx, resource).DeleteAsync(RouteHelpers.Param(http, "scope") ?? String.Empty, Key(http), http.Token).ConfigureAwait(false);
            if (!deleted) throw new NotFoundException("Knowledge entry not found.");
            await RouteHelpers.NoContentAsync(http).ConfigureAwait(false);
        }

        private ServerResourceKnowledgeStore Store(RequestContext ctx, Resource resource)
        {
            ServerResourceKnowledgeStore store = new ServerResourceKnowledgeStore(_Server.Database, _Server.Audit, resource.TenantId, resource.Id, ctx, ctx.PrincipalName ?? ctx.PrincipalId ?? "operator", true, null);
            store.Changed += (sender, tenantId) => _Server.TryGet<Hugo.Core.Instructions.InstructionCompiler>()?.Invalidate(tenantId);
            return store;
        }

        private static string Key(HttpContextBase http)
        {
            string? key = RouteHelpers.Param(http, "key");
            if (String.IsNullOrEmpty(key) || key == _RootKey) return String.Empty;
            return Uri.UnescapeDataString(key);
        }

        #endregion
    }
}
