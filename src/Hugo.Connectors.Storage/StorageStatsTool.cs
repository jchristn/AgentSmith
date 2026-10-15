namespace Hugo.Connectors.Storage
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Blobject.Core;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>storage_stats</c> (Read): object count and total bytes under a prefix, with the largest object, the oldest and
    /// newest modification times, and per-folder totals. The scan is capped; a capped result is flagged as truncated and
    /// its numbers are lower bounds. Thread-safe.
    /// </summary>
    public class StorageStatsTool : StorageToolBase
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public const string ToolName = "storage_stats";

        /// <summary>
        /// Input schema shared by every storage connector.
        /// </summary>
        public const string InputSchema =
            "{\"type\":\"object\",\"properties\":{"
            + "\"prefix\":{\"type\":\"string\",\"maxLength\":1024,\"description\":\"Key prefix to summarize. Omit for the resource's prefix restriction (or the whole bucket).\"},"
            + "\"maxObjects\":{\"type\":\"integer\",\"minimum\":1,\"description\":\"Stop after scanning this many objects. Capped by the resource's maxScanObjects.\"},"
            + "\"topFolders\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":1000,\"description\":\"How many sub-folders (next '/' segment under the prefix) to report, largest first. Default 20; 0 disables the breakdown.\"}"
            + "},\"additionalProperties\":false}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="provider">Provider.</param>
        public StorageStatsTool(StorageProviderEnum provider) : base(provider, BuildDescriptor())
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override EffectClassification Classify(ConnectorInvocationContext context, ToolArguments arguments)
        {
            ArgumentNullException.ThrowIfNull(arguments);
            string? prefix = arguments.GetString("prefix");
            EffectClassification? rejected = CheckKey(context, prefix, "prefix", true);
            if (rejected != null) return rejected;
            string shown = StorageKeyPolicy.ResolvePrefix(prefix, RestrictionOf(context));
            return new EffectClassification(EffectEnum.Read, "summarizing objects is read-only", "Summarize objects under '" + shown + "' in " + Where(context));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(
            ConnectorInvocationContext context,
            ToolArguments arguments,
            StorageSession session,
            StorageOptions options,
            CancellationToken token)
        {
            string prefix = StorageKeyPolicy.ResolvePrefix(arguments.GetString("prefix"), options.PrefixRestriction);
            int cap = arguments.GetInt("maxObjects", options.MaxScanObjects, 1, options.MaxScanObjects);
            int topFolders = Math.Min(arguments.GetInt("topFolders", 20, 0, 1000), context.Limits.MaxRows);

            StorageStatsResult stats = new StorageStatsResult();
            stats.Container = session.ContainerName;
            stats.Prefix = prefix;
            stats.ScanCap = cap;
            Dictionary<string, StorageStatsGroup> groups = new Dictionary<string, StorageStatsGroup>(StringComparer.Ordinal);

            EnumerationFilter filter = new EnumerationFilter();
            filter.Prefix = prefix;
            await foreach (BlobMetadata md in session.Client.EnumerateAsync(filter, token).ConfigureAwait(false))
            {
                if (md == null || String.IsNullOrEmpty(md.Key)) continue;
                if (!md.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (stats.ObjectCount >= cap)
                {
                    stats.Truncated = true;
                    break;
                }

                stats.ObjectCount++;
                stats.TotalBytes += md.ContentLength;
                if (stats.LargestKey == null || md.ContentLength > stats.LargestBytes)
                {
                    stats.LargestKey = md.Key;
                    stats.LargestBytes = md.ContentLength;
                }
                DateTime? modified = md.LastUpdateUtc ?? md.CreatedUtc;
                if (modified != null)
                {
                    if (stats.OldestModifiedUtc == null || modified < stats.OldestModifiedUtc) stats.OldestModifiedUtc = modified;
                    if (stats.NewestModifiedUtc == null || modified > stats.NewestModifiedUtc) stats.NewestModifiedUtc = modified;
                }

                if (topFolders > 0)
                {
                    int slash = md.Key.IndexOf('/', prefix.Length);
                    string group = slash >= 0 ? md.Key.Substring(0, slash + 1) : md.Key;
                    if (!groups.TryGetValue(group, out StorageStatsGroup? g))
                    {
                        g = new StorageStatsGroup { Prefix = group };
                        groups[group] = g;
                    }
                    g.ObjectCount++;
                    g.TotalBytes += md.ContentLength;
                }
            }

            token.ThrowIfCancellationRequested();
            stats.Groups = groups.Values
                .Where(g => g.Prefix.EndsWith('/'))
                .OrderByDescending(g => g.TotalBytes)
                .ThenBy(g => g.Prefix, StringComparer.Ordinal)
                .Take(topFolders)
                .ToList();

            ToolResult result = new ToolResult();
            result.Text = OutputLimiter.Head(Render(stats), context.Limits.MaxBytes, 0, result);
            result.StructuredJson = StorageJson.Serialize(stats);
            result.Truncated = result.Truncated || stats.Truncated;
            result.Metrics["objects"] = stats.ObjectCount;
            result.Metrics["bytes"] = stats.TotalBytes;
            return result;
        }

        #endregion

        #region Private-Methods

        private static ToolDescriptor BuildDescriptor()
        {
            ToolDescriptor d = new ToolDescriptor();
            d.Name = ToolName;
            d.Title = "Summarize objects";
            d.Description =
                "Count objects and total bytes under a prefix, with the largest object, oldest and newest modification times, and "
                + "the largest sub-folders. Use this for size and growth questions instead of paging through storage_list. The "
                + "scan is capped; when truncated is true the numbers are lower bounds, so narrow the prefix.";
            d.InputSchemaJson = InputSchema;
            d.BaseEffect = EffectEnum.Read;
            d.OutputDescription = "Totals, largest object, time range, truncation flag, and a per-folder table.";
            return d;
        }

        private static string Render(StorageStatsResult stats)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Summary of '").Append(stats.Prefix).Append("' in '").Append(stats.Container).Append("'\n");
            sb.Append("objects: ").Append(stats.ObjectCount).Append(stats.Truncated ? " (scan capped; at least this many)" : String.Empty).Append('\n');
            sb.Append("totalBytes: ").Append(stats.TotalBytes).Append(" (").Append(StorageContent.FormatBytes(stats.TotalBytes)).Append(")\n");
            if (stats.LargestKey != null)
                sb.Append("largest: ").Append(stats.LargestKey).Append(" (").Append(StorageContent.FormatBytes(stats.LargestBytes)).Append(")\n");
            if (stats.OldestModifiedUtc != null) sb.Append("oldestModified: ").Append(FormatTime(stats.OldestModifiedUtc)).Append('\n');
            if (stats.NewestModifiedUtc != null) sb.Append("newestModified: ").Append(FormatTime(stats.NewestModifiedUtc)).Append('\n');
            sb.Append("truncated: ").Append(stats.Truncated ? "true" : "false").Append(" (scan cap ").Append(stats.ScanCap).Append(")\n");
            if (stats.Groups.Count > 0)
            {
                List<string> columns = new List<string> { "folder", "objects", "bytes", "size" };
                List<IList<string?>> rows = new List<IList<string?>>();
                foreach (StorageStatsGroup g in stats.Groups)
                {
                    rows.Add(new List<string?>
                    {
                        g.Prefix,
                        g.ObjectCount.ToString(CultureInfo.InvariantCulture),
                        g.TotalBytes.ToString(CultureInfo.InvariantCulture),
                        StorageContent.FormatBytes(g.TotalBytes)
                    });
                }
                sb.Append(TextTable.Render(columns, rows));
            }
            return sb.ToString();
        }

        #endregion
    }
}
