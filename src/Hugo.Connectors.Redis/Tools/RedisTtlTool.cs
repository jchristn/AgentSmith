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
    /// <c>redis_ttl</c>: remaining time to live of up to 100 keys. Read. Thread-safe.
    /// </summary>
    public class RedisTtlTool : RedisToolBase
    {
        #region Public-Members

        /// <summary>
        /// Most keys per call.
        /// </summary>
        public static readonly int MaxKeys = 100;

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string Schema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"keys\"],\"properties\":{"
            + "\"keys\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"minItems\":1,\"maxItems\":100,\"description\":\"Key names (up to 100).\"}"
            + "}}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RedisTtlTool() : base(new ToolDescriptor
        {
            Name = "redis_ttl",
            Title = "Redis key TTLs",
            Description = "Report the remaining time to live of up to 100 keys, distinguishing keys with no expiry from keys that do not exist, with the absolute expiry time in UTC.",
            InputSchemaJson = Schema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "Table key, ttl, expiresAtUtc plus structured rows."
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
            List<Task<RedisResult>> calls = keys.Select(k => ExecAsync(session, "PTTL", new List<object> { RedisScanner.ParseKey(k) }, token)).ToList();
            await Task.WhenAll(calls).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;

            RedisRowsPayload rows = new RedisRowsPayload { Tool = Descriptor.Name, Columns = new List<string> { "key", "ttl", "ttlMs", "expiresAtUtc" } };
            for (int i = 0; i < keys.Count; i++)
            {
                long ms = (long)calls[i].Result;
                string? expires = ms >= 0 ? now.AddMilliseconds(ms).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture) : null;
                rows.Rows.Add(new List<string?> { keys[i], RedisOutput.Ttl(ms), ms.ToString(CultureInfo.InvariantCulture), expires });
            }
            return RedisOutput.Table(String.Empty, rows, context.Limits);
        }

        #endregion
    }
}
