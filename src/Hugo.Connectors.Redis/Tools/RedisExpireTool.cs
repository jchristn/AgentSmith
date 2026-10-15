namespace Hugo.Connectors.Redis.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>redis_expire</c>: set or remove a key's TTL (EXPIRE or PERSIST). Write. Thread-safe.
    /// </summary>
    public class RedisExpireTool : RedisToolBase
    {
        #region Public-Members

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string Schema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"key\"],\"properties\":{"
            + "\"key\":{\"type\":\"string\",\"description\":\"Exact key name (prefix base64: for a binary name).\"},"
            + "\"ttlSeconds\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":315360000,\"description\":\"New TTL in seconds. The key is deleted by Redis when it elapses.\"},"
            + "\"persist\":{\"type\":\"boolean\",\"description\":\"Remove the TTL so the key never expires (PERSIST). Use instead of ttlSeconds.\"}"
            + "}}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RedisExpireTool() : base(new ToolDescriptor
        {
            Name = "redis_expire",
            Title = "Set a Redis key TTL",
            Description = "Set a key's time to live in seconds (EXPIRE), or remove it with persist=true (PERSIST). Redis deletes the key when the TTL elapses, so a short TTL is effectively a delayed delete. Write.",
            InputSchemaJson = Schema,
            BaseEffect = EffectEnum.Write,
            OutputDescription = "Whether the TTL changed, plus structured rows."
        })
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override EffectClassification Classify(ConnectorInvocationContext context, ToolArguments arguments)
        {
            ArgumentNullException.ThrowIfNull(arguments);
            string key = arguments.GetString("key") ?? String.Empty;
            string what = arguments.GetBool("persist", false) ? "Remove the TTL of Redis key " + Quote(key) : "Expire Redis key " + Quote(key) + " in " + arguments.GetLong("ttlSeconds", 0) + "s";
            return new EffectClassification(EffectEnum.Write, "redis_expire changes a key's TTL", what + " on " + Target(context));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void ValidateArguments(ConnectorInvocationContext context, RedisConnectionOptions options, ToolArguments arguments)
        {
            RequireWritable(context, EffectEnum.Write);
            RedisScanner.ParseKey(RequiredKey(arguments));
            bool persist = arguments.GetBool("persist", false);
            if (persist == arguments.Has("ttlSeconds"))
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Give exactly one of ttlSeconds or persist=true.");
            if (!persist && arguments.GetLong("ttlSeconds", 0) < 1)
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "ttlSeconds must be at least 1.");
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, RedisSession session, ToolArguments arguments, CancellationToken token)
        {
            string keyText = RequiredKey(arguments);
            bool persist = arguments.GetBool("persist", false);
            long ttl = arguments.GetLong("ttlSeconds", 1, 1, 315360000);
            List<object> args = new List<object> { RedisScanner.ParseKey(keyText) };
            if (!persist) args.Add(ttl);
            long changed = (long)await ExecAsync(session, persist ? "PERSIST" : "EXPIRE", args, token).ConfigureAwait(false);

            if (changed == 0)
            {
                long exists = (long)await ExecAsync(session, "EXISTS", new List<object> { RedisScanner.ParseKey(keyText) }, token).ConfigureAwait(false);
                if (exists == 0) return ToolResult.Error(ConnectorErrorCodes.NotFound, "Key " + Quote(keyText) + " does not exist.");
            }

            RedisRowsPayload payload = new RedisRowsPayload
            {
                Tool = Descriptor.Name,
                Columns = new List<string> { "key", "changed", "ttlSeconds" },
                Rows = new List<List<string?>> { new List<string?> { keyText, changed == 1 ? "true" : "false", persist ? null : ttl.ToString(CultureInfo.InvariantCulture) } }
            };
            string text = persist
                ? (changed == 1 ? "Removed the TTL of " + Quote(keyText) + "; it no longer expires.\n" : Quote(keyText) + " already had no TTL.\n")
                : "Set the TTL of " + Quote(keyText) + " to " + ttl.ToString(CultureInfo.InvariantCulture) + "s.\n";
            return RedisOutput.Text(text, payload, context.Limits);
        }

        #endregion
    }
}
