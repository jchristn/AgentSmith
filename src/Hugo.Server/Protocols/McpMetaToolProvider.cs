namespace Hugo.Server.Protocols
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Connectors;
    using Hugo.Core.Models;
    using Hugo.Core.Profiles;
    using Hugo.Core.Security;
    using Hugo.Server.Services;

    /// <summary>
    /// Supplies the MCP meta tools (P7.6) to <see cref="ToolCatalogService"/> for surface mcp: the hugo_* tools,
    /// ask_agent_smith when a delegation service is registered and the profile has an inference endpoint, and the
    /// management tools when the profile has expose_management_tools and the principal is Admin-capable (P7.10). The
    /// resource argument of hugo_describe_resource lists the profile's bound aliases. Thread-safe.
    /// </summary>
    public class McpMetaToolProvider : IMetaToolProvider
    {
        #region Public-Members

        /// <summary>
        /// Surface label this provider serves.
        /// </summary>
        public static readonly string Surface = "mcp";

        #endregion

        #region Private-Members

        private readonly ProfileBindingResolver _Bindings;
        private readonly McpToolExecutor _Executor;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="bindings">Binding resolver.</param>
        /// <param name="executor">Tool executor (availability rules).</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public McpMetaToolProvider(ProfileBindingResolver bindings, McpToolExecutor executor)
        {
            _Bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
            _Executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<List<CallerTool>> GetToolsAsync(RequestContext principal, AgentProfile profile, string surface, CancellationToken token)
        {
            List<CallerTool> tools = new List<CallerTool>();
            if (!String.Equals(surface, Surface, StringComparison.OrdinalIgnoreCase) || principal == null || profile == null) return tools;
            bool management = await _Executor.ManagementAllowedAsync(principal, profile, token).ConfigureAwait(false);
            bool delegation = _Executor.DelegationAvailable(profile);
            List<string>? aliases = null;
            foreach (McpToolDefinition definition in McpToolCatalog.All())
            {
                if ((AgentInstructionTools.IsInstructionTool(definition.Name) || AgentDocumentTools.IsDocumentTool(definition.Name))) continue;
                if (definition.Kind == McpToolKindEnum.Management && !management) continue;
                if (definition.Kind == McpToolKindEnum.Delegation && !delegation) continue;
                string schema = definition.InputSchemaJson;
                if (definition.Name == McpToolCatalog.DescribeResource)
                {
                    if (aliases == null)
                    {
                        List<BoundResource> bound = await _Bindings.ResolveAsync(profile.TenantId, profile.Id, principal, token).ConfigureAwait(false);
                        aliases = bound.Select(b => b.Binding.Alias).OrderBy(a => a, StringComparer.Ordinal).ToList();
                    }
                    if (aliases.Count > 0) schema = ToolVisibilityFilter.WithResourceArgument("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}", aliases);
                }
                CallerTool tool = new CallerTool();
                tool.Name = definition.Name;
                tool.Family = "hugo";
                tool.Title = definition.Title;
                tool.Description = definition.Description;
                tool.InputSchemaJson = schema;
                tool.BaseEffect = definition.ReadOnly ? EffectEnum.Read : EffectEnum.Write;
                tool.KnownEffect = definition.ReadOnly ? EffectEnum.Read : null;
                tool.IsMetaTool = true;
                tools.Add(tool);
            }
            return tools;
        }

        #endregion
    }
}
