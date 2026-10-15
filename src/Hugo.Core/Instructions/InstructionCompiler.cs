namespace Hugo.Core.Instructions
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Agent;
    using Hugo.Core.Connectors;
    using Hugo.Core.Database;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Profiles;
    using Hugo.Core.Prompts;
    using Hugo.Core.Security;
    using Hugo.Core.Services;
    using Hugo.Core.Telemetry;
    using Hugo.Core.Templates;

    /// <summary>
    /// Compiles a profile's instructions for one caller and surface (P5.11, plan section 10.4). The composition root
    /// (instructions.root) includes the persona ({{&gt; persona}} binds to the profile's persona template key), the
    /// safety and tool usage rules, the surface preamble, one section.resource per usable binding (ordered by family,
    /// then alias) carrying the resolved connector instructions with the binding's Append or Replace applied and any third-party guidance fenced as untrusted content (P13.28), the
    /// operator notes, and the resource's root knowledge (P5.21), and finally the profile append. Every template comes
    /// from the most specific layer. The result carries a source map and a token estimate and is cached per profile,
    /// principal, and surface until <see cref="Invalidate"/> is called for the tenant. Thread-safe.
    /// </summary>
    public class InstructionCompiler
    {
        #region Public-Members

        /// <summary>
        /// Maximum characters of root knowledge included per resource (Instructions.MaxKnowledgeCharsPerResource).
        /// Default 2000, minimum 0 (off), maximum 100000.
        /// </summary>
        public int MaxKnowledgeCharsPerResource
        {
            get
            {
                return _MaxKnowledgeChars;
            }
            set
            {
                _MaxKnowledgeChars = Math.Clamp(value, 0, 100000);
            }
        }

        /// <summary>
        /// Profiles with <see cref="InstructionDetailEnum.Auto"/> use compact connector instructions when they have more
        /// bindings than this (Instructions.CompactBindingThreshold). Default 3, minimum 0 (always compact), maximum 1000.
        /// </summary>
        public int CompactBindingThreshold
        {
            get
            {
                return _CompactBindingThreshold;
            }
            set
            {
                _CompactBindingThreshold = Math.Clamp(value, 0, 1000);
            }
        }

        /// <summary>
        /// Maximum cached compilations. Default 1024, minimum 0 (no cache), maximum 100000.
        /// </summary>
        public int MaxCacheEntries
        {
            get
            {
                return _MaxCacheEntries;
            }
            set
            {
                _MaxCacheEntries = Math.Clamp(value, 0, 100000);
            }
        }

        /// <summary>
        /// Optional warning sink (for example the server log).
        /// </summary>
        public Action<string>? Warn { get; set; } = null;

        /// <summary>
        /// Optional source of third-party guidance (an external MCP server's instructions, a remote agent's card). When
        /// set, each binding whose source returns text gets that text appended to its section, scrubbed, clipped to
        /// <see cref="MaxThirdPartyInstructionChars"/>, and wrapped in an untrusted-content fence so the model treats it
        /// as data. A binding whose instruction mode is Replace with its own text omits it. Default null (none).
        /// </summary>
        public IThirdPartyInstructionSource? ThirdPartyInstructions { get; set; } = null;

        /// <summary>
        /// Maximum characters of third-party guidance kept per binding before fencing; longer text is cut and marked as
        /// truncated. Default 4000, minimum 0 (third-party guidance is never included), maximum 100000.
        /// </summary>
        public int MaxThirdPartyInstructionChars
        {
            get
            {
                return _MaxThirdPartyChars;
            }
            set
            {
                _MaxThirdPartyChars = Math.Clamp(value, 0, 100000);
            }
        }

        #endregion

        #region Private-Members

        private static readonly char _Open = '';
        private static readonly char _Mid = '';
        private static readonly char _Close = '';
        private readonly DatabaseDriverBase _Database;
        private readonly ProfileBindingResolver _Bindings;
        private readonly PromptTemplateService _Templates;
        private readonly ConcurrentDictionary<string, CompiledInstructions> _Cache = new ConcurrentDictionary<string, CompiledInstructions>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, long> _Generations = new ConcurrentDictionary<string, long>(StringComparer.Ordinal);
        private long _GlobalGeneration = 0;
        private int _MaxKnowledgeChars = 2000;
        private int _CompactBindingThreshold = 3;
        private int _MaxCacheEntries = 1024;
        private int _MaxThirdPartyChars = 4000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="bindings">Binding resolver.</param>
        /// <param name="templates">Layered templates.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public InstructionCompiler(DatabaseDriverBase database, ProfileBindingResolver bindings, PromptTemplateService templates)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
            _Templates = templates ?? throw new ArgumentNullException(nameof(templates));
            _Templates.Changed += (sender, tenantId) => Invalidate(tenantId);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Drop cached compilations of a tenant, or of every tenant when tenantId is null. Call after any change to a
        /// profile, binding, resource, knowledge entry, tenant name, or template.
        /// </summary>
        /// <param name="tenantId">Tenant identifier, or null for all.</param>
        public void Invalidate(string? tenantId)
        {
            if (String.IsNullOrEmpty(tenantId)) Interlocked.Increment(ref _GlobalGeneration);
            else _Generations.AddOrUpdate(tenantId, 1, (k, v) => v + 1);
            if (_Cache.Count > _MaxCacheEntries) _Cache.Clear();
        }

        /// <summary>
        /// Compile a profile's instructions.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="profileId">Profile identifier.</param>
        /// <param name="principal">Caller (drives resource visibility and the user variables), or null for an internal caller.</param>
        /// <param name="surface">Surface (Rest compiles as Chat).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Compiled instructions.</returns>
        /// <exception cref="ArgumentNullException">Thrown when tenantId or profileId is null or empty.</exception>
        /// <exception cref="NotFoundException">Thrown when the profile does not exist in the tenant.</exception>
        public async Task<CompiledInstructions> CompileAsync(string tenantId, string profileId, RequestContext? principal, InvocationSurfaceEnum surface, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(profileId)) throw new ArgumentNullException(nameof(profileId));
            if (surface == InvocationSurfaceEnum.Rest) surface = InvocationSurfaceEnum.Chat;
            long started = Stopwatch.GetTimestamp();
            string cacheKey = CacheKey(tenantId, profileId, principal, surface);
            if (_MaxCacheEntries > 0 && _Cache.TryGetValue(cacheKey, out CompiledInstructions? cached))
            {
                HugoMetrics.Observe(HugoMetricCatalog.InstructionsCompileDuration, HugoMetrics.SecondsSince(started), "cached", "true");
                CompiledInstructions copy = Copy(cached);
                copy.FromCache = true;
                return copy;
            }

            using (Activity? activity = HugoTracing.Start("instructions compile"))
            {
                activity?.SetTag("hugo.profile", profileId);
                activity?.SetTag("hugo.surface", surface.ToString());
                CompiledInstructions compiled = await CompileUncachedAsync(tenantId, profileId, principal, surface, token).ConfigureAwait(false);
                if (_MaxCacheEntries > 0)
                {
                    if (_Cache.Count >= _MaxCacheEntries) _Cache.Clear();
                    _Cache[cacheKey] = Copy(compiled);
                }
                HugoTracing.Ok(activity);
                HugoMetrics.Observe(HugoMetricCatalog.InstructionsCompileDuration, HugoMetrics.SecondsSince(started), "cached", "false");
                return compiled;
            }
        }

        /// <summary>
        /// The full guidance for one bound resource of a profile, as a caller of a surface receives it: the connector's
        /// full instructions (never the compact form, whatever the profile's instruction detail), rendered through the
        /// prompt template layers exactly as <see cref="CompileAsync"/> renders them, with the binding's Append or
        /// Replace applied, plus the operator notes, root knowledge, effective access mode, and usable tools. This is
        /// what hugo_describe_resource and hugo://resources/{alias} return, so a compact profile can fetch the detail.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="profileId">Profile identifier.</param>
        /// <param name="alias">Binding alias.</param>
        /// <param name="principal">Caller, or null for an internal caller.</param>
        /// <param name="surface">Surface.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Resource guidance, or null when no usable binding has the alias.</returns>
        /// <exception cref="ArgumentNullException">Thrown when tenantId, profileId, or alias is null or empty.</exception>
        /// <exception cref="NotFoundException">Thrown when the profile does not exist in the tenant.</exception>
        public async Task<ResourceInstructions?> DescribeResourceAsync(string tenantId, string profileId, string alias, RequestContext? principal, InvocationSurfaceEnum surface, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(profileId)) throw new ArgumentNullException(nameof(profileId));
            if (String.IsNullOrEmpty(alias)) throw new ArgumentNullException(nameof(alias));
            if (surface == InvocationSurfaceEnum.Rest) surface = InvocationSurfaceEnum.Chat;
            AgentProfile profile = await _Database.AgentProfiles.ReadAsync(tenantId, profileId, token).ConfigureAwait(false) ?? throw new NotFoundException("Agent profile not found.");
            Tenant? tenant = await _Database.Tenants.ReadAsync(tenantId, token).ConfigureAwait(false);
            List<BoundResource> bound = await _Bindings.ResolveAsync(tenantId, profileId, principal, token).ConfigureAwait(false);
            int index = bound.FindIndex(b => String.Equals(b.Binding.Alias, alias, StringComparison.Ordinal));
            if (index < 0) return null;
            Dictionary<string, ResolvedPromptTemplate> templates = await _Templates.ResolveAllAsync(tenantId, token).ConfigureAwait(false);
            List<SegmentSource> sources = new List<SegmentSource>();
            List<string> warnings = new List<string>();
            TemplateContext context = await BuildContextAsync(profile, tenant, principal, surface, bound, templates, sources, warnings, token, true, false).ConfigureAwait(false);
            if (context.Get("bindings") is not List<object?> maps || index >= maps.Count || maps[index] is not Dictionary<string, object?> map) return null;

            BoundResource item = bound[index];
            ResourceInstructions result = new ResourceInstructions();
            result.Alias = item.Binding.Alias;
            result.ResourceId = item.Resource.Id;
            result.ConnectorType = item.Resource.ConnectorType;
            result.ConnectorDisplayName = DisplayName(item);
            result.Family = item.Family;
            result.AccessMode = item.EffectiveAccessMode;
            result.RequireApprovalForDestructive = item.Binding.RequireApprovalForDestructive;
            result.Environment = String.IsNullOrEmpty(item.Resource.Environment) ? null : item.Resource.Environment;
            result.Endpoint = String.IsNullOrEmpty(item.Resource.Endpoint) ? null : item.Resource.Endpoint;
            result.Description = String.IsNullOrWhiteSpace(item.Resource.Description) ? null : item.Resource.Description.Trim();
            result.Notes = String.IsNullOrWhiteSpace(item.Resource.Notes) ? null : item.Resource.Notes.Trim();
            string knowledge = map.TryGetValue("knowledge", out object? k) && k is string ks ? ks : String.Empty;
            result.Knowledge = knowledge.Length == 0 ? null : knowledge;
            string marked = map.TryGetValue("instructions", out object? i) && i is string text ? text : String.Empty;
            result.Instructions = Extract(marked, sources, new List<InstructionSegment>()).Trim();
            bool hasModel = !String.IsNullOrEmpty(profile.InferenceEndpointId);
            result.Tools = item.Connector.Tools.Where(t => ToolVisibilityFilter.Offers(item, t.Descriptor, hasModel)).Select(t => t.Descriptor.Name).ToList();
            result.MaxOutputBytes = item.Binding.MaxOutputBytes;
            result.MaxRows = item.Binding.MaxRows;
            result.Warnings = warnings;
            return result;
        }

        #endregion

        #region Private-Methods

        private async Task<CompiledInstructions> CompileUncachedAsync(string tenantId, string profileId, RequestContext? principal, InvocationSurfaceEnum surface, CancellationToken token)
        {
            AgentProfile profile = await _Database.AgentProfiles.ReadAsync(tenantId, profileId, token).ConfigureAwait(false) ?? throw new NotFoundException("Agent profile not found.");
            Tenant? tenant = await _Database.Tenants.ReadAsync(tenantId, token).ConfigureAwait(false);
            List<BoundResource> bound = await _Bindings.ResolveAsync(tenantId, profileId, principal, token).ConfigureAwait(false);
            Dictionary<string, ResolvedPromptTemplate> templates = await _Templates.ResolveAllAsync(tenantId, token).ConfigureAwait(false);

            CompiledInstructions result = new CompiledInstructions();
            result.ProfileId = profile.Id;
            result.ProfileName = profile.Name;
            result.Surface = surface;
            result.BindingCount = bound.Count;

            List<SegmentSource> sources = new List<SegmentSource>();
            TemplateContext context = await BuildContextAsync(profile, tenant, principal, surface, bound, templates, sources, result.Warnings, token).ConfigureAwait(false);

            string rendered;
            try
            {
                rendered = Render(context, templates, profile, sources);
            }
            catch (TemplateException e)
            {
                // A customized template that fails to render falls back to the Built-in layer for every key.
                string warning = "Instructions for profile " + profile.Name + " failed to render (" + e.Message + "); the built-in templates were used instead.";
                result.Warnings.Add(warning);
                Warn?.Invoke(warning);
                Dictionary<string, ResolvedPromptTemplate> builtIns = new Dictionary<string, ResolvedPromptTemplate>(StringComparer.Ordinal);
                foreach (BuiltInTemplate builtIn in _Templates.BuiltIns.All())
                {
                    builtIns[builtIn.Key] = new ResolvedPromptTemplate { Key = builtIn.Key, Content = builtIn.Content, Layer = PromptLayerEnum.BuiltIn };
                }
                sources.Clear();
                List<string> ignored = new List<string>();
                context = await BuildContextAsync(profile, tenant, principal, surface, bound, builtIns, sources, ignored, token).ConfigureAwait(false);
                rendered = Render(context, builtIns, profile, sources);
            }

            List<InstructionSegment> segments = new List<InstructionSegment>();
            result.Text = Extract(rendered, sources, segments);
            result.Segments = segments;
            result.TokenEstimate = TokenEstimator.Estimate(result.Text);
            result.CompiledUtc = DateTime.UtcNow;
            return result;
        }

        private string Render(TemplateContext context, Dictionary<string, ResolvedPromptTemplate> templates, AgentProfile profile, List<SegmentSource> sources)
        {
            ResolvedPromptTemplate root = templates.TryGetValue(BuiltInTemplateCatalog.RootKey, out ResolvedPromptTemplate? r)
                ? r
                : new ResolvedPromptTemplate { Key = BuiltInTemplateCatalog.RootKey, Content = "{{> persona}}" };
            TemplateRenderOptions options = new TemplateRenderOptions();
            options.TemplateName = root.Key;
            options.IncludeResolver = name =>
            {
                string key = name == "persona" ? (String.IsNullOrEmpty(profile.PersonaTemplateKey) ? BuiltInTemplateCatalog.DefaultPersonaKey : profile.PersonaTemplateKey) : name;
                if (!templates.TryGetValue(key, out ResolvedPromptTemplate? include))
                {
                    if (name == "persona" && templates.TryGetValue(BuiltInTemplateCatalog.DefaultPersonaKey, out ResolvedPromptTemplate? fallback)) include = fallback;
                    else return null;
                }
                return Mark(sources, include.Key, include.Layer, include.Version, null, Clean(include.Content).TrimEnd('\n'));
            };
            string body = TemplateRenderer.Render(Clean(root.Content), context, options);
            return Mark(sources, root.Key, root.Layer, root.Version, null, body);
        }

        private async Task<TemplateContext> BuildContextAsync(AgentProfile profile, Tenant? tenant, RequestContext? principal, InvocationSurfaceEnum surface, List<BoundResource> bound, Dictionary<string, ResolvedPromptTemplate> templates, List<SegmentSource> sources, List<string> warnings, CancellationToken token, bool forceFull = false, bool includeThirdParty = true)
        {
            TemplateContext context = new TemplateContext();
            bool hasModel = !String.IsNullOrEmpty(profile.InferenceEndpointId);
            context.Set("profile.name", profile.Name);
            context.Set("profile.displayName", String.IsNullOrEmpty(profile.DisplayName) ? profile.Name : profile.DisplayName);
            context.Set("profile.personaName", profile.PersonaName);
            context.Set("profile.userAddress", profile.UserAddress);
            context.Set("profile.description", profile.Description ?? String.Empty);
            context.Set("profile.instructionsAppend", Clean(profile.InstructionsAppend ?? String.Empty).Trim());
            context.Set("profile.hasBindings", bound.Count > 0);
            context.Set("profile.bindingCount", bound.Count);
            context.Set("profile.singleBinding", bound.Count == 1);
            context.Set("profile.familySummary", String.Join(", ", bound.Select(b => DisplayName(b) + " (" + b.Binding.Alias + ")")));
            context.Set("tenant.id", tenant?.Id ?? profile.TenantId);
            context.Set("tenant.name", tenant?.Name ?? profile.TenantId);
            User? user = principal?.User;
            context.Set("user.firstName", user?.FirstName ?? String.Empty);
            context.Set("user.lastName", user?.LastName ?? String.Empty);
            context.Set("user.email", user?.Email ?? String.Empty);
            context.Set("now.utc", DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            context.Set("limits.maxIterations", profile.MaxIterations);
            context.Set("limits.maxToolCallsPerTurn", profile.MaxToolCallsPerTurn);
            context.Set("limits.toolTimeoutSeconds", profile.ToolTimeoutSeconds);
            Dictionary<string, object?> surfaceMap = TemplateContext.NewMap();
            surfaceMap["name"] = surface.ToString();
            surfaceMap["isChat"] = surface == InvocationSurfaceEnum.Chat;
            surfaceMap["isMcp"] = surface == InvocationSurfaceEnum.Mcp;
            surfaceMap["isA2A"] = surface == InvocationSurfaceEnum.A2A;
            surfaceMap["isOpenAI"] = surface == InvocationSurfaceEnum.OpenAI;
            context.Set("surface", surfaceMap);
            context.Set("hugo.version", Constants.ProductVersion);

            List<object?> bindings = new List<object?>();
            Dictionary<string, object?> byFamily = TemplateContext.NewMap();
            bool compact = !forceFull && (profile.InstructionDetail == InstructionDetailEnum.Compact
                || (profile.InstructionDetail == InstructionDetailEnum.Auto && bound.Count > _CompactBindingThreshold));
            context.Set("compactInstructions", compact);
            foreach (BoundResource item in bound)
            {
                Dictionary<string, object?> map = TemplateContext.NewMap();
                AccessModeEnum mode = item.EffectiveAccessMode;
                map["alias"] = item.Binding.Alias;
                map["connectorType"] = item.Resource.ConnectorType;
                map["connectorDisplayName"] = DisplayName(item);
                map["family"] = item.Family;
                map["accessMode"] = mode.ToString();
                map["requireApprovalForDestructive"] = item.Binding.RequireApprovalForDestructive;
                map["readOnly"] = mode == AccessModeEnum.ReadOnly;
                map["approvalRequired"] = mode == AccessModeEnum.ReadWriteWithApproval;
                map["destructiveApproval"] = mode == AccessModeEnum.ReadWrite && item.Binding.RequireApprovalForDestructive;
                map["environment"] = Clean(item.Resource.Environment ?? String.Empty);
                map["endpoint"] = Clean(item.Resource.Endpoint ?? String.Empty);
                map["description"] = Clean(item.Resource.Description ?? String.Empty).Trim();
                map["notes"] = Clean(item.Resource.Notes ?? String.Empty).Trim();
                List<object?> tools = new List<object?>();
                List<string> usable = new List<string>();
                foreach (IConnectorTool tool in item.Connector.Tools)
                {
                    bool allowed = ToolVisibilityFilter.Offers(item, tool.Descriptor, hasModel);
                    Dictionary<string, object?> t = TemplateContext.NewMap();
                    t["name"] = tool.Descriptor.Name;
                    t["effect"] = tool.Descriptor.ClassifiedPerCall ? "Classified" : tool.Descriptor.BaseEffect.ToString();
                    t["allowed"] = allowed;
                    tools.Add(t);
                    if (allowed) usable.Add(tool.Descriptor.Name);
                }
                map["tools"] = tools;
                map["toolList"] = String.Join(", ", usable);
                map["knowledge"] = await KnowledgeAsync(item, token).ConfigureAwait(false);
                map["compact"] = compact;
                string thirdParty = includeThirdParty ? await ThirdPartyAsync(item, warnings, token).ConfigureAwait(false) : String.Empty;
                map["thirdPartyInstructions"] = thirdParty;
                map["instructions"] = ConnectorInstructions(item, map, context, templates, sources, warnings, compact, thirdParty);
                bindings.Add(map);
                if (!byFamily.TryGetValue(item.Family, out object? list) || list is not List<object?> familyList)
                {
                    familyList = new List<object?>();
                    byFamily[item.Family] = familyList;
                }
                familyList.Add(map);
            }
            context.Set("bindings", bindings);
            context.Set("bindingsByFamily", byFamily);
            return context;
        }

        private string ConnectorInstructions(BoundResource item, Dictionary<string, object?> map, TemplateContext context, Dictionary<string, ResolvedPromptTemplate> templates, List<SegmentSource> sources, List<string> warnings, bool compact, string thirdParty)
        {
            string key = item.Connector.Descriptor.DefaultInstructionsKey;
            if (compact && templates.ContainsKey(item.Connector.Descriptor.CompactInstructionsKey)) key = item.Connector.Descriptor.CompactInstructionsKey;
            string bindingText = Clean(item.Binding.InstructionText ?? String.Empty).Trim();
            string connectorText = String.Empty;
            if (item.Binding.InstructionMode != InstructionModeEnum.Replace || bindingText.Length == 0)
            {
                if (templates.TryGetValue(key, out ResolvedPromptTemplate? resolved))
                {
                    string content = Clean(resolved.Content).Trim();
                    RenderScope scope = new RenderScope();
                    scope.Current = map;
                    TemplateRenderOptions options = new TemplateRenderOptions();
                    options.TemplateName = key;
                    try
                    {
                        content = TemplateRenderer.Render(content, context, options, scope).Trim();
                    }
                    catch (TemplateException e)
                    {
                        warnings.Add("Connector instructions " + key + " for " + item.Binding.Alias + " failed to render (" + e.Message + "); the text is used as written.");
                    }
                    connectorText = Mark(sources, resolved.Key, resolved.Layer, resolved.Version, item.Binding.Alias, NestHeadings(content));
                }
            }
            if (bindingText.Length == 0 || item.Binding.InstructionMode == InstructionModeEnum.Inherit) return AppendThirdParty(sources, item, connectorText, thirdParty);
            string marked = Mark(sources, "binding.instructions", PromptLayerEnum.Tenant, 0, item.Binding.Alias, bindingText);
            if (item.Binding.InstructionMode == InstructionModeEnum.Replace) return marked;
            return AppendThirdParty(sources, item, connectorText.Length == 0 ? marked : connectorText + "\n\n" + marked, thirdParty);
        }

        private static string AppendThirdParty(List<SegmentSource> sources, BoundResource item, string text, string thirdParty)
        {
            if (thirdParty.Length == 0) return text;
            string marked = Mark(sources, "binding.thirdPartyInstructions", PromptLayerEnum.Tenant, 0, item.Binding.Alias, thirdParty);
            return text.Length == 0 ? marked : text + "\n\n" + marked;
        }

        private async Task<string> ThirdPartyAsync(BoundResource item, List<string> warnings, CancellationToken token)
        {
            IThirdPartyInstructionSource? source = ThirdPartyInstructions;
            if (source == null || _MaxThirdPartyChars == 0) return String.Empty;
            string? text;
            try
            {
                text = await source.GetAsync(item, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                // A misbehaving source never fails compilation; the binding simply goes without third-party text.
                warnings.Add("Third-party instructions for " + item.Binding.Alias + " were skipped (" + e.GetType().Name + ").");
                return String.Empty;
            }
            if (String.IsNullOrWhiteSpace(text)) return String.Empty;
            string fenced = UntrustedContent.Fence("instructions from " + item.Resource.ConnectorType + " resource " + item.Binding.Alias, Clean(text).Trim(), _MaxThirdPartyChars);
            return Clean(fenced).TrimEnd('\n');
        }

        private static string NestHeadings(string content)
        {
            // Connector instructions are written as standalone Markdown; inside a resource section (a level 2 heading)
            // their headings shift so the highest one becomes level 3 and they read as subsections of that resource.
            string[] lines = content.Split('\n');
            int[] levels = new int[lines.Length];
            int highest = 7;
            bool fenced = false;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("```", StringComparison.Ordinal)) fenced = !fenced;
                if (fenced || !lines[i].StartsWith("#", StringComparison.Ordinal)) continue;
                int level = 0;
                while (level < lines[i].Length && lines[i][level] == '#') level++;
                if (level >= lines[i].Length || lines[i][level] != ' ') continue;
                levels[i] = level;
                highest = Math.Min(highest, level);
            }
            int shift = highest > 6 ? 0 : Math.Max(0, 3 - highest);
            if (shift == 0) return content;
            for (int i = 0; i < lines.Length; i++)
            {
                if (levels[i] > 0) lines[i] = new string('#', Math.Min(6, levels[i] + shift)) + lines[i].Substring(levels[i]);
            }
            return String.Join("\n", lines);
        }

        private async Task<string> KnowledgeAsync(BoundResource item, CancellationToken token)
        {
            if (_MaxKnowledgeChars == 0) return String.Empty;
            List<ResourceKnowledge> entries = await _Database.ResourceKnowledge.ListAsync(item.Resource.TenantId, item.Resource.Id, null, token).ConfigureAwait(false);
            List<string> roots = entries.Where(e => ResourceKnowledge.IsRootKey(e.Key) && !String.IsNullOrWhiteSpace(e.Content)).Select(e => Clean(e.Content).Trim()).ToList();
            if (roots.Count == 0) return String.Empty;
            string text = String.Join("\n\n", roots);
            if (text.Length > _MaxKnowledgeChars) text = text.Substring(0, _MaxKnowledgeChars).TrimEnd() + "\n[context truncated; use the resource's knowledge tools for the rest]";
            return text;
        }

        private static string DisplayName(BoundResource item)
        {
            string name = item.Connector.Descriptor.DisplayName;
            return String.IsNullOrEmpty(name) ? item.Resource.ConnectorType : name;
        }

        private static string Mark(List<SegmentSource> sources, string key, PromptLayerEnum layer, int version, string? alias, string content)
        {
            SegmentSource source = new SegmentSource();
            source.Key = key;
            source.Layer = layer;
            source.Version = version;
            source.BindingAlias = alias;
            int index;
            lock (sources)
            {
                sources.Add(source);
                index = sources.Count - 1;
            }
            return _Open + index.ToString(CultureInfo.InvariantCulture) + _Mid + content + _Close;
        }

        private static string Extract(string marked, List<SegmentSource> sources, List<InstructionSegment> segments)
        {
            StringBuilder text = new StringBuilder(marked.Length);
            Stack<InstructionSegment> open = new Stack<InstructionSegment>();
            int i = 0;
            while (i < marked.Length)
            {
                char c = marked[i];
                if (c == _Open)
                {
                    int mid = marked.IndexOf(_Mid, i);
                    int index = Int32.Parse(marked.Substring(i + 1, mid - i - 1), CultureInfo.InvariantCulture);
                    SegmentSource source = sources[index];
                    InstructionSegment segment = new InstructionSegment();
                    segment.Key = source.Key;
                    segment.Layer = source.Layer;
                    segment.Version = source.Version;
                    segment.BindingAlias = source.BindingAlias;
                    segment.Start = text.Length;
                    segment.Depth = open.Count;
                    open.Push(segment);
                    i = mid + 1;
                    continue;
                }
                if (c == _Close)
                {
                    if (open.Count > 0)
                    {
                        InstructionSegment segment = open.Pop();
                        segment.Length = text.Length - segment.Start;
                        segments.Add(segment);
                    }
                    i++;
                    continue;
                }
                text.Append(c);
                i++;
            }
            segments.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.Depth.CompareTo(b.Depth));
            string tidy = Tidy(text.ToString());
            foreach (InstructionSegment segment in segments)
            {
                if (segment.Start > tidy.Length) segment.Start = tidy.Length;
                if (segment.Start + segment.Length > tidy.Length) segment.Length = tidy.Length - segment.Start;
            }
            return tidy;
        }

        private static string Tidy(string text)
        {
            // Only the end is trimmed, so the source map offsets stay valid.
            return text.TrimEnd() + "\n";
        }

        private static string Clean(string value)
        {
            if (String.IsNullOrEmpty(value)) return String.Empty;
            if (value.IndexOf(_Open) < 0 && value.IndexOf(_Mid) < 0 && value.IndexOf(_Close) < 0) return value.Replace("\r\n", "\n");
            StringBuilder sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (c != _Open && c != _Mid && c != _Close) sb.Append(c);
            }
            return sb.ToString().Replace("\r\n", "\n");
        }

        private string CacheKey(string tenantId, string profileId, RequestContext? principal, InvocationSurfaceEnum surface)
        {
            long generation = _Generations.TryGetValue(tenantId, out long g) ? g : 0;
            string who = principal == null ? "internal" : (principal.UserId ?? principal.PrincipalId ?? "anonymous") + (principal.IsTenantAdmin || principal.IsAdmin ? ":admin" : String.Empty);
            return tenantId + "|" + profileId + "|" + who + "|" + surface + "|" + generation + "|" + Interlocked.Read(ref _GlobalGeneration);
        }

        private static CompiledInstructions Copy(CompiledInstructions source)
        {
            CompiledInstructions copy = new CompiledInstructions();
            copy.ProfileId = source.ProfileId;
            copy.ProfileName = source.ProfileName;
            copy.Surface = source.Surface;
            copy.Text = source.Text;
            copy.TokenEstimate = source.TokenEstimate;
            copy.BindingCount = source.BindingCount;
            copy.Segments = new List<InstructionSegment>(source.Segments);
            copy.Warnings = new List<string>(source.Warnings);
            copy.FromCache = source.FromCache;
            copy.CompiledUtc = source.CompiledUtc;
            return copy;
        }

        #endregion
    }
}
