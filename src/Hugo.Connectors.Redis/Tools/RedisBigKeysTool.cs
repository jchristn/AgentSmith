namespace Hugo.Connectors.Redis.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using StackExchange.Redis;

    /// <summary>
    /// <c>redis_bigkeys</c>: a sampled SCAN that measures MEMORY USAGE of each sampled key and reports the largest, with
    /// totals by type. Read. Thread-safe.
    /// </summary>
    public class RedisBigKeysTool : RedisToolBase
    {
        #region Public-Members

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string Schema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{"
            + "\"pattern\":{\"type\":\"string\",\"description\":\"Glob pattern limiting the sample, for example cache:*. Default *.\"},"
            + "\"type\":{\"type\":\"string\",\"enum\":[\"string\",\"hash\",\"list\",\"set\",\"zset\",\"stream\"],\"description\":\"Only sample keys of this type.\"},"
            + "\"sampleSize\":{\"type\":\"integer\",\"minimum\":10,\"maximum\":1000000,\"description\":\"Keys to measure. Default 1000. Capped by the resource's maxKeysScanned.\"},"
            + "\"top\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":1000,\"description\":\"Largest keys to report. Default 20. Capped by the row limit.\"}"
            + "}}";

        /// <summary>
        /// Keys measured per pipelined batch. Minimum 1, maximum 1000, default 100.
        /// </summary>
        public int BatchSize
        {
            get
            {
                return _BatchSize;
            }
            set
            {
                _BatchSize = Math.Clamp(value, 1, 1000);
            }
        }

        #endregion

        #region Private-Members

        private int _BatchSize = 100;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RedisBigKeysTool() : base(new ToolDescriptor
        {
            Name = "redis_bigkeys",
            Title = "Find big Redis keys",
            Description = "Sample keys with SCAN (never KEYS), measure each with MEMORY USAGE, and report the largest keys plus count and bytes by type. "
                + "It is a sample, not a full census: say so when reporting, and raise sampleSize or narrow pattern for more certainty.",
            InputSchemaJson = Schema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "Header with sample size and per-type totals, a table key, type, bytes, ttl, plus structured rows."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void ValidateArguments(ConnectorInvocationContext context, RedisConnectionOptions options, ToolArguments arguments)
        {
            string? type = arguments.GetString("type");
            if (!String.IsNullOrWhiteSpace(type) && !RedisScanner.KeyTypes.Contains(type.Trim().ToLowerInvariant()))
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "type must be one of " + String.Join(", ", RedisScanner.KeyTypes) + ".");
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, RedisSession session, ToolArguments arguments, CancellationToken token)
        {
            int sample = arguments.GetInt("sampleSize", Math.Min(1000, session.Options.MaxKeysScanned), 10, session.Options.MaxKeysScanned);
            int top = arguments.GetInt("top", 20, 1, Math.Min(1000, context.Limits.MaxRows));
            RedisScanPage page = await RedisScanner.ScanAsync(session, arguments.GetString("pattern"), arguments.GetString("type"), sample, session.Options.MaxKeysScanned, null, token).ConfigureAwait(false);
            int measuredCount = Math.Min(sample, page.RawKeys.Count);

            List<long> bytes = new List<long>();
            List<string> types = new List<string>();
            List<long> ttls = new List<long>();
            for (int start = 0; start < measuredCount; start += BatchSize)
            {
                token.ThrowIfCancellationRequested();
                List<RedisKey> batch = page.RawKeys.Skip(start).Take(Math.Min(BatchSize, measuredCount - start)).ToList();
                List<Task<RedisResult>> usage = batch.Select(k => ExecAsync(session, "MEMORY", new List<object> { "USAGE", k, "SAMPLES", 5 }, token)).ToList();
                List<Task<RedisResult>> type = batch.Select(k => ExecAsync(session, "TYPE", new List<object> { k }, token)).ToList();
                List<Task<RedisResult>> ttl = batch.Select(k => ExecAsync(session, "PTTL", new List<object> { k }, token)).ToList();
                await Task.WhenAll(usage.Concat(type).Concat(ttl)).ConfigureAwait(false);
                for (int i = 0; i < batch.Count; i++)
                {
                    bytes.Add(usage[i].Result.IsNull ? 0 : (long)usage[i].Result);
                    types.Add(type[i].Result.ToString() ?? "none");
                    ttls.Add((long)ttl[i].Result);
                }
            }

            Dictionary<string, long> countByType = new Dictionary<string, long>(StringComparer.Ordinal);
            Dictionary<string, long> bytesByType = new Dictionary<string, long>(StringComparer.Ordinal);
            for (int i = 0; i < bytes.Count; i++)
            {
                countByType[types[i]] = countByType.TryGetValue(types[i], out long c) ? c + 1 : 1;
                bytesByType[types[i]] = bytesByType.TryGetValue(types[i], out long b) ? b + bytes[i] : bytes[i];
            }

            RedisRowsPayload rows = new RedisRowsPayload { Tool = Descriptor.Name, Columns = new List<string> { "key", "type", "bytes", "ttl" }, Total = bytes.Count };
            foreach (int i in Enumerable.Range(0, bytes.Count).OrderByDescending(i => bytes[i]).Take(top))
                rows.Rows.Add(new List<string?> { page.Keys[i].Key, types[i], bytes[i].ToString(CultureInfo.InvariantCulture), RedisOutput.Ttl(ttls[i]) });

            long? dbSize = await RedisScanner.DatabaseSizeAsync(session, token).ConfigureAwait(false);
            rows.Notes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "sampledKeys", bytes.Count.ToString(CultureInfo.InvariantCulture) },
                { "databaseKeys", dbSize.HasValue ? dbSize.Value.ToString(CultureInfo.InvariantCulture) : "unknown" },
                { "sampledBytes", bytes.Sum().ToString(CultureInfo.InvariantCulture) },
                { "byType", String.Join(", ", countByType.OrderByDescending(p => bytesByType[p.Key]).Select(p => p.Key + "=" + p.Value + " keys/" + bytesByType[p.Key] + " bytes")) },
                { "complete", page.Complete && measuredCount >= page.RawKeys.Count ? "true (every matching key was measured)" : "false (sample)" }
            };

            StringBuilder header = new StringBuilder();
            header.Append("Largest ").Append(rows.Rows.Count).Append(" of ").Append(bytes.Count).Append(" sampled key(s)");
            if (dbSize.HasValue) header.Append(" (database holds ").Append(dbSize.Value.ToString(CultureInfo.InvariantCulture)).Append(" keys)");
            header.Append(".\n");
            ToolResult result = RedisOutput.Table(header.ToString(), rows, context.Limits);
            result.Metrics["sampled"] = bytes.Count;
            return result;
        }

        #endregion
    }
}
