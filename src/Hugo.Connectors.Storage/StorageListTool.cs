namespace Hugo.Connectors.Storage
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Blobject.Core;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>storage_list</c> (Read): list objects under a prefix, optionally grouped into folders at a delimiter, with
    /// continuation tokens. Blobject enumerates flat listings and cannot resume from a marker, so folder grouping and
    /// paging are computed by <see cref="StorageListPager"/> over a lexicographically ordered scan: each page re-walks
    /// from the start of the prefix, skips up to the token's key (at most maxScanObjects times
    /// <see cref="SkipBudgetMultiplier"/> keys), then examines at most maxScanObjects more. Thread-safe.
    /// </summary>
    public class StorageListTool : StorageToolBase
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public const string ToolName = "storage_list";

        /// <summary>
        /// How many keys a page after the first may re-walk and skip, as a multiple of the resource's maxScanObjects.
        /// Blobject cannot resume a listing from a marker, so this bounds how deep paging under one prefix can go.
        /// Default 10, minimum 1, maximum 100.
        /// </summary>
        public int SkipBudgetMultiplier
        {
            get
            {
                return _SkipBudgetMultiplier;
            }
            set
            {
                _SkipBudgetMultiplier = Math.Clamp(value, 1, 100);
            }
        }

        /// <summary>
        /// Input schema shared by every storage connector.
        /// </summary>
        public const string InputSchema =
            "{\"type\":\"object\",\"properties\":{"
            + "\"prefix\":{\"type\":\"string\",\"maxLength\":1024,\"description\":\"Key prefix to list, for example 'logs/2026/'. Omit to list from the resource's prefix restriction (or the bucket root).\"},"
            + "\"delimiter\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":8,\"description\":\"Group keys into folders at this delimiter (usually '/'), like a directory listing. Omit for a flat, recursive listing.\"},"
            + "\"continuationToken\":{\"type\":\"string\",\"description\":\"Token returned by a previous storage_list call with the same prefix and delimiter, to fetch the next page.\"},"
            + "\"maxResults\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":10000,\"description\":\"Maximum entries to return. Capped by the resource's maxListResults and the output row limit.\"}"
            + "},\"additionalProperties\":false}";

        #endregion

        #region Private-Members

        private int _SkipBudgetMultiplier = 10;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="provider">Provider.</param>
        public StorageListTool(StorageProviderEnum provider) : base(provider, BuildDescriptor())
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
            return new EffectClassification(EffectEnum.Read, "listing objects is read-only", "List objects under '" + shown + "' in " + Where(context));
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
            string? delimiter = arguments.GetString("delimiter");
            if (delimiter != null && (delimiter.Length < 1 || delimiter.Length > 8))
                throw new ConnectorException("invalid_argument", "Argument 'delimiter' must be 1 to 8 characters.");
            string? startAfter = StorageContinuationToken.Decode(arguments.GetString("continuationToken"), prefix);
            int cap = Math.Min(options.MaxListResults, context.Limits.MaxRows);
            int maxResults = arguments.GetInt("maxResults", cap, 1, cap);

            EnumerationFilter filter = new EnumerationFilter();
            filter.Prefix = prefix;
            StorageListPager pager = new StorageListPager();
            pager.MaxResults = maxResults;
            pager.MaxScanObjects = options.MaxScanObjects;
            pager.MaxSkipObjects = (long)options.MaxScanObjects * _SkipBudgetMultiplier;
            StorageListResult result = await pager.PageAsync(session.Client.EnumerateAsync(filter, token), prefix, delimiter, startAfter, ToInfo, token).ConfigureAwait(false);
            result.Container = session.ContainerName;
            long scanned = result.Scanned;

            ToolResult tool = new ToolResult();
            tool.Text = OutputLimiter.Head(Render(result), context.Limits.MaxBytes, 0, tool);
            tool.StructuredJson = StorageJson.Serialize(result);
            tool.Truncated = tool.Truncated || result.ContinuationToken != null;
            tool.Metrics["rows"] = result.Entries.Count;
            tool.Metrics["scanned"] = scanned;
            tool.Metrics["skipped"] = result.Skipped;
            return tool;
        }

        #endregion

        #region Private-Methods

        private static ToolDescriptor BuildDescriptor()
        {
            ToolDescriptor d = new ToolDescriptor();
            d.Name = ToolName;
            d.Title = "List objects";
            d.Description =
                "List objects in the bucket or container under a key prefix. Use delimiter '/' to browse one folder level at a time "
                + "(folders end with '/'); omit it for a flat recursive listing. Results are in key order and paged: when a "
                + "continuationToken is returned, call again with the same prefix and delimiter plus that token. Keys outside the "
                + "resource's prefix restriction are not visible. For totals use storage_stats instead of paging through everything.";
            d.InputSchemaJson = InputSchema;
            d.BaseEffect = EffectEnum.Read;
            d.OutputDescription = "A table of key, type, size, lastModified, contentType, then a continuation token when more entries exist.";
            return d;
        }

        private static string Render(StorageListResult result)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Listing of '").Append(result.Prefix).Append("' in '").Append(result.Container).Append('\'');
            if (result.Delimiter != null) sb.Append(" (delimiter '").Append(result.Delimiter).Append("')");
            sb.Append(": ").Append(result.ObjectCount).Append(" object(s), ").Append(result.FolderCount).Append(" folder(s).\n");
            if (result.Entries.Count == 0)
            {
                sb.Append("No objects found.\n");
            }
            else
            {
                List<string> columns = new List<string> { "key", "type", "size", "lastModified", "contentType" };
                List<IList<string?>> rows = new List<IList<string?>>();
                foreach (StorageObjectInfo e in result.Entries)
                {
                    rows.Add(new List<string?>
                    {
                        e.Key,
                        e.IsFolder ? "folder" : "object",
                        e.IsFolder ? String.Empty : e.Size.ToString(CultureInfo.InvariantCulture),
                        FormatTime(e.LastModifiedUtc),
                        e.ContentType ?? String.Empty
                    });
                }
                sb.Append(TextTable.Render(columns, rows));
            }
            if (result.ScanTruncated) sb.Append("The scan stopped after ").Append(result.Scanned).Append(" objects (maxScanObjects); more may exist.\n");
            if (result.ContinuationToken != null) sb.Append("More entries exist. Call storage_list again with continuationToken \"").Append(result.ContinuationToken).Append("\".\n");
            return sb.ToString();
        }

        #endregion
    }
}
