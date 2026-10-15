namespace Hugo.Connectors.Redis.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using StackExchange.Redis;

    /// <summary>
    /// <c>redis_memory_usage</c>: MEMORY USAGE of up to 50 keys, with their types. Read. Thread-safe.
    /// </summary>
    public class RedisMemoryUsageTool : RedisToolBase
    {
        #region Public-Members

        /// <summary>
        /// Most keys per call.
        /// </summary>
        public static readonly int MaxKeys = 50;

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string Schema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"keys\"],\"properties\":{"
            + "\"keys\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"minItems\":1,\"maxItems\":50,\"description\":\"Key names (up to 50).\"},"
            + "\"samples\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":10000,\"description\":\"Nested elements sampled to estimate collection size (MEMORY USAGE SAMPLES). 0 reads every element, which is slow for large keys. Default 5.\"}"
            + "}}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RedisMemoryUsageTool() : base(new ToolDescriptor
        {
            Name = "redis_memory_usage",
            Title = "Redis key memory usage",
            Description = "Estimate the memory used by up to 50 keys (MEMORY USAGE, including overhead), with each key's type. Use it to confirm suspected large keys; use redis_bigkeys to find them.",
            InputSchemaJson = Schema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "Table key, type, bytes plus structured rows."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void ValidateArguments(ConnectorInvocationContext context, RedisConnectionOptions options, ToolArguments arguments)
        {
            foreach (string k in KeyList(arguments, MaxKeys)) RedisScanner.ParseKey(k);
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, RedisSession session, ToolArguments arguments, CancellationToken token)
        {
            List<string> keys = KeyList(arguments, MaxKeys);
            int samples = arguments.GetInt("samples", 5, 0, 10000);
            List<Task<RedisResult>> usage = new List<Task<RedisResult>>();
            List<Task<RedisResult>> types = new List<Task<RedisResult>>();
            foreach (string k in keys)
            {
                RedisKey key = RedisScanner.ParseKey(k);
                usage.Add(ExecAsync(session, "MEMORY", new List<object> { "USAGE", key, "SAMPLES", samples }, token));
                types.Add(ExecAsync(session, "TYPE", new List<object> { key }, token));
            }
            await Task.WhenAll(usage.Concat(types)).ConfigureAwait(false);

            RedisRowsPayload rows = new RedisRowsPayload { Tool = Descriptor.Name, Columns = new List<string> { "key", "type", "bytes" } };
            long total = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                RedisResult r = usage[i].Result;
                long? bytes = r.IsNull ? null : (long)r;
                if (bytes.HasValue) total += bytes.Value;
                string type = types[i].Result.ToString() ?? "none";
                rows.Rows.Add(new List<string?> { keys[i], type == "none" ? "(missing)" : type, bytes.HasValue ? bytes.Value.ToString(CultureInfo.InvariantCulture) : null });
            }
            rows.Notes = new Dictionary<string, string> { { "totalBytes", total.ToString(CultureInfo.InvariantCulture) } };
            return RedisOutput.Table(String.Empty, rows, context.Limits);
        }

        #endregion
    }
}
