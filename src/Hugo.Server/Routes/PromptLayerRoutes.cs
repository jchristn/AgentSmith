namespace Hugo.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Prompts;
    using Hugo.Core.Requests;
    using Hugo.Core.Responses;
    using Hugo.Core.Security;
    using Hugo.Core.Services;
    using Hugo.Core.Templates;
    using WatsonWebserver.Core;

    /// <summary>
    /// Layered prompt template routes (P5.7, plan sections 10.2 and 14.2) on the flat path /v1.0/api/prompt-templates,
    /// next to Garrison's per-row routes under /v1.0/api/tenants/{tenantId}/prompt-templates. Templates are addressed by
    /// key; ?layer=Tenant (default) or ?layer=Global selects the editable layer. The Global layer is editable only by
    /// system administrators. Every save creates a version and is audited (PromptTemplateChanged).
    /// </summary>
    public class PromptLayerRoutes
    {
        #region Private-Members

        private static readonly string _Base = "/v1.0/api/prompt-templates";
        private readonly ServerContext _Server;
        private readonly PromptTemplateService _Templates;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="server">Server context.</param>
        /// <param name="templates">Layered templates.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public PromptLayerRoutes(ServerContext server, PromptTemplateService templates)
        {
            _Server = server ?? throw new ArgumentNullException(nameof(server));
            _Templates = templates ?? throw new ArgumentNullException(nameof(templates));
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
            routes.Scoped(HttpMethod.GET, _Base, ResourceTypeEnum.PromptTemplate, OperationTypeEnum.Read, ListAsync, "Every template key with its Built-in, Global, and Tenant layers and the layer in effect", "Prompt Templates", null, typeof(List<PromptTemplateView>));
            routes.Scoped(HttpMethod.POST, _Base + "/render", ResourceTypeEnum.PromptTemplate, OperationTypeEnum.Read, RenderAsync, "Render template content with sample data; includes resolve through the layers", "Prompt Templates", typeof(PromptRenderRequest), typeof(PromptRenderResponse));
            routes.Scoped(HttpMethod.GET, _Base + "/{key}", ResourceTypeEnum.PromptTemplate, OperationTypeEnum.Read, ReadAsync, "One template key's layers", "Prompt Templates", null, typeof(PromptTemplateView), "key");
            routes.Scoped(HttpMethod.PUT, _Base + "/{key}", ResourceTypeEnum.PromptTemplate, OperationTypeEnum.Update, SaveAsync, "Save content at ?layer=Tenant (default) or ?layer=Global (system administrators); creates a version", "Prompt Templates", typeof(PromptLayerRequest), typeof(PromptTemplateView), "key");
            routes.Scoped(HttpMethod.POST, _Base + "/{key}/reset", ResourceTypeEnum.PromptTemplate, OperationTypeEnum.Update, ResetAsync, "Reset ?layer=Tenant to the Global default, or ?layer=Global to the Built-in (adopts a changed Built-in)", "Prompt Templates", null, typeof(PromptTemplateView), "key");
            routes.Scoped(HttpMethod.GET, _Base + "/{key}/versions", ResourceTypeEnum.PromptTemplate, OperationTypeEnum.Read, VersionsAsync, "Version history of ?layer=Tenant (default) or ?layer=Global, newest first", "Prompt Templates", null, typeof(List<PromptTemplateVersion>), "key");
            routes.Scoped(HttpMethod.GET, _Base + "/{key}/builtin", ResourceTypeEnum.PromptTemplate, OperationTypeEnum.Read, BuiltInAsync, "The shipped Built-in content of a key", "Prompt Templates", null, typeof(BuiltInTemplate), "key");
            routes.Scoped(HttpMethod.GET, _Base + "/{key}/diff", ResourceTypeEnum.PromptTemplate, OperationTypeEnum.Read, DiffAsync, "Line diff of ?layer=Tenant (default) or ?layer=Global against the layer below it", "Prompt Templates", null, typeof(List<PromptDiffLine>), "key");
        }

        #endregion

        #region Private-Methods

        private async Task ListAsync(HttpContextBase http)
        {
            await RouteHelpers.JsonAsync(http, 200, await _Templates.ViewsAsync(RouteHelpers.Tenant(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase http)
        {
            PromptTemplateView view = await _Templates.ViewAsync(RouteHelpers.Tenant(http), Key(http), http.Token).ConfigureAwait(false) ?? throw new NotFoundException("Prompt template " + Key(http) + " not found.");
            await RouteHelpers.JsonAsync(http, 200, view).ConfigureAwait(false);
        }

        private async Task SaveAsync(HttpContextBase http)
        {
            RequestContext ctx = RouteHelpers.Context(http);
            PromptLayerEnum layer = Layer(http);
            PromptLayerRequest request = RouteHelpers.RequiredBody<PromptLayerRequest>(http);
            string tenantId = RouteHelpers.Tenant(http);
            PromptTemplateView view = await _Templates.SaveAsync(tenantId, layer, Key(http), request.Content, request.ChangeNote, ctx.PrincipalName, http.Token).ConfigureAwait(false);
            await ConfigurationSupport.AuditAsync(_Server, http, AuditEventTypeEnum.PromptTemplateChanged, "PromptTemplate", Key(http), "Saved " + layer + " layer of prompt template " + Key(http) + ".").ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, view).ConfigureAwait(false);
        }

        private async Task ResetAsync(HttpContextBase http)
        {
            RequestContext ctx = RouteHelpers.Context(http);
            PromptLayerEnum layer = Layer(http);
            PromptTemplateView view = await _Templates.ResetAsync(RouteHelpers.Tenant(http), layer, Key(http), ctx.PrincipalName, http.Token).ConfigureAwait(false);
            await ConfigurationSupport.AuditAsync(_Server, http, AuditEventTypeEnum.PromptTemplateChanged, "PromptTemplate", Key(http), "Reset " + layer + " layer of prompt template " + Key(http) + ".").ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, view).ConfigureAwait(false);
        }

        private async Task VersionsAsync(HttpContextBase http)
        {
            PromptLayerEnum layer = RouteHelpers.QueryEnum<PromptLayerEnum>(http, "layer") ?? PromptLayerEnum.Tenant;
            await RouteHelpers.JsonAsync(http, 200, await _Templates.VersionsAsync(RouteHelpers.Tenant(http), layer, Key(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task BuiltInAsync(HttpContextBase http)
        {
            BuiltInTemplate template = _Templates.BuiltIns.Get(Key(http)) ?? throw new NotFoundException("Prompt template " + Key(http) + " has no built-in default.");
            await RouteHelpers.JsonAsync(http, 200, template).ConfigureAwait(false);
        }

        private async Task DiffAsync(HttpContextBase http)
        {
            PromptLayerEnum layer = RouteHelpers.QueryEnum<PromptLayerEnum>(http, "layer") ?? PromptLayerEnum.Tenant;
            PromptTemplateView view = await _Templates.ViewAsync(RouteHelpers.Tenant(http), Key(http), http.Token).ConfigureAwait(false) ?? throw new NotFoundException("Prompt template " + Key(http) + " not found.");
            string? below = layer == PromptLayerEnum.Global ? view.BuiltInContent : (view.Global != null && view.Global.Exists ? view.Global.Content : view.BuiltInContent);
            string? content = layer == PromptLayerEnum.Global ? view.Global?.Content : view.Tenant?.Content;
            await RouteHelpers.JsonAsync(http, 200, PromptTemplateService.Diff(below, content ?? below)).ConfigureAwait(false);
        }

        private async Task RenderAsync(HttpContextBase http)
        {
            PromptRenderRequest request = RouteHelpers.RequiredBody<PromptRenderRequest>(http);
            Dictionary<string, ResolvedPromptTemplate> resolved = await _Templates.ResolveAllAsync(RouteHelpers.Tenant(http), http.Token).ConfigureAwait(false);
            PromptRenderResponse response = new PromptRenderResponse();
            string content = request.Content ?? String.Empty;
            TemplateAnalysis analysis = TemplateAnalyzer.Analyze(content, "preview", false);
            response.Variables = analysis.Variables;
            response.Includes = analysis.Includes;
            response.Errors.AddRange(analysis.Errors);
            if (!analysis.Valid)
            {
                response.Success = false;
                await RouteHelpers.JsonAsync(http, 200, response).ConfigureAwait(false);
                return;
            }
            TemplateRenderOptions options = new TemplateRenderOptions();
            options.TemplateName = "preview";
            options.IncludeResolver = name =>
            {
                string key = name == "persona" ? BuiltInTemplateCatalog.DefaultPersonaKey : name;
                return resolved.TryGetValue(key, out ResolvedPromptTemplate? t) ? t.Content : null;
            };
            try
            {
                response.Rendered = TemplateRenderer.Render(content, TemplateSampleContext.Build(request.Context), options);
            }
            catch (TemplateException e)
            {
                response.Success = false;
                response.Errors.Add(e.Message);
            }
            await RouteHelpers.JsonAsync(http, 200, response).ConfigureAwait(false);
        }

        private PromptLayerEnum Layer(HttpContextBase http)
        {
            PromptLayerEnum layer = RouteHelpers.QueryEnum<PromptLayerEnum>(http, "layer") ?? PromptLayerEnum.Tenant;
            if (layer == PromptLayerEnum.BuiltIn) throw new ValidationException("The Built-in layer cannot be changed.", new Dictionary<string, string> { { "layer", "Use Tenant or Global." } });
            if (layer == PromptLayerEnum.Global && !RouteHelpers.Context(http).IsAdmin) throw new ForbiddenException("SystemAdminRequired", "Only system administrators can change the Global layer.");
            return layer;
        }

        private static string Key(HttpContextBase http)
        {
            return Uri.UnescapeDataString(RouteHelpers.Param(http, "key") ?? String.Empty);
        }

        #endregion
    }
}
