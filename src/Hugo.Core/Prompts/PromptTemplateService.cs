namespace Hugo.Core.Prompts
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Services;
    using Hugo.Core.Templates;

    /// <summary>
    /// Layered prompt templates (P5.4, P5.5; D-015 adapted to Garrison's storage per D-039). Built-in templates come from
    /// <see cref="BuiltInTemplateCatalog"/>; the Global layer is prompt_templates rows with a null tenant and the key as
    /// name; the Tenant layer is rows with the tenant and the key as name. Resolution picks Tenant, then Global, then
    /// Built-in. Every save writes a version. Reset on the Global layer restores the Built-in; reset on the Tenant layer
    /// deactivates the override so the key falls back to Global (history is kept). base_hash records the hash of the
    /// layer below at synchronization time, which detects "default updated since customized". Thread-safe.
    /// </summary>
    public class PromptTemplateService
    {
        #region Public-Members

        /// <summary>
        /// Built-in catalog.
        /// </summary>
        public BuiltInTemplateCatalog BuiltIns
        {
            get
            {
                return _BuiltIns;
            }
        }

        /// <summary>
        /// Raised after any layer of any key changes (tenant identifier, or null for the Global layer). Used to invalidate
        /// compiled instructions.
        /// </summary>
        public event EventHandler<string?>? Changed;

        #endregion

        #region Private-Members

        private static readonly Regex _KeyPattern = new Regex("^[a-z][a-z0-9_-]*(\\.[a-z0-9_-]+)*$", RegexOptions.CultureInvariant);
        private readonly DatabaseDriverBase _Database;
        private readonly BuiltInTemplateCatalog _BuiltIns;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="builtIns">Built-in catalog.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public PromptTemplateService(DatabaseDriverBase database, BuiltInTemplateCatalog builtIns)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _BuiltIns = builtIns ?? throw new ArgumentNullException(nameof(builtIns));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a string is a valid template key (lowercase segments separated by dots, for example persona.agent-smith).
        /// </summary>
        /// <param name="key">Key.</param>
        /// <returns>True when valid.</returns>
        public static bool IsValidKey(string? key)
        {
            return !String.IsNullOrEmpty(key) && key.Length <= 128 && _KeyPattern.IsMatch(key);
        }

        /// <summary>
        /// Seed the Global layer from the Built-ins (P5.4). A missing Global row is created. An unedited Global row whose
        /// Built-in changed is updated to the new Built-in with a new version. An edited Global row is left alone, and its
        /// view reports DefaultUpdatedSinceCustomized.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows created or updated.</returns>
        public async Task<int> SeedGlobalDefaultsAsync(CancellationToken token = default)
        {
            int changed = 0;
            foreach (BuiltInTemplate builtIn in _BuiltIns.All())
            {
                token.ThrowIfCancellationRequested();
                PromptTemplate? global = await _Database.PromptTemplates.ReadByNameAsync(null, builtIn.Key, token).ConfigureAwait(false);
                if (global == null)
                {
                    PromptTemplate created = new PromptTemplate();
                    created.TenantId = null;
                    created.Name = builtIn.Key;
                    created.Description = builtIn.Description;
                    created.Kind = KindOf(builtIn.Key);
                    created.Content = builtIn.Content;
                    created.CurrentVersion = 1;
                    created.IsBuiltIn = true;
                    created.IsProtected = true;
                    created.BuiltInKey = builtIn.Key;
                    created.BaseHash = builtIn.Hash;
                    await _Database.PromptTemplates.CreateAsync(created, token).ConfigureAwait(false);
                    await AddVersionAsync(created, "Seeded from the built-in default.", "system", token).ConfigureAwait(false);
                    changed++;
                    continue;
                }
                bool customized = !String.Equals(BuiltInTemplateCatalog.HashOf(global.Content), global.BaseHash, StringComparison.Ordinal);
                if (!customized && !String.Equals(global.BaseHash, builtIn.Hash, StringComparison.Ordinal))
                {
                    global.Content = builtIn.Content;
                    global.BaseHash = builtIn.Hash;
                    global.CurrentVersion = global.CurrentVersion + 1;
                    global.Active = true;
                    await _Database.PromptTemplates.UpdateAsync(global, token).ConfigureAwait(false);
                    await AddVersionAsync(global, "Updated to the new built-in default.", "system", token).ConfigureAwait(false);
                    changed++;
                }
            }
            if (changed > 0) RaiseChanged(null);
            return changed;
        }

        /// <summary>
        /// Resolve one key for a tenant.
        /// </summary>
        /// <param name="tenantId">Tenant identifier, or null for the Global and Built-in layers only.</param>
        /// <param name="key">Key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Resolved template, or null when no layer defines the key.</returns>
        public async Task<ResolvedPromptTemplate?> ResolveAsync(string? tenantId, string key, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            Dictionary<string, ResolvedPromptTemplate> all = await ResolveAllAsync(tenantId, token).ConfigureAwait(false);
            return all.TryGetValue(key, out ResolvedPromptTemplate? resolved) ? resolved : null;
        }

        /// <summary>
        /// Resolve every key visible to a tenant (Built-in keys plus keys defined only in the Global or Tenant layer).
        /// </summary>
        /// <param name="tenantId">Tenant identifier, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Map from key to resolved template.</returns>
        public async Task<Dictionary<string, ResolvedPromptTemplate>> ResolveAllAsync(string? tenantId, CancellationToken token = default)
        {
            Dictionary<string, ResolvedPromptTemplate> result = new Dictionary<string, ResolvedPromptTemplate>(StringComparer.Ordinal);
            foreach (BuiltInTemplate builtIn in _BuiltIns.All())
            {
                result[builtIn.Key] = Resolved(builtIn.Key, builtIn.Content, PromptLayerEnum.BuiltIn, 0, null);
            }
            List<PromptTemplate> rows = await RowsAsync(tenantId, token).ConfigureAwait(false);
            foreach (PromptTemplate row in rows.Where(r => r.TenantId == null && r.Active && IsLayerRow(r)))
            {
                result[row.Name] = Resolved(row.Name, row.Content, PromptLayerEnum.Global, row.CurrentVersion, row.Id);
            }
            if (!String.IsNullOrEmpty(tenantId))
            {
                foreach (PromptTemplate row in rows.Where(r => r.TenantId == tenantId && r.Active && IsLayerRow(r)))
                {
                    result[row.Name] = Resolved(row.Name, row.Content, PromptLayerEnum.Tenant, row.CurrentVersion, row.Id);
                }
            }
            return result;
        }

        /// <summary>
        /// Every layer of one key.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="key">Key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View, or null when no layer defines the key.</returns>
        public async Task<PromptTemplateView?> ViewAsync(string tenantId, string key, CancellationToken token = default)
        {
            List<PromptTemplateView> views = await ViewsAsync(tenantId, token).ConfigureAwait(false);
            return views.FirstOrDefault(v => v.Key == key);
        }

        /// <summary>
        /// Every key's layers for a tenant, ordered by key.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Views.</returns>
        public async Task<List<PromptTemplateView>> ViewsAsync(string tenantId, CancellationToken token = default)
        {
            List<PromptTemplate> rows = await RowsAsync(tenantId, token).ConfigureAwait(false);
            HashSet<string> keys = new HashSet<string>(_BuiltIns.All().Select(b => b.Key), StringComparer.Ordinal);
            foreach (PromptTemplate row in rows.Where(IsLayerRow)) keys.Add(row.Name);
            List<PromptTemplateView> views = new List<PromptTemplateView>();
            foreach (string key in keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                BuiltInTemplate? builtIn = _BuiltIns.Get(key);
                PromptTemplate? global = rows.FirstOrDefault(r => r.TenantId == null && r.Name == key && IsLayerRow(r));
                PromptTemplate? tenant = rows.FirstOrDefault(r => r.TenantId == tenantId && r.Name == key && IsLayerRow(r));
                PromptTemplateView view = new PromptTemplateView();
                view.Key = key;
                view.Description = builtIn?.Description ?? global?.Description ?? tenant?.Description ?? String.Empty;
                view.Source = builtIn?.Source ?? "custom";
                view.BuiltInContent = builtIn?.Content;
                view.BuiltInHash = builtIn?.Hash;
                string? globalEffective = global != null && global.Active ? global.Content : null;
                view.Global = State(PromptLayerEnum.Global, global, builtIn?.Hash);
                string? below = globalEffective ?? builtIn?.Content;
                view.Tenant = State(PromptLayerEnum.Tenant, tenant, below == null ? null : BuiltInTemplateCatalog.HashOf(below));
                if (tenant != null && tenant.Active)
                {
                    view.ActiveLayer = PromptLayerEnum.Tenant;
                    view.EffectiveContent = tenant.Content;
                }
                else if (globalEffective != null)
                {
                    view.ActiveLayer = PromptLayerEnum.Global;
                    view.EffectiveContent = globalEffective;
                }
                else
                {
                    view.ActiveLayer = PromptLayerEnum.BuiltIn;
                    view.EffectiveContent = builtIn?.Content ?? String.Empty;
                }
                views.Add(view);
            }
            return views;
        }

        /// <summary>
        /// Save content at a layer, creating a version.
        /// </summary>
        /// <param name="tenantId">Tenant identifier (required for the Tenant layer; ignored for Global).</param>
        /// <param name="layer">Global or Tenant.</param>
        /// <param name="key">Key.</param>
        /// <param name="content">Content.</param>
        /// <param name="note">Change note, or null.</param>
        /// <param name="editedBy">Who edited it.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The view after saving.</returns>
        /// <exception cref="ValidationException">Thrown when the key, layer, or template is invalid.</exception>
        public async Task<PromptTemplateView> SaveAsync(string tenantId, PromptLayerEnum layer, string key, string content, string? note, string? editedBy, CancellationToken token = default)
        {
            if (!IsValidKey(key)) throw Invalid("key", "The template key must be lowercase segments separated by dots, at most 128 characters.");
            if (layer == PromptLayerEnum.BuiltIn) throw Invalid("layer", "The Built-in layer cannot be edited; save to the Global or Tenant layer.");
            if (content == null || content.Trim().Length == 0) throw Invalid("content", "Content is required.");
            if (content.Length > 262144) throw Invalid("content", "Content is longer than 262144 characters.");
            TemplateAnalysis analysis = TemplateAnalyzer.Analyze(content, key, false);
            if (!analysis.Valid) throw Invalid("content", "The template is invalid: " + String.Join(" ", analysis.Errors));
            string normalized = content.Replace("\r\n", "\n");

            PromptTemplate? row = await LayerRowAsync(layer == PromptLayerEnum.Tenant ? tenantId : null, key, token).ConfigureAwait(false);
            string? baseHash = await LowerHashAsync(layer, tenantId, key, token).ConfigureAwait(false);
            if (row == null)
            {
                row = new PromptTemplate();
                row.TenantId = layer == PromptLayerEnum.Tenant ? tenantId : null;
                row.Name = key;
                row.Description = _BuiltIns.Get(key)?.Description;
                row.Kind = KindOf(key);
                row.Content = normalized;
                row.CurrentVersion = 1;
                row.IsBuiltIn = layer == PromptLayerEnum.Global;
                row.IsProtected = layer == PromptLayerEnum.Global;
                row.BuiltInKey = key;
                row.BaseHash = baseHash;
                await _Database.PromptTemplates.CreateAsync(row, token).ConfigureAwait(false);
                await AddVersionAsync(row, note ?? "Created.", editedBy, token).ConfigureAwait(false);
            }
            else
            {
                bool reactivated = !row.Active;
                if (row.Content != normalized || reactivated)
                {
                    // A reactivated tenant override starts from the current lower layer, so its customization base moves.
                    if (reactivated) row.BaseHash = baseHash;
                    row.Content = normalized;
                    row.Active = true;
                    row.CurrentVersion = row.CurrentVersion + 1;
                    await _Database.PromptTemplates.UpdateAsync(row, token).ConfigureAwait(false);
                    await AddVersionAsync(row, note ?? "Updated.", editedBy, token).ConfigureAwait(false);
                }
            }
            RaiseChanged(layer == PromptLayerEnum.Tenant ? tenantId : null);
            return (await ViewAsync(tenantId, key, token).ConfigureAwait(false))!;
        }

        /// <summary>
        /// Reset a layer to its default: Global returns to the Built-in content (adopting a changed Built-in); Tenant is
        /// deactivated so the key falls back to Global.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="layer">Global or Tenant.</param>
        /// <param name="key">Key.</param>
        /// <param name="editedBy">Who reset it.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The view after resetting.</returns>
        /// <exception cref="ValidationException">Thrown when the layer is Built-in or the key has no Built-in to reset Global to.</exception>
        /// <exception cref="NotFoundException">Thrown when the key is unknown.</exception>
        public async Task<PromptTemplateView> ResetAsync(string tenantId, PromptLayerEnum layer, string key, string? editedBy, CancellationToken token = default)
        {
            if (!IsValidKey(key)) throw Invalid("key", "Invalid template key.");
            if (layer == PromptLayerEnum.BuiltIn) throw Invalid("layer", "The Built-in layer cannot be reset.");
            PromptTemplate? row = await LayerRowAsync(layer == PromptLayerEnum.Tenant ? tenantId : null, key, token).ConfigureAwait(false);
            if (layer == PromptLayerEnum.Global)
            {
                BuiltInTemplate? builtIn = _BuiltIns.Get(key);
                if (builtIn == null)
                {
                    if (row == null) throw new NotFoundException("Prompt template " + key + " not found.");
                    if (row.Active)
                    {
                        row.Active = false;
                        row.CurrentVersion = row.CurrentVersion + 1;
                        await _Database.PromptTemplates.UpdateAsync(row, token).ConfigureAwait(false);
                        await AddVersionAsync(row, "Reset: the Global layer was removed (no built-in default exists).", editedBy, token).ConfigureAwait(false);
                    }
                }
                else if (row == null)
                {
                    await SeedGlobalDefaultsAsync(token).ConfigureAwait(false);
                }
                else if (row.Content != builtIn.Content || row.BaseHash != builtIn.Hash || !row.Active)
                {
                    row.Content = builtIn.Content;
                    row.BaseHash = builtIn.Hash;
                    row.Active = true;
                    row.CurrentVersion = row.CurrentVersion + 1;
                    await _Database.PromptTemplates.UpdateAsync(row, token).ConfigureAwait(false);
                    await AddVersionAsync(row, "Reset to the built-in default.", editedBy, token).ConfigureAwait(false);
                }
            }
            else if (row != null && row.Active)
            {
                row.Active = false;
                row.CurrentVersion = row.CurrentVersion + 1;
                await _Database.PromptTemplates.UpdateAsync(row, token).ConfigureAwait(false);
                await AddVersionAsync(row, "Reset to the Global default.", editedBy, token).ConfigureAwait(false);
            }
            RaiseChanged(layer == PromptLayerEnum.Tenant ? tenantId : null);
            PromptTemplateView? view = await ViewAsync(tenantId, key, token).ConfigureAwait(false);
            return view ?? throw new NotFoundException("Prompt template " + key + " not found.");
        }

        /// <summary>
        /// Version history of one layer, newest first.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="layer">Global or Tenant.</param>
        /// <param name="key">Key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Versions; empty when the layer has no row.</returns>
        public async Task<List<PromptTemplateVersion>> VersionsAsync(string tenantId, PromptLayerEnum layer, string key, CancellationToken token = default)
        {
            if (layer == PromptLayerEnum.BuiltIn) return new List<PromptTemplateVersion>();
            PromptTemplate? row = await LayerRowAsync(layer == PromptLayerEnum.Tenant ? tenantId : null, key, token).ConfigureAwait(false);
            if (row == null) return new List<PromptTemplateVersion>();
            List<PromptTemplateVersion> versions = await _Database.PromptTemplateVersions.ListByTemplateAsync(row.Id, token).ConfigureAwait(false);
            return versions.OrderByDescending(v => v.Version).ToList();
        }

        /// <summary>
        /// Line diff between two texts (longest common subsequence).
        /// </summary>
        /// <param name="from">Original text.</param>
        /// <param name="to">New text.</param>
        /// <returns>Lines with equal, add, or remove operations.</returns>
        public static List<PromptDiffLine> Diff(string? from, string? to)
        {
            string[] a = (from ?? String.Empty).Replace("\r\n", "\n").Split('\n');
            string[] b = (to ?? String.Empty).Replace("\r\n", "\n").Split('\n');
            int[,] lcs = new int[a.Length + 1, b.Length + 1];
            for (int i = a.Length - 1; i >= 0; i--)
            {
                for (int j = b.Length - 1; j >= 0; j--)
                {
                    lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
                }
            }
            List<PromptDiffLine> lines = new List<PromptDiffLine>();
            int x = 0;
            int y = 0;
            while (x < a.Length && y < b.Length)
            {
                if (a[x] == b[y])
                {
                    lines.Add(new PromptDiffLine { Op = "equal", Text = a[x] });
                    x++;
                    y++;
                }
                else if (lcs[x + 1, y] >= lcs[x, y + 1])
                {
                    lines.Add(new PromptDiffLine { Op = "remove", Text = a[x] });
                    x++;
                }
                else
                {
                    lines.Add(new PromptDiffLine { Op = "add", Text = b[y] });
                    y++;
                }
            }
            while (x < a.Length) lines.Add(new PromptDiffLine { Op = "remove", Text = a[x++] });
            while (y < b.Length) lines.Add(new PromptDiffLine { Op = "add", Text = b[y++] });
            return lines;
        }

        #endregion

        #region Private-Methods

        private async Task<List<PromptTemplate>> RowsAsync(string? tenantId, CancellationToken token)
        {
            List<PromptTemplate> rows = new List<PromptTemplate>();
            EnumerationQuery query = new EnumerationQuery();
            query.MaxResults = 1000;
            query.IncludeBuiltIn = true;
            query.Ordering = EnumerationOrderEnum.NameAscending;
            while (true)
            {
                EnumerationResult<PromptTemplate> page = await _Database.PromptTemplates.EnumerateAsync(String.IsNullOrEmpty(tenantId) ? null : tenantId, query, token).ConfigureAwait(false);
                rows.AddRange(String.IsNullOrEmpty(tenantId) ? page.Objects.Where(r => r.TenantId == null) : page.Objects);
                if (page.EndOfResults || page.Objects.Count == 0) break;
                query.Skip = query.Skip + page.Objects.Count;
            }
            return rows;
        }

        private async Task<PromptTemplate?> LayerRowAsync(string? tenantId, string key, CancellationToken token)
        {
            PromptTemplate? row = await _Database.PromptTemplates.ReadByNameAsync(tenantId, key, token).ConfigureAwait(false);
            if (row == null) return null;
            if (String.IsNullOrEmpty(tenantId)) return row.TenantId == null ? row : null;
            return row.TenantId == tenantId ? row : null;
        }

        private async Task<string?> LowerHashAsync(PromptLayerEnum layer, string tenantId, string key, CancellationToken token)
        {
            BuiltInTemplate? builtIn = _BuiltIns.Get(key);
            if (layer == PromptLayerEnum.Global) return builtIn?.Hash;
            PromptTemplate? global = await LayerRowAsync(null, key, token).ConfigureAwait(false);
            if (global != null && global.Active) return BuiltInTemplateCatalog.HashOf(global.Content);
            return builtIn?.Hash;
        }

        private async Task AddVersionAsync(PromptTemplate template, string note, string? editedBy, CancellationToken token)
        {
            PromptTemplateVersion version = new PromptTemplateVersion();
            version.TenantId = template.TenantId;
            version.TemplateId = template.Id;
            version.Version = template.CurrentVersion;
            version.Content = template.Content;
            version.ChangeNote = note;
            version.CreatedBy = editedBy;
            await _Database.PromptTemplateVersions.CreateAsync(version, token).ConfigureAwait(false);
        }

        private static PromptLayerState? State(PromptLayerEnum layer, PromptTemplate? row, string? lowerHash)
        {
            PromptLayerState state = new PromptLayerState();
            state.Layer = layer;
            if (row == null) return state;
            state.Exists = row.Active;
            state.TemplateId = row.Id;
            state.Content = row.Content;
            state.Version = row.CurrentVersion;
            state.LastUpdateUtc = row.LastUpdateUtc;
            state.Customized = row.Active && !String.Equals(BuiltInTemplateCatalog.HashOf(row.Content), row.BaseHash, StringComparison.Ordinal);
            state.DefaultUpdatedSinceCustomized = row.Active && lowerHash != null && row.BaseHash != null && !String.Equals(row.BaseHash, lowerHash, StringComparison.Ordinal);
            return state;
        }

        private static bool IsLayerRow(PromptTemplate row)
        {
            return !String.IsNullOrEmpty(row.BuiltInKey) && String.Equals(row.BuiltInKey, row.Name, StringComparison.Ordinal) && IsValidKey(row.Name);
        }

        private static ResolvedPromptTemplate Resolved(string key, string content, PromptLayerEnum layer, int version, string? id)
        {
            ResolvedPromptTemplate resolved = new ResolvedPromptTemplate();
            resolved.Key = key;
            resolved.Content = content;
            resolved.Layer = layer;
            resolved.Version = version;
            resolved.TemplateId = id;
            return resolved;
        }

        private static PromptTemplateKindEnum KindOf(string key)
        {
            if (key.StartsWith("connector.", StringComparison.Ordinal) || key.StartsWith("section.", StringComparison.Ordinal)) return PromptTemplateKindEnum.Fragment;
            return PromptTemplateKindEnum.System;
        }

        private static ValidationException Invalid(string field, string message)
        {
            return new ValidationException(message, new Dictionary<string, string> { { field, message } });
        }

        private void RaiseChanged(string? tenantId)
        {
            EventHandler<string?>? handler = Changed;
            if (handler == null) return;
            try
            {
                handler(this, tenantId);
            }
            catch (Exception)
            {
                // A cache invalidation hook must never break the save that triggered it.
            }
        }

        #endregion
    }
}
