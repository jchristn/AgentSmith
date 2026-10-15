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
    /// <c>redis_scan</c>: find keys by pattern and type with SCAN, bounded by a key count and the resource's
    /// <c>maxKeysScanned</c>. Never uses KEYS. Read. Thread-safe.
    /// </summary>
    public class RedisScanTool : RedisToolBase
    {
        #region Public-Members

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string Schema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{"
            + "\"pattern\":{\"type\":\"string\",\"description\":\"Glob pattern (MATCH), for example user:* or session:??:*. Default * (every key). A specific prefix is much cheaper.\"},"
            + "\"type\":{\"type\":\"string\",\"enum\":[\"string\",\"hash\",\"list\",\"set\",\"zset\",\"stream\"],\"description\":\"Only keys of this type.\"},"
            + "\"count\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":10000,\"description\":\"Keys to return. Default 100. Capped by the row limit.\"},"
            + "\"maxScan\":{\"type\":\"integer\",\"minimum\":100,\"maximum\":1000000,\"description\":\"Most keys to examine in this call. Default and maximum: the resource's maxKeysScanned.\"},"
            + "\"cursor\":{\"type\":\"string\",\"description\":\"nextCursor from a previous redis_scan call with the same pattern and type, to continue the walk.\"},"
            + "\"details\":{\"type\":\"boolean\",\"description\":\"Also report each key's type and TTL. Default true.\"}"
            + "}}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RedisScanTool() : base(new ToolDescriptor
        {
            Name = "redis_scan",
            Title = "Scan Redis keys",
            Description = "Find keys matching a glob pattern (and optionally a type) by walking the keyspace with SCAN, never KEYS. Returns up to count keys with their type and TTL, "
                + "how many keys were examined, and a nextCursor when the walk is not finished. A walk stops after the resource's maxKeysScanned keys; continue with the cursor if needed. "
                + "Use a specific prefix pattern; an empty page with a nextCursor only means no match was found yet.",
            InputSchemaJson = Schema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "Table key, type, ttl plus structured JSON {pattern, type, keys[{key, type, ttlMs, node}], scanned, complete, scanLimitReached, nextCursor, databaseSize}."
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
            string? cursor = arguments.GetString("cursor");
            if (!String.IsNullOrWhiteSpace(cursor)) RedisScanner.ParseCursor(cursor, Int32.MaxValue, out int _, out ulong _);
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, RedisSession session, ToolArguments arguments, CancellationToken token)
        {
            int count = arguments.GetInt("count", 100, 1, Math.Min(10000, context.Limits.MaxRows));
            int budget = arguments.GetInt("maxScan", session.Options.MaxKeysScanned, 100, session.Options.MaxKeysScanned);
            RedisScanPage page = await RedisScanner.ScanAsync(session, arguments.GetString("pattern"), arguments.GetString("type"), count, budget, arguments.GetString("cursor"), token).ConfigureAwait(false);

            if (arguments.GetBool("details", true) && page.Keys.Any())
            {
                List<Task<RedisResult>> types = new List<Task<RedisResult>>();
                List<Task<RedisResult>> ttls = new List<Task<RedisResult>>();
                foreach (RedisKey key in page.RawKeys)
                {
                    types.Add(ExecAsync(session, "TYPE", new List<object> { key }, token));
                    ttls.Add(ExecAsync(session, "PTTL", new List<object> { key }, token));
                }
                await Task.WhenAll(types.Concat(ttls)).ConfigureAwait(false);
                for (int i = 0; i < page.Keys.Count; i++)
                {
                    page.Keys[i].Type = types[i].Result.ToString();
                    page.Keys[i].TtlMs = (long)ttls[i].Result;
                }
            }

            page.DatabaseSize = await RedisScanner.DatabaseSizeAsync(session, token).ConfigureAwait(false);

            RedisRowsPayload rows = new RedisRowsPayload { Tool = Descriptor.Name, Columns = new List<string> { "key", "type", "ttl" } };
            foreach (RedisScanKey k in page.Keys)
                rows.Rows.Add(new List<string?> { k.Key, k.Type, k.TtlMs.HasValue ? RedisOutput.Ttl(k.TtlMs.Value) : null });

            StringBuilder header = new StringBuilder();
            header.Append(page.Keys.Count).Append(" key(s) matching ").Append(page.Pattern);
            if (page.Type != null) header.Append(" of type ").Append(page.Type);
            header.Append("; examined about ").Append(page.Scanned.ToString(CultureInfo.InvariantCulture)).Append(" key(s)");
            if (page.DatabaseSize.HasValue) header.Append(" of ").Append(page.DatabaseSize.Value.ToString(CultureInfo.InvariantCulture)).Append(" in the database");
            header.Append(".\n");
            if (page.Complete) header.Append("The walk is complete.\n");
            else if (page.ScanLimitReached) header.Append("Stopped at the scan limit (maxKeysScanned). nextCursor: ").Append(page.NextCursor).Append('\n');
            else header.Append("More keys may match. nextCursor: ").Append(page.NextCursor).Append('\n');

            ToolResult result = RedisOutput.Table(header.ToString(), rows, context.Limits);
            result.StructuredJson = RedisJson.Serialize(page);
            result.Metrics["keys"] = page.Keys.Count;
            result.Metrics["scanned"] = page.Scanned;
            return result;
        }

        #endregion
    }
}
