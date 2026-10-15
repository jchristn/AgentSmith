namespace Hugo.Connectors.Redis.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Redis.Classification;
    using StackExchange.Redis;

    /// <summary>
    /// <c>redis_command</c>: the escape hatch. Runs one Redis command, classified per call by
    /// <see cref="RedisCommandClassifier"/> (unknown commands are Destructive; blocking, subscription, and connection
    /// state commands are rejected). Thread-safe.
    /// </summary>
    public class RedisCommandTool : RedisToolBase
    {
        #region Public-Members

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string Schema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"command\"],\"properties\":{"
            + "\"command\":{\"type\":\"string\",\"description\":\"Command line in redis-cli syntax, for example HGET user:42 email or CONFIG GET maxmemory*. Quote arguments containing spaces.\"},"
            + "\"args\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"maxItems\":1000,\"description\":\"Extra arguments appended verbatim after the command line (no quoting needed), for values with spaces or quotes.\"},"
            + "\"node\":{\"type\":\"string\",\"description\":\"host:port of the node to run on (cluster or replica diagnostics). Default: routed by key, or the primary.\"}"
            + "}}";

        /// <summary>
        /// Most arguments shown in an approval summary. Minimum 1, maximum 32, default 4.
        /// </summary>
        public int SummaryArguments
        {
            get
            {
                return _SummaryArguments;
            }
            set
            {
                _SummaryArguments = Math.Clamp(value, 1, 32);
            }
        }

        #endregion

        #region Private-Members

        private int _SummaryArguments = 4;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RedisCommandTool() : base(new ToolDescriptor
        {
            Name = "redis_command",
            Title = "Run a Redis command",
            Description = "Run one Redis command when no purpose-built redis_* tool fits. Each call is classified from a table of every Redis command: reads (GET, HGETALL, XINFO, CONFIG GET) are Read, "
                + "writes (SET, HSET, EXPIRE) are Write, and deletes, flushes, CONFIG SET, SHUTDOWN, scripts (EVAL), and unknown commands are Destructive and need approval. "
                + "Key deletes (DEL, UNLINK, GETDEL) and flushes always need a human approval, whatever the access mode. "
                + "KEYS is denied unless the resource allows it (use redis_scan); blocking commands (BLPOP, XREAD BLOCK), SUBSCRIBE, MONITOR, MULTI/EXEC, SELECT, and AUTH are rejected. Replies are capped by the row limit and maxValueBytes.",
            InputSchemaJson = Schema,
            BaseEffect = EffectEnum.Read,
            ClassifiedPerCall = true,
            OutputDescription = "The reply in redis-cli form plus structured JSON {command, reply}."
        })
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override EffectClassification Classify(ConnectorInvocationContext context, ToolArguments arguments)
        {
            ArgumentNullException.ThrowIfNull(arguments);
            List<string> tokens;
            try
            {
                tokens = Tokens(arguments);
            }
            catch (ConnectorException e)
            {
                return EffectClassification.Reject(e.Message);
            }

            RedisClassifierOptions options = context == null
                ? new RedisClassifierOptions()
                : RedisClassifierOptions.From(RedisConnectionOptions.FromSettings(context.Resource.Settings));
            EffectClassification c = RedisCommandClassifier.Classify(tokens, options);
            c.ApprovalSummary = Summary(tokens, context);
            return c;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void ValidateArguments(ConnectorInvocationContext context, RedisConnectionOptions options, ToolArguments arguments)
        {
            List<string> tokens = Tokens(arguments);
            EffectClassification c = RedisCommandClassifier.Classify(tokens, RedisClassifierOptions.From(options));
            if (c.Rejected) throw new ConnectorException(ConnectorErrorCodes.PermissionDenied, "Rejected: " + c.Reason + ".");
            RequireWritable(context, c.Effect);
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, RedisSession session, ToolArguments arguments, CancellationToken token)
        {
            List<string> tokens = Tokens(arguments);
            string name = tokens[0];
            List<object> args = tokens.Skip(1).Cast<object>().ToList();
            string label = RedisCommandClassifier.Label(tokens);
            string? node = arguments.GetString("node");

            Task<RedisResult> call;
            if (!String.IsNullOrWhiteSpace(node))
            {
                IServer server = session.SelectServers(node)[0];
                call = server.ExecuteAsync(session.DatabaseIndex(), name, args, CommandFlags.None);
            }
            else if (session.IsCluster && args.Any())
            {
                args = await RouteByKeyAsync(session, tokens, token).ConfigureAwait(false);
                call = session.Database().ExecuteAsync(name, args, CommandFlags.None);
            }
            else
            {
                call = session.Database().ExecuteAsync(name, args, CommandFlags.None);
            }

            RedisResult reply = await call.WaitAsync(token).ConfigureAwait(false);
            RedisResultRenderer renderer = new RedisResultRenderer { MaxElements = context.Limits.MaxRows, MaxValueBytes = session.Options.MaxValueBytes };
            string text = renderer.RenderText(reply);
            string json = renderer.RenderJson(reply);
            if (renderer.Truncated) text += "[reply truncated: showing " + renderer.Elements + " element(s); narrow the command, for example with COUNT or a range]\n";

            ToolResult result = new ToolResult();
            result.Text = OutputLimiter.Head(text, context.Limits.MaxBytes, context.Limits.MaxLines, result);
            result.Truncated = result.Truncated || renderer.Truncated;
            result.StructuredJson = "{\"command\":" + System.Text.Json.JsonSerializer.Serialize(label) + ",\"reply\":" + json + "}";
            result.Metrics["elements"] = renderer.Elements;
            return result;
        }

        #endregion

        #region Private-Methods

        private static List<string> Tokens(ToolArguments arguments)
        {
            List<string> tokens = RedisCommandTokenizer.Tokenize(arguments.GetString("command"));
            tokens.AddRange(arguments.GetStringList("args"));
            if (!tokens.Any() || String.IsNullOrWhiteSpace(tokens[0]))
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Argument 'command' is required, for example HGETALL user:42.");
            return tokens;
        }

        private static async Task<List<object>> RouteByKeyAsync(RedisSession session, List<string> tokens, CancellationToken token)
        {
            List<object> args = tokens.Skip(1).Cast<object>().ToList();
            RedisKey[] keys;
            try
            {
                IServer any = session.Primaries()[0];
                keys = await any.CommandGetKeysAsync(tokens.Select(t => (RedisValue)t).ToArray(), CommandFlags.None).WaitAsync(token).ConfigureAwait(false);
            }
            catch (RedisServerException)
            {
                return args;
            }

            if (keys == null || keys.Length == 0) return args;
            HashSet<string> keySet = new HashSet<string>(keys.Select(k => (string?)k ?? String.Empty), StringComparer.Ordinal);
            for (int i = 0; i < args.Count; i++)
            {
                string value = (string)args[i];
                if (keySet.Contains(value)) args[i] = (RedisKey)value;
            }
            return args;
        }

        private string Summary(List<string> tokens, ConnectorInvocationContext? context)
        {
            StringBuilder sb = new StringBuilder("Run Redis \"");
            string label = RedisCommandClassifier.Label(tokens);
            sb.Append(label);
            int skip = label.Contains(' ', StringComparison.Ordinal) ? 2 : 1;
            foreach (string arg in tokens.Skip(skip).Take(SummaryArguments))
            {
                string a = arg.Replace("\n", " ", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal);
                if (a.Length > 40) a = a.Substring(0, 37) + "...";
                sb.Append(' ').Append(a.Contains(' ', StringComparison.Ordinal) ? "'" + a + "'" : a);
            }
            int remaining = tokens.Count - skip - SummaryArguments;
            if (remaining > 0) sb.Append(" (+").Append(remaining).Append(" more args)");
            sb.Append("\" on ").Append(Target(context));
            return sb.ToString();
        }

        #endregion
    }
}
