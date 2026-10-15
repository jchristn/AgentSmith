namespace Hugo.Sdk.Clients
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Sdk.Enumeration;
    using Hugo.Sdk.Http;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Requests;
    using Hugo.Sdk.Responses;

    /// <summary>
    /// Resources and their knowledge entries (/v1.0/api/resources; the tenant comes from the credentials).
    /// </summary>
    public class ResourcesClient : CrudClientBase<Resource, ResourceRequest>
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="transport">Transport.</param>
        public ResourcesClient(HugoTransport transport) : base(transport, "resources", false)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// List resources of one connector type.
        /// </summary>
        /// <param name="connectorType">Connector type, for example ssh or http.</param>
        /// <param name="query">Optional paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<EnumerationResult<Resource>> ListByConnectorTypeAsync(string connectorType, EnumerationQuery? query = null, CancellationToken token = default)
        {
            return ListAsync(query, new Dictionary<string, string?> { { "connectorType", connectorType } }, token);
        }

        /// <summary>
        /// List one page of resources with filters. Unset filter properties are not sent.
        /// </summary>
        /// <param name="filter">Filter.</param>
        /// <param name="query">Optional paging, ordering, and search.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when filter is null.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status (400 ValidationFailed listing the allowed values when health is not Healthy, Unhealthy, or Unknown).</exception>
        public Task<EnumerationResult<Resource>> ListAsync(ResourceFilter filter, EnumerationQuery? query = null, CancellationToken token = default)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            Dictionary<string, string?> filters = new Dictionary<string, string?>
            {
                { "connectorType", filter.ConnectorType },
                { "environment", filter.Environment },
                { "health", filter.Health }
            };
            return ListAsync(query, filters, token);
        }

        /// <summary>
        /// Test a saved resource's connection and record the result.
        /// </summary>
        /// <param name="id">Resource identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Test result.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<ResourceTestResult> TestAsync(string id, CancellationToken token = default)
        {
            return PostAsync<ResourceTestResult>(Item(id) + "/test", null, token);
        }

        /// <summary>
        /// Validate and test unsaved resource settings. Nothing is stored.
        /// </summary>
        /// <param name="request">Resource definition.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Test result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<ResourceTestResult> TestUnsavedAsync(ResourceRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return PostAsync<ResourceTestResult>(Collection() + "/test", request, token);
        }

        /// <summary>
        /// Copy a resource with its settings and secret slots under a new name.
        /// </summary>
        /// <param name="id">Resource identifier.</param>
        /// <param name="name">Name of the copy.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>New resource.</returns>
        /// <exception cref="ArgumentNullException">Thrown when name is null or empty.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<Resource> DuplicateAsync(string id, string name, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            CloneRequest request = new CloneRequest();
            request.Name = name;
            return PostAsync<Resource>(Item(id) + "/duplicate", request, token);
        }

        /// <summary>
        /// Profiles and bindings that use a resource.
        /// </summary>
        /// <param name="id">Resource identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Referrers.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<List<ResourceReferrer>> GetReferrersAsync(string id, CancellationToken token = default)
        {
            return GetAsync<List<ResourceReferrer>>(Item(id) + "/referrers", null, token);
        }

        /// <summary>
        /// Invoke a tool on a resource directly, without a profile. The server answers 202 with the approval while
        /// approval is pending; the outcome then carries ApprovalId.
        /// </summary>
        /// <param name="id">Resource identifier.</param>
        /// <param name="toolName">Tool name, for example sql_query.</param>
        /// <param name="arguments">Tool arguments object, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Outcome.</returns>
        /// <exception cref="ArgumentNullException">Thrown when toolName is null or empty.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<ToolInvocationOutcome> InvokeAsync(string id, string toolName, object? arguments = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(toolName)) throw new ArgumentNullException(nameof(toolName));
            ToolCallRequest request = new ToolCallRequest();
            request.ResourceId = id;
            request.ToolName = toolName;
            request.Arguments = arguments;
            return PostAsync<ToolInvocationOutcome>(Item(id) + "/invoke", request, token);
        }

        /// <summary>
        /// List a resource's knowledge entries.
        /// </summary>
        /// <param name="id">Resource identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entries.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<List<ResourceKnowledge>> ListKnowledgeAsync(string id, CancellationToken token = default)
        {
            return GetAsync<List<ResourceKnowledge>>(Item(id) + "/knowledge", null, token);
        }

        /// <summary>
        /// List the knowledge entries of one scope.
        /// </summary>
        /// <param name="id">Resource identifier.</param>
        /// <param name="scope">Scope, for example database or table.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entries.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<List<ResourceKnowledge>> ListKnowledgeScopeAsync(string id, string scope, CancellationToken token = default)
        {
            return GetAsync<List<ResourceKnowledge>>(Item(id) + "/knowledge/" + Segment(scope), null, token);
        }

        /// <summary>
        /// Read one knowledge entry.
        /// </summary>
        /// <param name="id">Resource identifier.</param>
        /// <param name="scope">Scope.</param>
        /// <param name="key">Key; _root for the scope root.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entry.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<ResourceKnowledge> GetKnowledgeAsync(string id, string scope, string key, CancellationToken token = default)
        {
            return GetAsync<ResourceKnowledge>(Item(id) + "/knowledge/" + Segment(scope) + "/" + Segment(key), null, token);
        }

        /// <summary>
        /// Write a knowledge entry (operator edit; scrubbed and audited).
        /// </summary>
        /// <param name="id">Resource identifier.</param>
        /// <param name="scope">Scope.</param>
        /// <param name="key">Key, or null (or _root) to write the scope root.</param>
        /// <param name="content">Content.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Stored entry.</returns>
        /// <exception cref="ArgumentNullException">Thrown when content is null.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<ResourceKnowledge> PutKnowledgeAsync(string id, string scope, string? key, string content, CancellationToken token = default)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            KnowledgeRequest request = new KnowledgeRequest();
            request.Content = content;
            return PutAsync<ResourceKnowledge>(KnowledgePath(id, scope, key), request, token);
        }

        /// <summary>
        /// Delete a knowledge entry.
        /// </summary>
        /// <param name="id">Resource identifier.</param>
        /// <param name="scope">Scope.</param>
        /// <param name="key">Key, or null to delete the scope root.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task DeleteKnowledgeAsync(string id, string scope, string? key = null, CancellationToken token = default)
        {
            return DeleteAsync(KnowledgePath(id, scope, key), null, token);
        }

        /// <summary>
        /// Version history of one knowledge entry, newest first.
        /// </summary>
        /// <param name="id">Resource identifier.</param>
        /// <param name="scope">Scope.</param>
        /// <param name="key">Key; _root for the scope root.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Versions.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<List<ResourceKnowledgeVersion>> GetKnowledgeVersionsAsync(string id, string scope, string key, CancellationToken token = default)
        {
            return GetAsync<List<ResourceKnowledgeVersion>>(Item(id) + "/knowledge/" + Segment(scope) + "/" + Segment(key) + "/versions", null, token);
        }

        #endregion

        #region Private-Methods

        private string KnowledgePath(string id, string scope, string? key)
        {
            return Item(id) + "/knowledge/" + Segment(scope) + (String.IsNullOrEmpty(key) ? String.Empty : "/" + Segment(key));
        }

        #endregion
    }
}
