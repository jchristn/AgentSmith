namespace Hugo.Sdk.Clients
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Sdk.Enums;
    using Hugo.Sdk.Http;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Requests;
    using Hugo.Sdk.Responses;

    /// <summary>
    /// Layered prompt templates by key (/v1.0/api/prompt-templates): persona, connector instructions, and system
    /// templates with Built-in, Global, and Tenant layers. The Tenant layer is the default; the Global layer needs a
    /// system administrator.
    /// </summary>
    public class PromptLayersClient : ClientBase
    {
        #region Private-Members

        private static readonly string _Base = "/v1.0/api/prompt-templates";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="transport">Transport.</param>
        public PromptLayersClient(HugoTransport transport) : base(transport)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Every template key with its layers and the layer in effect.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Template views.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<List<PromptTemplateView>> ListAsync(CancellationToken token = default)
        {
            return GetAsync<List<PromptTemplateView>>(_Base, null, token);
        }

        /// <summary>
        /// One template key's layers.
        /// </summary>
        /// <param name="key">Template key, for example persona.agent-smith.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Template view.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<PromptTemplateView> GetAsync(string key, CancellationToken token = default)
        {
            return GetAsync<PromptTemplateView>(_Base + "/" + Segment(key), null, token);
        }

        /// <summary>
        /// Save content on a layer, creating a new version.
        /// </summary>
        /// <param name="key">Template key.</param>
        /// <param name="content">Content.</param>
        /// <param name="changeNote">Optional change note.</param>
        /// <param name="layer">Tenant (default) or Global (system administrators).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated template view.</returns>
        /// <exception cref="ArgumentNullException">Thrown when content is null.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status (400 when the template does not parse).</exception>
        public Task<PromptTemplateView> SaveAsync(string key, string content, string? changeNote = null, PromptLayerEnum layer = PromptLayerEnum.Tenant, CancellationToken token = default)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            PromptLayerRequest request = new PromptLayerRequest();
            request.Content = content;
            request.ChangeNote = changeNote;
            return Transport.SendRequiredAsync<PromptTemplateView>(HttpMethod.Put, _Base + "/" + Segment(key), request, Layer(layer), token);
        }

        /// <summary>
        /// Reset a layer: Tenant back to the Global default, or Global back to the Built-in.
        /// </summary>
        /// <param name="key">Template key.</param>
        /// <param name="layer">Layer to reset. Default Tenant.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated template view.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<PromptTemplateView> ResetAsync(string key, PromptLayerEnum layer = PromptLayerEnum.Tenant, CancellationToken token = default)
        {
            return Transport.SendRequiredAsync<PromptTemplateView>(HttpMethod.Post, _Base + "/" + Segment(key) + "/reset", null, Layer(layer), token);
        }

        /// <summary>
        /// Version history of a layer, newest first.
        /// </summary>
        /// <param name="key">Template key.</param>
        /// <param name="layer">Layer. Default Tenant.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Versions.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<List<PromptTemplateVersion>> GetVersionsAsync(string key, PromptLayerEnum layer = PromptLayerEnum.Tenant, CancellationToken token = default)
        {
            return GetAsync<List<PromptTemplateVersion>>(_Base + "/" + Segment(key) + "/versions", Layer(layer), token);
        }

        /// <summary>
        /// The shipped Built-in content of a key.
        /// </summary>
        /// <param name="key">Template key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Built-in template.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<BuiltInTemplate> GetBuiltInAsync(string key, CancellationToken token = default)
        {
            return GetAsync<BuiltInTemplate>(_Base + "/" + Segment(key) + "/builtin", null, token);
        }

        /// <summary>
        /// Line diff of a layer against the layer below it.
        /// </summary>
        /// <param name="key">Template key.</param>
        /// <param name="layer">Layer. Default Tenant.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Diff lines.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<List<PromptDiffLine>> GetDiffAsync(string key, PromptLayerEnum layer = PromptLayerEnum.Tenant, CancellationToken token = default)
        {
            return GetAsync<List<PromptDiffLine>>(_Base + "/" + Segment(key) + "/diff", Layer(layer), token);
        }

        /// <summary>
        /// Render template content with sample data; includes resolve through the layers.
        /// </summary>
        /// <param name="content">Template content.</param>
        /// <param name="context">Sample data object, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Render result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when content is null.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<PromptRenderResponse> RenderAsync(string content, object? context = null, CancellationToken token = default)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            PromptRenderRequest request = new PromptRenderRequest();
            request.Content = content;
            request.Context = context;
            return PostAsync<PromptRenderResponse>(_Base + "/render", request, token);
        }

        #endregion

        #region Private-Methods

        private static Dictionary<string, string?> Layer(PromptLayerEnum layer)
        {
            return new Dictionary<string, string?> { { "layer", layer.ToString() } };
        }

        #endregion
    }
}
