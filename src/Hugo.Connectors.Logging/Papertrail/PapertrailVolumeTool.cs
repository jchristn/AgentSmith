namespace Hugo.Connectors.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>logs_volume</c> for Papertrail: counts matching events in equal time buckets by paging backward through the
    /// window, up to a scan cap. Papertrail has no aggregation endpoint, so this is the only way to count. Read.
    /// Thread-safe.
    /// </summary>
    public class PapertrailVolumeTool : LoggingToolBase
    {
        #region Public-Members

        /// <summary>
        /// Events requested per page while counting. Minimum 100, maximum 10000 (the API maximum), default 10000.
        /// </summary>
        public int PageSize
        {
            get
            {
                return _PageSize;
            }
            set
            {
                _PageSize = Math.Clamp(value, 100, 10000);
            }
        }

        #endregion

        #region Private-Members

        private int _PageSize = 10000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="options">Connector options.</param>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        public PapertrailVolumeTool(LoggingConnectorOptions options) : base(LogsToolSchemas.Volume(), options)
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token)
        {
            PapertrailClient client = PapertrailClient.FromResource(context.Resource);
            PapertrailSettings settings = client.Settings;
            LogsProviderOptions options = LogsProviderOptions.FromArguments(arguments);
            options.Require(PapertrailClient.ProviderName, true, false, false);
            TimeRange range = TimeRangeResolver.Resolve(arguments.GetString("startTime"), arguments.GetString("endTime"), Options.DefaultWindow, settings.MaxTimeRangeHours, DateTime.UtcNow);
            int bucketCount = arguments.GetInt("buckets", 12, 1, 120);
            int maxScan = arguments.GetInt("maxScanEvents", 10000, 100, 100000);
            string? query = arguments.GetString("query");
            query = String.IsNullOrWhiteSpace(query) ? null : query.Trim();
            string? groupId = await client.ResolveGroupIdAsync(options.Group ?? settings.DefaultGroup, token).ConfigureAwait(false);

            double bucketSeconds = range.Duration.TotalSeconds / bucketCount;
            LogVolumePayload payload = new LogVolumePayload
            {
                Provider = PapertrailConnector.TypeName,
                Query = query,
                StartTime = range.Start,
                EndTime = range.End,
                BucketSeconds = Math.Round(bucketSeconds, 3)
            };
            for (int i = 0; i < bucketCount; i++)
            {
                payload.Buckets.Add(new LogVolumeBucket
                {
                    Start = range.Start.AddSeconds(bucketSeconds * i),
                    End = i == bucketCount - 1 ? range.End : range.Start.AddSeconds(bucketSeconds * (i + 1))
                });
            }

            string? maxId = null;
            long scanned = 0;
            DateTime? oldestSeen = null;
            bool complete = false;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                int pageLimit = (int)Math.Min(PageSize, maxScan - scanned);
                if (pageLimit <= 0) break;

                PapertrailSearchRequest request = new PapertrailSearchRequest
                {
                    Query = query,
                    SystemId = options.System ?? settings.DefaultSystem,
                    GroupId = groupId,
                    MinTime = range.Start,
                    MaxTime = range.End,
                    MaxId = maxId,
                    Limit = pageLimit
                };
                PapertrailSearchResponse response = await client.SearchAsync(request, token).ConfigureAwait(false);
                payload.Pages++;

                foreach (PapertrailEvent e in response.Events!)
                {
                    LogEvent le = e.ToLogEvent();
                    if (le.Timestamp < range.Start || le.Timestamp > range.End) continue;
                    int index = (int)Math.Floor((le.Timestamp - range.Start).TotalSeconds / bucketSeconds);
                    index = Math.Clamp(index, 0, bucketCount - 1);
                    payload.Buckets[index].Count++;
                    payload.Total++;
                    if (!oldestSeen.HasValue || le.Timestamp < oldestSeen.Value) oldestSeen = le.Timestamp;
                }
                scanned += response.Events!.Count;

                if (!response.HasOlder)
                {
                    complete = true;
                    break;
                }
                if (String.Equals(maxId, response.MinId, StringComparison.Ordinal)) break;
                maxId = response.MinId;
            }

            payload.Complete = complete;
            if (!complete)
            {
                payload.ScannedFrom = oldestSeen ?? range.End;
                foreach (LogVolumeBucket b in payload.Buckets)
                    if (b.Start < payload.ScannedFrom.Value) b.Complete = false;
            }

            List<IList<string?>> rows = payload.Buckets
                .Select(b => (IList<string?>)new List<string?>
                {
                    TimeRange.Format(b.Start),
                    TimeRange.Format(b.End),
                    b.Count.ToString(CultureInfo.InvariantCulture),
                    b.Complete ? "yes" : "partial"
                })
                .ToList();

            string footer = "Total " + payload.Total.ToString(CultureInfo.InvariantCulture) + " events in " + payload.Pages.ToString(CultureInfo.InvariantCulture) + " page(s).";
            if (!complete)
            {
                footer += " The scan stopped at maxScanEvents (" + maxScan.ToString(CultureInfo.InvariantCulture) + ") after reaching "
                    + TimeRange.Format(payload.ScannedFrom!.Value) + "; earlier buckets are lower bounds. Use a narrower query or window, or raise maxScanEvents.";
            }

            OutputLimits tableLimits = new OutputLimits { MaxBytes = context.Limits.MaxBytes, MaxLines = context.Limits.MaxLines, MaxRows = Math.Max(context.Limits.MaxRows, bucketCount) };
            string title = "Papertrail volume" + (query == null ? String.Empty : " for " + query) + " between " + TimeRange.Format(range.Start) + " and " + TimeRange.Format(range.End);
            ToolResult result = ListToolHelper.Render(title, new List<string> { "bucketStart", "bucketEnd", "count", "complete" }, rows, payload, tableLimits, footer);
            result.Metrics["scanned"] = scanned;
            result.Metrics["pages"] = payload.Pages;
            return result;
        }

        #endregion
    }
}
