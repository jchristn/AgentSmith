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
    /// <c>redis_delete</c>: delete up to 100 named keys (UNLINK, so large values are freed in the background).
    /// Destructive. Thread-safe.
    /// </summary>
    public class RedisDeleteTool : RedisToolBase
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
            + "\"keys\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"minItems\":1,\"maxItems\":100,\"description\":\"Exact key names to delete (up to 100). Patterns are not expanded.\"}"
            + "}}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RedisDeleteTool() : base(new ToolDescriptor
        {
            Name = "redis_delete",
            Title = "Delete Redis keys",
            Description = "Delete up to 100 keys by exact name (UNLINK). Glob patterns are not expanded: find keys with redis_scan first and list them explicitly. "
                + "Destructive: it requires approval, and deleted data cannot be recovered.",
            InputSchemaJson = Schema,
            BaseEffect = EffectEnum.Destructive,
            OutputDescription = "Count deleted and keys that did not exist, plus structured rows."
        })
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override EffectClassification Classify(ConnectorInvocationContext context, ToolArguments arguments)
        {
            ArgumentNullException.ThrowIfNull(arguments);
            List<string> keys = arguments.GetStringList("keys");
            string shown = String.Join(", ", keys.Take(5).Select(Quote));
            if (keys.Count > 5) shown += " and " + (keys.Count - 5) + " more";
            return new EffectClassification(EffectEnum.Destructive, "redis_delete removes keys", "Delete " + keys.Count + " Redis key(s) " + shown + " on " + Target(context));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void ValidateArguments(ConnectorInvocationContext context, RedisConnectionOptions options, ToolArguments arguments)
        {
            RequireWritable(context, EffectEnum.Destructive);
            foreach (string k in KeyList(arguments, MaxKeys)) RedisScanner.ParseKey(k);
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, RedisSession session, ToolArguments arguments, CancellationToken token)
        {
            List<string> keys = KeyList(arguments, MaxKeys);
            List<Task<RedisResult>> calls = keys.Select(k => ExecAsync(session, "UNLINK", new List<object> { RedisScanner.ParseKey(k) }, token)).ToList();
            await Task.WhenAll(calls).ConfigureAwait(false);

            RedisRowsPayload rows = new RedisRowsPayload { Tool = Descriptor.Name, Columns = new List<string> { "key", "deleted" } };
            int deleted = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                bool gone = (long)calls[i].Result > 0;
                if (gone) deleted++;
                rows.Rows.Add(new List<string?> { keys[i], gone ? "true" : "false (did not exist)" });
            }
            ToolResult result = RedisOutput.Table("Deleted " + deleted.ToString(CultureInfo.InvariantCulture) + " of " + keys.Count.ToString(CultureInfo.InvariantCulture) + " key(s).\n", rows, context.Limits);
            result.Metrics["deleted"] = deleted;
            return result;
        }

        #endregion
    }
}
