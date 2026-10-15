namespace Hugo.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;
    using Hugo.Core.Responses;
    using Hugo.Core.Services;
    using Hugo.Core.Templates;
    using WatsonWebserver.Core;

    /// <summary>
    /// Prompt template routes: CRUD (updates create versions), versions, restore, clone, reset to the built-in default,
    /// and render previews.
    /// </summary>
    public class PromptTemplateRoutes
    {
        #region Private-Members

        private readonly ServerContext _Server;
        private static readonly string _Base = "/v1.0/api/tenants/{tenantId}/prompt-templates";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="server">Server context.</param>
        /// <exception cref="ArgumentNullException">Thrown when server is null.</exception>
        public PromptTemplateRoutes(ServerContext server)
        {
            _Server = server ?? throw new ArgumentNullException(nameof(server));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="routes">Registrar.</param>
        public void Register(RouteRegistrar routes)
        {
            routes.Scoped(HttpMethod.GET, _Base, ResourceTypeEnum.PromptTemplate, OperationTypeEnum.Read, ListAsync, "List prompt templates (tenant and built-in)", "Prompt Templates", null, typeof(EnumerationResult<PromptTemplate>));
            routes.Scoped(HttpMethod.POST, _Base, ResourceTypeEnum.PromptTemplate, OperationTypeEnum.Create, CreateAsync, "Create a prompt template", "Prompt Templates", typeof(PromptTemplateRequest), typeof(PromptTemplate), statuses: new[] { 201 });
            routes.Scoped(HttpMethod.POST, _Base + "/render", ResourceTypeEnum.PromptTemplate, OperationTypeEnum.Read, RenderAdHocAsync, "Render ad hoc template content with sample data", "Prompt Templates", typeof(PromptRenderRequest), typeof(PromptRenderResponse));
            routes.Scoped(HttpMethod.GET, _Base + "/{id}", ResourceTypeEnum.PromptTemplate, OperationTypeEnum.Read, ReadAsync, "Read a prompt template", "Prompt Templates", null, typeof(PromptTemplate));
            routes.Scoped(HttpMethod.PUT, _Base + "/{id}", ResourceTypeEnum.PromptTemplate, OperationTypeEnum.Update, UpdateAsync, "Update a prompt template (content changes create a version)", "Prompt Templates", typeof(PromptTemplateRequest), typeof(PromptTemplate));
            routes.Scoped(HttpMethod.DELETE, _Base + "/{id}", ResourceTypeEnum.PromptTemplate, OperationTypeEnum.Delete, DeleteAsync, "Delete a prompt template and its versions", "Prompt Templates");
            routes.Scoped(HttpMethod.GET, _Base + "/{id}/versions", ResourceTypeEnum.PromptTemplate, OperationTypeEnum.Read, VersionsAsync, "List template versions, newest first", "Prompt Templates", null, typeof(List<PromptTemplateVersion>));
            routes.Scoped(HttpMethod.GET, _Base + "/{id}/versions/{version}", ResourceTypeEnum.PromptTemplate, OperationTypeEnum.Read, VersionAsync, "Read one template version", "Prompt Templates", null, typeof(PromptTemplateVersion));
            routes.Scoped(HttpMethod.POST, _Base + "/{id}/restore/{version}", ResourceTypeEnum.PromptTemplate, OperationTypeEnum.Update, RestoreAsync, "Restore a version as a new version", "Prompt Templates", null, typeof(PromptTemplate));
            routes.Scoped(HttpMethod.POST, _Base + "/{id}/clone", ResourceTypeEnum.PromptTemplate, OperationTypeEnum.Create, CloneAsync, "Clone a template (built-in or tenant)", "Prompt Templates", typeof(CloneRequest), typeof(PromptTemplate), statuses: new[] { 201 });
            routes.Scoped(HttpMethod.POST, _Base + "/{id}/reset", ResourceTypeEnum.PromptTemplate, OperationTypeEnum.Update, ResetAsync, "Reset a cloned built-in template to the shipped default", "Prompt Templates", null, typeof(PromptTemplate));
            routes.Scoped(HttpMethod.POST, _Base + "/{id}/render", ResourceTypeEnum.PromptTemplate, OperationTypeEnum.Read, RenderAsync, "Render a stored template with sample data", "Prompt Templates", typeof(PromptRenderRequest), typeof(PromptRenderResponse));
        }

        #endregion

        #region Private-Methods

        private async Task ListAsync(HttpContextBase http)
        {
            EnumerationResult<PromptTemplate> result = await _Server.Database.PromptTemplates.EnumerateAsync(RouteHelpers.Tenant(http), RouteHelpers.Enumeration(http), http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, result).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase http)
        {
            string tenantId = RouteHelpers.Tenant(http);
            PromptTemplateRequest request = RouteHelpers.RequiredBody<PromptTemplateRequest>(http);
            ConfigurationValidator.PromptTemplate(request.Name, request.Content);
            await EnsureNameAsync(tenantId, request.Name, null, http).ConfigureAwait(false);

            PromptTemplate template = new PromptTemplate();
            template.TenantId = tenantId;
            Apply(template, request);
            template.CurrentVersion = 1;
            PromptTemplate created = await _Server.Database.PromptTemplates.CreateAsync(template, http.Token).ConfigureAwait(false);
            await AddVersionAsync(created, request.ChangeNote ?? "Created.", http).ConfigureAwait(false);
            await ConfigurationSupport.AuditAsync(_Server, http, AuditEventTypeEnum.PromptTemplateChanged, "PromptTemplate", created.Id, "Created prompt template " + created.Name + ".").ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 201, created).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase http)
        {
            await RouteHelpers.JsonAsync(http, 200, await LoadAsync(http).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase http)
        {
            string tenantId = RouteHelpers.Tenant(http);
            PromptTemplate existing = await LoadAsync(http).ConfigureAwait(false);
            ConfigurationSupport.EnsureEditable(existing.TenantId, existing.IsBuiltIn, existing.IsProtected, tenantId, "Prompt template " + existing.Name);
            PromptTemplateRequest request = RouteHelpers.RequiredBody<PromptTemplateRequest>(http);
            ConfigurationValidator.PromptTemplate(request.Name, request.Content);
            await EnsureNameAsync(tenantId, request.Name, existing.Id, http).ConfigureAwait(false);

            bool contentChanged = existing.Content != request.Content;
            Apply(existing, request);
            if (contentChanged) existing.CurrentVersion = existing.CurrentVersion + 1;
            PromptTemplate updated = await _Server.Database.PromptTemplates.UpdateAsync(existing, http.Token).ConfigureAwait(false);
            if (contentChanged) await AddVersionAsync(updated, request.ChangeNote ?? "Updated.", http).ConfigureAwait(false);
            await ConfigurationSupport.AuditAsync(_Server, http, AuditEventTypeEnum.PromptTemplateChanged, "PromptTemplate", updated.Id, "Updated prompt template " + updated.Name + (contentChanged ? " to version " + updated.CurrentVersion : String.Empty) + ".").ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, updated).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase http)
        {
            string tenantId = RouteHelpers.Tenant(http);
            PromptTemplate existing = await LoadAsync(http).ConfigureAwait(false);
            ConfigurationSupport.EnsureEditable(existing.TenantId, existing.IsBuiltIn, existing.IsProtected, tenantId, "Prompt template " + existing.Name);
            await _Server.Database.PromptTemplateVersions.DeleteByTemplateAsync(existing.Id, http.Token).ConfigureAwait(false);
            await _Server.Database.PromptTemplates.DeleteAsync(existing.Id, http.Token).ConfigureAwait(false);
            await ConfigurationSupport.AuditAsync(_Server, http, AuditEventTypeEnum.PromptTemplateChanged, "PromptTemplate", existing.Id, "Deleted prompt template " + existing.Name + ".").ConfigureAwait(false);
            await RouteHelpers.NoContentAsync(http).ConfigureAwait(false);
        }

        private async Task VersionsAsync(HttpContextBase http)
        {
            PromptTemplate template = await LoadAsync(http).ConfigureAwait(false);
            List<PromptTemplateVersion> versions = await _Server.Database.PromptTemplateVersions.ListByTemplateAsync(template.Id, http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, versions).ConfigureAwait(false);
        }

        private async Task VersionAsync(HttpContextBase http)
        {
            PromptTemplate template = await LoadAsync(http).ConfigureAwait(false);
            PromptTemplateVersion version = await ReadVersionAsync(template, http).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, version).ConfigureAwait(false);
        }

        private async Task RestoreAsync(HttpContextBase http)
        {
            string tenantId = RouteHelpers.Tenant(http);
            PromptTemplate template = await LoadAsync(http).ConfigureAwait(false);
            ConfigurationSupport.EnsureEditable(template.TenantId, template.IsBuiltIn, template.IsProtected, tenantId, "Prompt template " + template.Name);
            PromptTemplateVersion version = await ReadVersionAsync(template, http).ConfigureAwait(false);
            template.Content = version.Content;
            template.CurrentVersion = template.CurrentVersion + 1;
            PromptTemplate updated = await _Server.Database.PromptTemplates.UpdateAsync(template, http.Token).ConfigureAwait(false);
            await AddVersionAsync(updated, "Restored version " + version.Version + ".", http).ConfigureAwait(false);
            await ConfigurationSupport.AuditAsync(_Server, http, AuditEventTypeEnum.PromptTemplateChanged, "PromptTemplate", updated.Id, "Restored prompt template " + updated.Name + " from version " + version.Version + ".").ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, updated).ConfigureAwait(false);
        }

        private async Task CloneAsync(HttpContextBase http)
        {
            string tenantId = RouteHelpers.Tenant(http);
            PromptTemplate source = await LoadAsync(http).ConfigureAwait(false);
            CloneRequest request = RouteHelpers.RequiredBody<CloneRequest>(http);
            ConfigurationValidator.PromptTemplate(request.Name, source.Content);
            await EnsureNameAsync(tenantId, request.Name, null, http).ConfigureAwait(false);

            PromptTemplate copy = new PromptTemplate();
            copy.TenantId = tenantId;
            copy.Name = request.Name;
            copy.Description = source.Description;
            copy.Kind = source.Kind;
            copy.Content = source.Content;
            copy.BuiltInKey = source.BuiltInKey;
            copy.CurrentVersion = 1;
            foreach (PromptTemplateVariable variable in source.Variables)
            {
                PromptTemplateVariable v = new PromptTemplateVariable();
                v.Name = variable.Name;
                v.Description = variable.Description;
                v.Required = variable.Required;
                v.DefaultValue = variable.DefaultValue;
                copy.Variables.Add(v);
            }
            PromptTemplate created = await _Server.Database.PromptTemplates.CreateAsync(copy, http.Token).ConfigureAwait(false);
            await AddVersionAsync(created, "Cloned from " + source.Name + " version " + source.CurrentVersion + ".", http).ConfigureAwait(false);
            await ConfigurationSupport.AuditAsync(_Server, http, AuditEventTypeEnum.PromptTemplateChanged, "PromptTemplate", created.Id, "Cloned prompt template " + source.Name + " to " + created.Name + ".").ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 201, created).ConfigureAwait(false);
        }

        private async Task ResetAsync(HttpContextBase http)
        {
            string tenantId = RouteHelpers.Tenant(http);
            PromptTemplate template = await LoadAsync(http).ConfigureAwait(false);
            ConfigurationSupport.EnsureEditable(template.TenantId, template.IsBuiltIn, template.IsProtected, tenantId, "Prompt template " + template.Name);
            string? content = BuiltInPrompts.Default(template.BuiltInKey);
            if (content == null) throw new ConflictException("NoBuiltInDefault", "Prompt template " + template.Name + " was not cloned from a built-in, so there is no default to reset to.");
            if (template.Content != content)
            {
                template.Content = content;
                template.CurrentVersion = template.CurrentVersion + 1;
                template = await _Server.Database.PromptTemplates.UpdateAsync(template, http.Token).ConfigureAwait(false);
                await AddVersionAsync(template, "Reset to the built-in default.", http).ConfigureAwait(false);
                await ConfigurationSupport.AuditAsync(_Server, http, AuditEventTypeEnum.PromptTemplateChanged, "PromptTemplate", template.Id, "Reset prompt template " + template.Name + " to the built-in default.").ConfigureAwait(false);
            }
            await RouteHelpers.JsonAsync(http, 200, template).ConfigureAwait(false);
        }

        private async Task RenderAsync(HttpContextBase http)
        {
            PromptTemplate template = await LoadAsync(http).ConfigureAwait(false);
            PromptRenderRequest request = RouteHelpers.Body<PromptRenderRequest>(http) ?? new PromptRenderRequest();
            await RouteHelpers.JsonAsync(http, 200, await RenderPreviewAsync(RouteHelpers.Tenant(http), template.Name, template.Content, request, http).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task RenderAdHocAsync(HttpContextBase http)
        {
            PromptRenderRequest request = RouteHelpers.RequiredBody<PromptRenderRequest>(http);
            await RouteHelpers.JsonAsync(http, 200, await RenderPreviewAsync(RouteHelpers.Tenant(http), "ad hoc", request.Content ?? String.Empty, request, http).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task<PromptRenderResponse> RenderPreviewAsync(string tenantId, string name, string content, PromptRenderRequest request, HttpContextBase http)
        {
            PromptRenderResponse response = new PromptRenderResponse();
            TemplateAnalysis analysis = TemplateAnalyzer.Analyze(content, name, false);
            response.Variables = analysis.Variables;
            response.Includes = analysis.Includes;
            response.Errors.AddRange(analysis.Errors);
            if (!analysis.Valid)
            {
                response.Success = false;
                return response;
            }

            Dictionary<string, string> includes = new Dictionary<string, string>(StringComparer.Ordinal);
            Queue<string> pending = new Queue<string>(analysis.Includes);
            while (pending.Count > 0)
            {
                string include = pending.Dequeue();
                if (includes.ContainsKey(include)) continue;
                PromptTemplate? found = await _Server.Database.PromptTemplates.ReadByNameAsync(tenantId, include, http.Token).ConfigureAwait(false);
                if (found == null) continue;
                includes[include] = found.Content;
                foreach (string nested in TemplateAnalyzer.Analyze(found.Content, include, false).Includes) pending.Enqueue(nested);
            }

            TemplateRenderOptions options = new TemplateRenderOptions();
            options.TemplateName = name;
            options.IncludeResolver = n => includes.TryGetValue(n, out string? c) ? c : null;
            try
            {
                response.Rendered = TemplateRenderer.Render(content, TemplateSampleContext.Build(request.Context), options);
            }
            catch (TemplateException e)
            {
                response.Success = false;
                response.Errors.Add(e.Message);
            }
            return response;
        }

        private async Task<PromptTemplate> LoadAsync(HttpContextBase http)
        {
            PromptTemplate? template = await _Server.Database.PromptTemplates.ReadAsync(RouteHelpers.Tenant(http), RouteHelpers.Param(http, "id")!, http.Token).ConfigureAwait(false);
            return template ?? throw new NotFoundException("Prompt template not found.");
        }

        private async Task<PromptTemplateVersion> ReadVersionAsync(PromptTemplate template, HttpContextBase http)
        {
            if (!Int32.TryParse(RouteHelpers.Param(http, "version"), out int number)) throw new ArgumentException("The version must be an integer.");
            PromptTemplateVersion? version = await _Server.Database.PromptTemplateVersions.ReadByVersionAsync(template.Id, number, http.Token).ConfigureAwait(false);
            return version ?? throw new NotFoundException("Version " + number + " of prompt template " + template.Name + " not found.");
        }

        private async Task EnsureNameAsync(string tenantId, string name, string? currentId, HttpContextBase http)
        {
            PromptTemplate? existing = await _Server.Database.PromptTemplates.ReadByNameAsync(tenantId, name, http.Token).ConfigureAwait(false);
            ConfigurationSupport.EnsureNameAvailable(existing?.TenantId == tenantId ? existing : null, existing?.Id, currentId, name);
        }

        private async Task AddVersionAsync(PromptTemplate template, string note, HttpContextBase http)
        {
            PromptTemplateVersion version = new PromptTemplateVersion();
            version.TenantId = template.TenantId;
            version.TemplateId = template.Id;
            version.Version = template.CurrentVersion;
            version.Content = template.Content;
            version.ChangeNote = note;
            version.CreatedBy = RouteHelpers.Context(http).PrincipalName;
            await _Server.Database.PromptTemplateVersions.CreateAsync(version, http.Token).ConfigureAwait(false);
        }

        private static void Apply(PromptTemplate template, PromptTemplateRequest request)
        {
            template.Name = request.Name;
            template.Description = request.Description;
            template.Kind = request.Kind;
            template.Content = request.Content;
            template.IsBuiltIn = false;
            List<PromptTemplateVariable> variables = new List<PromptTemplateVariable>();
            foreach (PromptTemplateVariable variable in request.Variables)
            {
                PromptTemplateVariable v = new PromptTemplateVariable();
                v.Name = variable.Name;
                v.Description = variable.Description;
                v.Required = variable.Required;
                v.DefaultValue = variable.DefaultValue;
                variables.Add(v);
            }
            if (variables.Count == 0)
            {
                foreach (string path in TemplateAnalyzer.Analyze(request.Content, request.Name, false).Variables)
                {
                    PromptTemplateVariable v = new PromptTemplateVariable();
                    v.Name = path;
                    variables.Add(v);
                }
            }
            template.Variables = variables;
        }

        #endregion
    }
}
