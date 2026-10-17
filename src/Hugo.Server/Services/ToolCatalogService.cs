namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Connectors;
    using Hugo.Core.Database;
    using Hugo.Core.Models;
    using Hugo.Core.Profiles;
    using Hugo.Core.Security;
    using Hugo.Core.Services;

    /// <summary>
    /// The per-caller tool list of a profile (P5.16, D-017, plan section 11.3), shared by MCP tools/list, chat tool
    /// definitions, and GET /v1.0/api/agent-profiles/{id}/tools: family tools with a resource enum of the bindings that
    /// offer them, Write and Destructive base tools hidden on ReadOnly bindings, tools denied by rules hidden, tools that
    /// require a model hidden when the profile has none, plus meta tools from registered providers. Thread-safe.
    /// </summary>
    public class ToolCatalogService
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly ProfileBindingResolver _Bindings;
        private readonly ConcurrentBag<IMetaToolProvider> _MetaProviders = new ConcurrentBag<IMetaToolProvider>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="bindings">Binding resolver.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public ToolCatalogService(DatabaseDriverBase database, ProfileBindingResolver bindings)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register a meta tool provider (the hook the MCP and chat surfaces use for hugo_* tools).
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <exception cref="ArgumentNullException">Thrown when provider is null.</exception>
        public void AddMetaToolProvider(IMetaToolProvider provider)
        {
            _MetaProviders.Add(provider ?? throw new ArgumentNullException(nameof(provider)));
        }

        /// <summary>
        /// The tools a caller sees for a profile.
        /// </summary>
        /// <param name="principal">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="profileId">Profile identifier.</param>
        /// <param name="surface">Surface label for meta tool providers (chat, mcp, a2a, openai, rest).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Catalog.</returns>
        /// <exception cref="NotFoundException">Thrown when the profile is not visible.</exception>
        public async Task<CallerToolCatalog> ListAsync(RequestContext principal, string tenantId, string profileId, string surface, CancellationToken token = default)
        {
            AgentProfile? profile = await _Database.AgentProfiles.ReadAsync(tenantId, profileId, token).ConfigureAwait(false);
            if (profile == null || !ResourceVisibility.CanSee(profile, principal)) throw new NotFoundException("Agent profile not found.");
            List<BoundResource> bound = await _Bindings.ResolveAsync(tenantId, profile.Id, principal, token).ConfigureAwait(false);
            CallerToolCatalog catalog = new CallerToolCatalog();
            catalog.ProfileId = profile.Id;
            catalog.ProfileName = profile.Name;
            catalog.HasModel = !String.IsNullOrEmpty(profile.InferenceEndpointId);
            catalog.ResourceCount = bound.Count;
            catalog.Tools = ToolVisibilityFilter.Build(bound, catalog.HasModel);
            if (principal.IsPublicChat)
            {
                catalog.Tools.RemoveAll(tool => tool.UpdatesKnowledge);
                return catalog;
            }
            foreach (IMetaToolProvider provider in _MetaProviders)
            {
                List<CallerTool> meta = await provider.GetToolsAsync(principal, profile, surface, token).ConfigureAwait(false) ?? new List<CallerTool>();
                foreach (CallerTool tool in meta)
                {
                    tool.IsMetaTool = true;
                    if (!catalog.MetaTools.Any(t => t.Name == tool.Name)) catalog.MetaTools.Add(tool);
                }
            }
            catalog.MetaTools = catalog.MetaTools.OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
            return catalog;
        }

        #endregion
    }
}
