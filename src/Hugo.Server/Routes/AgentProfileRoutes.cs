namespace Hugo.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Hugo.Core;
    using Hugo.Core.Connectors;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Instructions;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;
    using Hugo.Core.Responses;
    using Hugo.Core.Security;
    using Hugo.Server.Services;
    using WatsonWebserver.Core;

    /// <summary>
    /// Agent profile and binding routes (P5.10, P5.12): profile CRUD and duplicate, compiled instructions, the caller's
    /// tool view, the A2A card preview, bindings, and binding tool rules. The tenant comes from the request context.
    /// </summary>
    public class AgentProfileRoutes
    {
        #region Private-Members

        private static readonly string _Base = "/v1.0/api/agent-profiles";
        private readonly ServerContext _Server;
        private readonly AgentProfileService _Profiles;
        private readonly BindingService _Bindings;
        private readonly InstructionCompiler _Compiler;
        private readonly ToolCatalogService _Tools;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="server">Server context.</param>
        /// <param name="profiles">Profiles.</param>
        /// <param name="bindings">Bindings.</param>
        /// <param name="compiler">Instruction compiler.</param>
        /// <param name="tools">Tool catalog.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public AgentProfileRoutes(ServerContext server, AgentProfileService profiles, BindingService bindings, InstructionCompiler compiler, ToolCatalogService tools)
        {
            _Server = server ?? throw new ArgumentNullException(nameof(server));
            _Profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
            _Bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
            _Compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
            _Tools = tools ?? throw new ArgumentNullException(nameof(tools));
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
            routes.Scoped(HttpMethod.GET, _Base, ResourceTypeEnum.AgentProfile, OperationTypeEnum.Read, ListAsync, "List agent profiles", "Agent Profiles", null, typeof(EnumerationResult<AgentProfile>));
            routes.Scoped(HttpMethod.POST, _Base, ResourceTypeEnum.AgentProfile, OperationTypeEnum.Create, CreateAsync, "Create an agent profile", "Agent Profiles", typeof(AgentProfileRequest), typeof(AgentProfile));
            routes.Scoped(HttpMethod.GET, _Base + "/{id}", ResourceTypeEnum.AgentProfile, OperationTypeEnum.Read, ReadAsync, "Read an agent profile", "Agent Profiles", null, typeof(AgentProfile));
            routes.Scoped(HttpMethod.PUT, _Base + "/{id}", ResourceTypeEnum.AgentProfile, OperationTypeEnum.Update, UpdateAsync, "Update an agent profile", "Agent Profiles", typeof(AgentProfileRequest), typeof(AgentProfile));
            routes.Scoped(HttpMethod.DELETE, _Base + "/{id}", ResourceTypeEnum.AgentProfile, OperationTypeEnum.Delete, DeleteAsync, "Delete an agent profile and its bindings (never its resources)", "Agent Profiles");
            routes.Scoped(HttpMethod.POST, _Base + "/{id}/duplicate", ResourceTypeEnum.AgentProfile, OperationTypeEnum.Create, DuplicateAsync, "Copy an agent profile with its bindings", "Agent Profiles", typeof(CloneRequest), typeof(AgentProfile));
            routes.Scoped(HttpMethod.GET, _Base + "/{id}/compiled-instructions", ResourceTypeEnum.AgentProfile, OperationTypeEnum.Read, CompiledAsync, "Compiled instructions as the caller would receive them (surface=Chat|Mcp|A2A|OpenAI), with source map and token estimate", "Agent Profiles", null, typeof(CompiledInstructions));
            routes.Scoped(HttpMethod.GET, _Base + "/{id}/tools", ResourceTypeEnum.AgentProfile, OperationTypeEnum.Read, ToolsAsync, "Tools as the caller would see them, with the resource enum per tool", "Agent Profiles", null, typeof(CallerToolCatalog));
            routes.Scoped(HttpMethod.GET, _Base + "/{id}/card", ResourceTypeEnum.AgentProfile, OperationTypeEnum.Read, CardAsync, "Preview of the A2A agent card", "Agent Profiles", null, typeof(AgentCardPreview));
            routes.Scoped(HttpMethod.GET, _Base + "/{id}/bindings", ResourceTypeEnum.Binding, OperationTypeEnum.Read, BindingsAsync, "List a profile's bindings", "Bindings", null, typeof(List<Binding>));
            routes.Scoped(HttpMethod.POST, _Base + "/{id}/bindings", ResourceTypeEnum.Binding, OperationTypeEnum.Create, CreateBindingAsync, "Bind a resource to a profile", "Bindings", typeof(BindingRequest), typeof(Binding));
            routes.Scoped(HttpMethod.GET, _Base + "/{id}/bindings/{bindingId}", ResourceTypeEnum.Binding, OperationTypeEnum.Read, ReadBindingAsync, "Read a binding", "Bindings", null, typeof(Binding), "bindingId");
            routes.Scoped(HttpMethod.PUT, _Base + "/{id}/bindings/{bindingId}", ResourceTypeEnum.Binding, OperationTypeEnum.Update, UpdateBindingAsync, "Update a binding (access mode, approval policy, instruction override, limits, tool rules, deny patterns)", "Bindings", typeof(BindingRequest), typeof(Binding), "bindingId");
            routes.Scoped(HttpMethod.DELETE, _Base + "/{id}/bindings/{bindingId}", ResourceTypeEnum.Binding, OperationTypeEnum.Delete, DeleteBindingAsync, "Remove a binding", "Bindings", null, null, "bindingId");
            routes.Scoped(HttpMethod.GET, _Base + "/{id}/bindings/{bindingId}/tool-rules", ResourceTypeEnum.Binding, OperationTypeEnum.Read, ToolRulesAsync, "List a binding's tool rules", "Bindings", null, typeof(List<BindingToolRule>), "bindingId");
            routes.Scoped(HttpMethod.PUT, _Base + "/{id}/bindings/{bindingId}/tool-rules", ResourceTypeEnum.Binding, OperationTypeEnum.Update, SetToolRulesAsync, "Replace a binding's tool rules", "Bindings", typeof(List<BindingToolRule>), typeof(Binding), "bindingId");
        }

        #endregion

        #region Private-Methods

        private async Task ListAsync(HttpContextBase http)
        {
            await RouteHelpers.JsonAsync(http, 200, await _Profiles.EnumerateAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), RouteHelpers.Enumeration(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase http)
        {
            AgentProfile created = await _Profiles.CreateAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), RouteHelpers.RequiredBody<AgentProfileRequest>(http), http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 201, created).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase http)
        {
            await RouteHelpers.JsonAsync(http, 200, await _Profiles.ReadAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), Id(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase http)
        {
            AgentProfile updated = await _Profiles.UpdateAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), Id(http), RouteHelpers.RequiredBody<AgentProfileRequest>(http), http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, updated).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase http)
        {
            await _Profiles.DeleteAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), Id(http), http.Token).ConfigureAwait(false);
            await RouteHelpers.NoContentAsync(http).ConfigureAwait(false);
        }

        private async Task DuplicateAsync(HttpContextBase http)
        {
            CloneRequest request = RouteHelpers.RequiredBody<CloneRequest>(http);
            AgentProfile copy = await _Profiles.DuplicateAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), Id(http), request.Name, http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 201, copy).ConfigureAwait(false);
        }

        private async Task CompiledAsync(HttpContextBase http)
        {
            RequestContext ctx = RouteHelpers.Context(http);
            string tenantId = RouteHelpers.Tenant(http);
            AgentProfile profile = await _Profiles.ReadAsync(ctx, tenantId, Id(http), http.Token).ConfigureAwait(false);
            InvocationSurfaceEnum surface = RouteHelpers.QueryEnum<InvocationSurfaceEnum>(http, "surface") ?? InvocationSurfaceEnum.Chat;
            CompiledInstructions compiled = await _Compiler.CompileAsync(tenantId, profile.Id, ctx, surface, http.Token).ConfigureAwait(false);
            if (RouteHelpers.QueryBool(http, "text") == true)
            {
                http.Response.StatusCode = 200;
                http.Response.ContentType = "text/plain; charset=utf-8";
                await http.Response.Send(compiled.Text).ConfigureAwait(false);
                return;
            }
            await RouteHelpers.JsonAsync(http, 200, compiled).ConfigureAwait(false);
        }

        private async Task ToolsAsync(HttpContextBase http)
        {
            RequestContext ctx = RouteHelpers.Context(http);
            CallerToolCatalog catalog = await _Tools.ListAsync(ctx, RouteHelpers.Tenant(http), Id(http), RouteHelpers.Query(http, "surface") ?? "rest", http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, catalog).ConfigureAwait(false);
        }

        private async Task CardAsync(HttpContextBase http)
        {
            RequestContext ctx = RouteHelpers.Context(http);
            string tenantId = RouteHelpers.Tenant(http);
            AgentProfile profile = await _Profiles.ReadAsync(ctx, tenantId, Id(http), http.Token).ConfigureAwait(false);
            CallerToolCatalog catalog = await _Tools.ListAsync(ctx, tenantId, profile.Id, "a2a", http.Token).ConfigureAwait(false);
            AgentCardPreview card = new AgentCardPreview();
            card.Name = String.IsNullOrEmpty(profile.DisplayName) ? profile.Name : profile.DisplayName;
            card.Version = Constants.ProductVersion;
            string baseUrl = String.IsNullOrEmpty(_Server.Settings.Rest.ExternalBaseUrl) ? "http://127.0.0.1:" + _Server.Settings.Rest.Port : _Server.Settings.Rest.ExternalBaseUrl.TrimEnd('/');
            card.Url = baseUrl + "/v1.0/a2a/" + tenantId + "/" + profile.Name;
            List<string> families = catalog.Tools.Select(t => t.Family).Distinct(StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal).ToList();
            card.Description = profile.PersonaName + " operates " + catalog.ResourceCount + " resource" + (catalog.ResourceCount == 1 ? String.Empty : "s") + (families.Count > 0 ? " (" + String.Join(", ", families) + ")" : String.Empty) + " for " + profile.UserAddress + ". " + (profile.Description ?? String.Empty).Trim();
            AgentCardSkill general = new AgentCardSkill();
            general.Id = "agent-smith";
            general.Name = profile.PersonaName;
            general.Description = "Investigate and operate the attached systems in plain language; changes may require human approval.";
            general.Tags = new List<string> { "operations" };
            card.Skills.Add(general);
            foreach (string family in families)
            {
                AgentCardSkill skill = new AgentCardSkill();
                skill.Id = family;
                skill.Name = family;
                List<CallerTool> tools = catalog.Tools.Where(t => t.Family == family).ToList();
                skill.Description = "Uses " + String.Join(", ", tools.Select(t => t.Name)) + " on " + String.Join(", ", tools.SelectMany(t => t.Resources).Select(r => r.Alias).Distinct(StringComparer.Ordinal)) + ".";
                skill.Tags = new List<string> { family };
                card.Skills.Add(skill);
            }
            await RouteHelpers.JsonAsync(http, 200, card).ConfigureAwait(false);
        }

        private async Task BindingsAsync(HttpContextBase http)
        {
            await RouteHelpers.JsonAsync(http, 200, await _Bindings.ListAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), Id(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task CreateBindingAsync(HttpContextBase http)
        {
            Binding created = await _Bindings.CreateAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), Id(http), RouteHelpers.RequiredBody<BindingRequest>(http), http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 201, created).ConfigureAwait(false);
        }

        private async Task ReadBindingAsync(HttpContextBase http)
        {
            await RouteHelpers.JsonAsync(http, 200, await _Bindings.ReadAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), Id(http), BindingId(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UpdateBindingAsync(HttpContextBase http)
        {
            Binding updated = await _Bindings.UpdateAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), Id(http), BindingId(http), RouteHelpers.RequiredBody<BindingRequest>(http), http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, updated).ConfigureAwait(false);
        }

        private async Task DeleteBindingAsync(HttpContextBase http)
        {
            await _Bindings.DeleteAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), Id(http), BindingId(http), http.Token).ConfigureAwait(false);
            await RouteHelpers.NoContentAsync(http).ConfigureAwait(false);
        }

        private async Task ToolRulesAsync(HttpContextBase http)
        {
            Binding binding = await _Bindings.ReadAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), Id(http), BindingId(http), http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, binding.ToolRules).ConfigureAwait(false);
        }

        private async Task SetToolRulesAsync(HttpContextBase http)
        {
            List<BindingToolRule> rules = RouteHelpers.Body<List<BindingToolRule>>(http) ?? new List<BindingToolRule>();
            Binding updated = await _Bindings.SetToolRulesAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), Id(http), BindingId(http), rules, http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, updated).ConfigureAwait(false);
        }

        private static string Id(HttpContextBase http)
        {
            return RouteHelpers.Param(http, "id")!;
        }

        private static string BindingId(HttpContextBase http)
        {
            return RouteHelpers.Param(http, "bindingId")!;
        }

        #endregion
    }
}
