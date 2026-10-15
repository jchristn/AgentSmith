namespace Hugo.Connectors.Redis.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using StackExchange.Redis;

    /// <summary>
    /// <c>redis_set</c>: set a string key, optionally only if it does or does not exist, with an optional TTL. Write.
    /// Thread-safe.
    /// </summary>
    public class RedisSetTool : RedisToolBase
    {
        #region Public-Members

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string Schema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"key\",\"value\"],\"properties\":{"
            + "\"key\":{\"type\":\"string\",\"description\":\"Exact key name (prefix base64: for a binary name).\"},"
            + "\"value\":{\"type\":\"string\",\"description\":\"String value. At most the resource's maxValueBytes (UTF-8).\"},"
            + "\"ttlSeconds\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":315360000,\"description\":\"Expire the key after this many seconds. Default: no expiry (an existing TTL is cleared unless keepTtl is true).\"},"
            + "\"keepTtl\":{\"type\":\"boolean\",\"description\":\"Keep the key's existing TTL (SET KEEPTTL). Cannot be combined with ttlSeconds. Default false.\"},"
            + "\"condition\":{\"type\":\"string\",\"enum\":[\"always\",\"ifNotExists\",\"ifExists\"],\"description\":\"always (default) overwrites; ifNotExists sets only a new key (NX); ifExists only replaces an existing key (XX).\"}"
            + "}}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RedisSetTool() : base(new ToolDescriptor
        {
            Name = "redis_set",
            Title = "Set a Redis string",
            Description = "Set a string key to a value (SET), optionally only when the key is new or only when it exists, and optionally with a TTL. "
                + "It overwrites any existing value of any type when condition is always, so read the key first and state the exact key before calling. Write.",
            InputSchemaJson = Schema,
            BaseEffect = EffectEnum.Write,
            OutputDescription = "Whether the key was set, plus structured JSON {key, set, bytes, ttlSeconds, condition}."
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
            int bytes = Encoding.UTF8.GetByteCount(arguments.GetString("value") ?? String.Empty);
            long ttl = arguments.GetLong("ttlSeconds", 0);
            string condition = arguments.GetString("condition") ?? "always";
            StringBuilder summary = new StringBuilder("Set Redis key ").Append(Quote(key)).Append(" (").Append(bytes).Append(" bytes");
            if (ttl > 0) summary.Append(", expires in ").Append(ttl).Append("s");
            if (condition != "always") summary.Append(", ").Append(condition);
            summary.Append(") on ").Append(Target(context));
            return new EffectClassification(EffectEnum.Write, "redis_set writes a string value (SET)", summary.ToString());
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void ValidateArguments(ConnectorInvocationContext context, RedisConnectionOptions options, ToolArguments arguments)
        {
            RequireWritable(context, EffectEnum.Write);
            RedisScanner.ParseKey(RequiredKey(arguments));
            if (!arguments.Has("value")) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Argument 'value' is required.");
            int bytes = Encoding.UTF8.GetByteCount(arguments.GetString("value") ?? String.Empty);
            if (bytes > options.MaxValueBytes)
                throw new ConnectorException(ConnectorErrorCodes.TooLarge, "The value is " + bytes + " bytes; this resource allows at most " + options.MaxValueBytes + " (maxValueBytes).");
            if (arguments.Has("ttlSeconds") && arguments.GetBool("keepTtl", false))
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Use either ttlSeconds or keepTtl, not both.");
            string condition = arguments.GetString("condition") ?? "always";
            if (condition != "always" && condition != "ifNotExists" && condition != "ifExists")
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "condition must be always, ifNotExists, or ifExists.");
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, RedisSession session, ToolArguments arguments, CancellationToken token)
        {
            string keyText = RequiredKey(arguments);
            string value = arguments.GetString("value") ?? String.Empty;
            long ttl = arguments.GetLong("ttlSeconds", 0, 0, 315360000);
            string condition = arguments.GetString("condition") ?? "always";

            List<object> args = new List<object> { RedisScanner.ParseKey(keyText), value };
            if (condition == "ifNotExists") args.Add("NX");
            else if (condition == "ifExists") args.Add("XX");
            if (ttl > 0)
            {
                args.Add("EX");
                args.Add(ttl);
            }
            else if (arguments.GetBool("keepTtl", false))
            {
                args.Add("KEEPTTL");
            }

            RedisResult reply = await ExecAsync(session, "SET", args, token).ConfigureAwait(false);
            bool set = !reply.IsNull;
            RedisSetPayload payload = new RedisSetPayload
            {
                Key = keyText,
                Set = set,
                Bytes = Encoding.UTF8.GetByteCount(value),
                TtlSeconds = ttl > 0 ? ttl : null,
                Condition = condition
            };
            string text = set
                ? "Set " + Quote(keyText) + " (" + payload.Bytes.ToString(CultureInfo.InvariantCulture) + " bytes" + (ttl > 0 ? ", expires in " + ttl.ToString(CultureInfo.InvariantCulture) + "s" : String.Empty) + ").\n"
                : "Not set: the condition " + condition + " was not met for " + Quote(keyText) + ".\n";
            return RedisOutput.Text(text, payload, context.Limits);
        }

        #endregion
    }
}
