namespace Hugo.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Inference;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;
    using Hugo.Server.Services;
    using WatsonWebserver.Core;

    /// <summary>
    /// Model endpoint routes (P6.5): CRUD, validate (records health), and the provider's model list. API keys are vault
    /// secret references and are never returned.
    /// </summary>
    public class ModelEndpointRoutes
    {
        #region Private-Members

        private static readonly string _Base = "/v1.0/api/model-endpoints";
        private readonly ServerContext _Server;
        private readonly ModelEndpointService _Models;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="server">Server context.</param>
        /// <param name="models">Model endpoint service.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public ModelEndpointRoutes(ServerContext server, ModelEndpointService models)
        {
            _Server = server ?? throw new ArgumentNullException(nameof(server));
            _Models = models ?? throw new ArgumentNullException(nameof(models));
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
            routes.Scoped(HttpMethod.GET, _Base, ResourceTypeEnum.ModelEndpoint, OperationTypeEnum.Read, ListAsync, "List model endpoints", "Model Endpoints", null, typeof(EnumerationResult<ModelEndpoint>));
            routes.Scoped(HttpMethod.POST, _Base, ResourceTypeEnum.ModelEndpoint, OperationTypeEnum.Create, CreateAsync, "Create a model endpoint (API key as a vault secret reference)", "Model Endpoints", typeof(ModelEndpointRequest), typeof(ModelEndpoint));
            routes.Scoped(HttpMethod.GET, _Base + "/{id}", ResourceTypeEnum.ModelEndpoint, OperationTypeEnum.Read, ReadAsync, "Read a model endpoint", "Model Endpoints", null, typeof(ModelEndpoint));
            routes.Scoped(HttpMethod.PUT, _Base + "/{id}", ResourceTypeEnum.ModelEndpoint, OperationTypeEnum.Update, UpdateAsync, "Update a model endpoint", "Model Endpoints", typeof(ModelEndpointRequest), typeof(ModelEndpoint));
            routes.Scoped(HttpMethod.DELETE, _Base + "/{id}", ResourceTypeEnum.ModelEndpoint, OperationTypeEnum.Delete, DeleteAsync, "Delete a model endpoint (profiles lose the reference)", "Model Endpoints");
            routes.Scoped(HttpMethod.POST, _Base + "/{id}/validate", ResourceTypeEnum.ModelEndpoint, OperationTypeEnum.Update, ValidateAsync, "Send a tiny prompt (or embed a short string), record latency, dimensions, and health", "Model Endpoints", null, typeof(ModelValidationResult));
            routes.Scoped(HttpMethod.GET, _Base + "/{id}/models", ResourceTypeEnum.ModelEndpoint, OperationTypeEnum.Read, ModelsAsync, "Models the provider offers (Ollama, OpenAI, OpenAI-compatible, Anthropic, Gemini)", "Model Endpoints", null, typeof(List<string>));
        }

        #endregion

        #region Private-Methods

        private async Task ListAsync(HttpContextBase http)
        {
            await RouteHelpers.JsonAsync(http, 200, await _Server.Database.ModelEndpoints.EnumerateAsync(RouteHelpers.Tenant(http), RouteHelpers.Enumeration(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase http)
        {
            ModelEndpoint created = await _Models.CreateAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), RouteHelpers.RequiredBody<ModelEndpointRequest>(http), http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 201, created).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase http)
        {
            await RouteHelpers.JsonAsync(http, 200, await _Models.ReadAsync(RouteHelpers.Tenant(http), RouteHelpers.Param(http, "id")!, http.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase http)
        {
            ModelEndpoint updated = await _Models.UpdateAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), RouteHelpers.Param(http, "id")!, RouteHelpers.RequiredBody<ModelEndpointRequest>(http), http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, updated).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase http)
        {
            await _Models.DeleteAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), RouteHelpers.Param(http, "id")!, http.Token).ConfigureAwait(false);
            await RouteHelpers.NoContentAsync(http).ConfigureAwait(false);
        }

        private async Task ValidateAsync(HttpContextBase http)
        {
            ModelValidationResult result = await _Models.ValidateAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), RouteHelpers.Param(http, "id")!, http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, result).ConfigureAwait(false);
        }

        private async Task ModelsAsync(HttpContextBase http)
        {
            await RouteHelpers.JsonAsync(http, 200, await _Models.ListProviderModelsAsync(RouteHelpers.Tenant(http), RouteHelpers.Param(http, "id")!, http.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
