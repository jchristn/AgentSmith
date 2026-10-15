namespace Hugo.Sdk.Clients
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Sdk.Http;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Requests;
    using Hugo.Sdk.Responses;

    /// <summary>
    /// Prompt templates in the client's tenant, including built-ins (/v1.0/api/tenants/{tenantId}/prompt-templates).
    /// </summary>
    public class PromptTemplatesClient : CrudClientBase<PromptTemplate, PromptTemplateRequest>
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="transport">Transport.</param>
        public PromptTemplatesClient(HugoTransport transport) : base(transport, "prompt-templates", true)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// List a template's versions, newest first.
        /// </summary>
        /// <param name="id">Template identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Versions.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<List<PromptTemplateVersion>> GetVersionsAsync(string id, CancellationToken token = default)
        {
            return GetAsync<List<PromptTemplateVersion>>(Item(id) + "/versions", null, token);
        }

        /// <summary>
        /// Read one version of a template.
        /// </summary>
        /// <param name="id">Template identifier.</param>
        /// <param name="version">Version number.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Version.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<PromptTemplateVersion> GetVersionAsync(string id, int version, CancellationToken token = default)
        {
            return GetAsync<PromptTemplateVersion>(Item(id) + "/versions/" + version.ToString(CultureInfo.InvariantCulture), null, token);
        }

        /// <summary>
        /// Restore a version as a new version.
        /// </summary>
        /// <param name="id">Template identifier.</param>
        /// <param name="version">Version to restore.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated template.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<PromptTemplate> RestoreAsync(string id, int version, CancellationToken token = default)
        {
            return PostAsync<PromptTemplate>(Item(id) + "/restore/" + version.ToString(CultureInfo.InvariantCulture), null, token);
        }

        /// <summary>
        /// Clone a template (built-in or tenant) under a new name.
        /// </summary>
        /// <param name="id">Template identifier.</param>
        /// <param name="name">Name of the copy.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>New template.</returns>
        /// <exception cref="ArgumentNullException">Thrown when name is null or empty.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<PromptTemplate> CloneAsync(string id, string name, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            CloneRequest request = new CloneRequest();
            request.Name = name;
            return PostAsync<PromptTemplate>(Item(id) + "/clone", request, token);
        }

        /// <summary>
        /// Reset a cloned built-in template to the shipped default.
        /// </summary>
        /// <param name="id">Template identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated template.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<PromptTemplate> ResetAsync(string id, CancellationToken token = default)
        {
            return PostAsync<PromptTemplate>(Item(id) + "/reset", null, token);
        }

        /// <summary>
        /// Render a stored template with sample data.
        /// </summary>
        /// <param name="id">Template identifier.</param>
        /// <param name="context">Sample data object, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Render result.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<PromptRenderResponse> RenderAsync(string id, object? context = null, CancellationToken token = default)
        {
            PromptRenderRequest request = new PromptRenderRequest();
            request.Context = context;
            return PostAsync<PromptRenderResponse>(Item(id) + "/render", request, token);
        }

        /// <summary>
        /// Render ad hoc template content with sample data.
        /// </summary>
        /// <param name="content">Template content.</param>
        /// <param name="context">Sample data object, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Render result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when content is null.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<PromptRenderResponse> RenderContentAsync(string content, object? context = null, CancellationToken token = default)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            PromptRenderRequest request = new PromptRenderRequest();
            request.Content = content;
            request.Context = context;
            return PostAsync<PromptRenderResponse>(Collection() + "/render", request, token);
        }

        #endregion
    }
}
