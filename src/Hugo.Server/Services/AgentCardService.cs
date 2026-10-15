namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core;
    using Hugo.Core.Models;
    using Hugo.Core.Profiles;
    using Hugo.Core.Prompts;
    using Hugo.Core.Templates;
    using Voltaic.A2A;

    /// <summary>
    /// Builds A2A 1.0 agent cards for agent profiles (P7.16, adapted from Garrison AgentCardService). The general skill
    /// <c>agent-smith</c> is described by the <c>a2a.skill-description</c> prompt template (tenant override, then the
    /// global default); each bound connector family adds one skill. Only resources visible to the whole tenant are
    /// described, because the card is public. Thread-safe.
    /// </summary>
    public class AgentCardService
    {
        #region Public-Members

        /// <summary>
        /// Name of the bearer security scheme on the card.
        /// </summary>
        public string SecuritySchemeName { get; set; } = "hugo";

        #endregion

        #region Private-Members

        private readonly ProfileBindingResolver _Bindings;
        private readonly PromptTemplateService _Templates;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="bindings">Binding resolver.</param>
        /// <param name="templates">Prompt templates.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public AgentCardService(ProfileBindingResolver bindings, PromptTemplateService templates)
        {
            _Bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
            _Templates = templates ?? throw new ArgumentNullException(nameof(templates));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The JSON-RPC endpoint of a profile.
        /// </summary>
        /// <param name="baseUrl">External base URL.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="profile">Profile.</param>
        /// <returns>URL.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static string EndpointUrl(string baseUrl, string tenantId, AgentProfile profile)
        {
            if (baseUrl == null) throw new ArgumentNullException(nameof(baseUrl));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            return baseUrl.TrimEnd('/') + "/v1.0/a2a/" + Uri.EscapeDataString(tenantId) + "/" + Uri.EscapeDataString(profile.Name);
        }

        /// <summary>
        /// Build a profile's card.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="profile">Profile.</param>
        /// <param name="baseUrl">External base URL (<c>Rest.ExternalBaseUrl</c> or the request's own).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Card.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public async Task<AgentCard> BuildAsync(string tenantId, AgentProfile profile, string baseUrl, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (baseUrl == null) throw new ArgumentNullException(nameof(baseUrl));
            List<BoundResource> bound = await _Bindings.ResolveAsync(tenantId, profile.Id, null, token).ConfigureAwait(false);
            string displayName = String.IsNullOrEmpty(profile.DisplayName) ? profile.Name : profile.DisplayName!;
            string skillDescription = await RenderAsync(tenantId, profile, bound, displayName, token).ConfigureAwait(false);

            AgentCard card = new AgentCard();
            card.Name = displayName;
            card.Description = String.IsNullOrWhiteSpace(profile.Description) ? skillDescription : profile.Description!.Trim();
            card.Version = Constants.ProductVersion;
            AgentInterface endpoint = new AgentInterface();
            endpoint.Url = EndpointUrl(baseUrl, tenantId, profile);
            endpoint.ProtocolBinding = "JSONRPC";
            card.SupportedInterfaces.Add(endpoint);
            card.Capabilities.Streaming = true;
            card.Capabilities.PushNotifications = false;
            card.Capabilities.StateTransitionHistory = false;
            AgentProvider provider = new AgentProvider();
            provider.Organization = "Hugo";
            provider.Url = "https://github.com/jchristn/AgentSmith";
            card.Provider = provider;
            card.DefaultInputModes = new List<string> { "text/plain", "application/json" };
            card.DefaultOutputModes = new List<string> { "text/plain" };

            AgentSkill general = new AgentSkill();
            general.Id = "agent-smith";
            general.Name = profile.PersonaName;
            general.Description = skillDescription;
            general.Tags = new List<string> { "operations", "investigation" };
            card.Skills.Add(general);
            foreach (IGrouping<string, BoundResource> family in bound.GroupBy(b => b.Family, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                AgentSkill skill = new AgentSkill();
                skill.Id = family.Key;
                string familyName = family.Select(b => b.Connector.Descriptor.DisplayName).FirstOrDefault(n => !String.IsNullOrEmpty(n)) ?? family.Key;
                skill.Name = familyName;
                skill.Description = "Investigate and operate " + familyName + " resources: " + String.Join(", ", family.Select(b => b.Binding.Alias + " (" + b.EffectiveAccessMode + ")")) + ".";
                skill.Tags = new List<string> { family.Key };
                card.Skills.Add(skill);
            }

            HttpAuthSecurityScheme http = new HttpAuthSecurityScheme();
            http.Scheme = "bearer";
            http.Description = "Hugo session token from POST /v1.0/token. Credentials may also send x-access-key and x-secret-key headers.";
            SecurityScheme scheme = new SecurityScheme();
            scheme.HttpAuthSecurityScheme = http;
            card.SecuritySchemes = new Dictionary<string, SecurityScheme> { { SecuritySchemeName, scheme } };
            SecurityRequirement requirement = new SecurityRequirement();
            requirement.Schemes = new Dictionary<string, List<string>> { { SecuritySchemeName, new List<string>() } };
            card.SecurityRequirements = new List<SecurityRequirement> { requirement };
            return card;
        }

        #endregion

        #region Private-Methods

        private async Task<string> RenderAsync(string tenantId, AgentProfile profile, List<BoundResource> bound, string displayName, CancellationToken token)
        {
            string fallback = displayName + " (" + profile.PersonaName + ") investigates and operates the systems attached to it.";
            ResolvedPromptTemplate? template = await _Templates.ResolveAsync(tenantId, "a2a.skill-description", token).ConfigureAwait(false);
            if (template == null || String.IsNullOrWhiteSpace(template.Content)) return fallback;
            TemplateContext context = new TemplateContext();
            context.Set("profile.name", profile.Name);
            context.Set("profile.displayName", displayName);
            context.Set("profile.personaName", profile.PersonaName);
            context.Set("profile.userAddress", profile.UserAddress);
            context.Set("profile.description", profile.Description ?? String.Empty);
            context.Set("profile.hasBindings", bound.Count > 0);
            context.Set("profile.bindingCount", bound.Count);
            context.Set("profile.singleBinding", bound.Count == 1);
            context.Set("profile.familySummary", String.Join(", ", bound.Select(b => (String.IsNullOrEmpty(b.Connector.Descriptor.DisplayName) ? b.Resource.ConnectorType : b.Connector.Descriptor.DisplayName) + " (" + b.Binding.Alias + ")")));
            context.Set("tenant.id", tenantId);
            context.Set("hugo.version", Constants.ProductVersion);
            try
            {
                string rendered = TemplateRenderer.Render(template.Content, context, new TemplateRenderOptions { TemplateName = template.Key }).Trim();
                return rendered.Length > 0 ? rendered : fallback;
            }
            catch (TemplateException)
            {
                return fallback;
            }
        }

        #endregion
    }
}
