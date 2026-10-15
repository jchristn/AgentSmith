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
    /// <c>redis_slowlog</c>: recent slow commands from SLOWLOG GET on each primary (or one node). Read. Thread-safe.
    /// </summary>
    public class RedisSlowlogTool : RedisToolBase
    {
        #region Public-Members

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string Schema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{"
            + "\"count\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":128,\"description\":\"Entries per node, newest first. Default 25.\"},"
            + "\"node\":{\"type\":\"string\",\"description\":\"host:port of one node. Default: every primary.\"}"
            + "}}";

        /// <summary>
        /// Most arguments of each slow command shown. Minimum 1, maximum 64, default 8.
        /// </summary>
        public int MaxArguments
        {
            get
            {
                return _MaxArguments;
            }
            set
            {
                _MaxArguments = Math.Clamp(value, 1, 64);
            }
        }

        /// <summary>
        /// Most characters of each argument shown. Minimum 8, maximum 4096, default 64.
        /// </summary>
        public int MaxArgumentChars
        {
            get
            {
                return _MaxArgumentChars;
            }
            set
            {
                _MaxArgumentChars = Math.Clamp(value, 8, 4096);
            }
        }

        #endregion

        #region Private-Members

        private int _MaxArguments = 8;
        private int _MaxArgumentChars = 64;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RedisSlowlogTool() : base(new ToolDescriptor
        {
            Name = "redis_slowlog",
            Title = "Redis slow log",
            Description = "List the most recent commands that exceeded the server's slowlog-log-slower-than threshold: when, how long (microseconds), and the command with its first arguments. "
                + "Use it for latency complaints together with redis_latency and the stats section of redis_info.",
            InputSchemaJson = Schema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "Table node, id, timeUtc, durationUs, command plus structured rows."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, RedisSession session, ToolArguments arguments, CancellationToken token)
        {
            int count = arguments.GetInt("count", 25, 1, 128);
            List<IServer> servers = session.SelectServers(arguments.GetString("node"));
            RedisRowsPayload rows = new RedisRowsPayload { Tool = Descriptor.Name, Columns = new List<string> { "node", "id", "timeUtc", "durationUs", "command" } };
            foreach (IServer server in servers)
            {
                CommandTrace[] traces = await server.SlowlogGetAsync(count, CommandFlags.None).WaitAsync(token).ConfigureAwait(false);
                string node = RedisSession.Format(server);
                foreach (CommandTrace t in traces)
                {
                    rows.Rows.Add(new List<string?>
                    {
                        node,
                        t.UniqueId.ToString(CultureInfo.InvariantCulture),
                        t.Time.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                        ((long)(t.Duration.TotalMilliseconds * 1000)).ToString(CultureInfo.InvariantCulture),
                        Command(t.Arguments)
                    });
                }
            }
            string header = rows.Rows.Any() ? String.Empty : "The slow log is empty on " + String.Join(", ", servers.Select(RedisSession.Format)) + ".\n";
            return RedisOutput.Table(header, rows, context.Limits);
        }

        #endregion

        #region Private-Methods

        private string Command(RedisValue[] args)
        {
            List<string> parts = new List<string>();
            foreach (RedisValue a in args.Take(MaxArguments))
            {
                RedisDisplayValue v = RedisDisplayValue.From(a, MaxArgumentChars);
                string text = v.Binary ? "<binary>" : (v.Text ?? String.Empty);
                if (v.Truncated) text += "...";
                parts.Add(text.Contains(' ', StringComparison.Ordinal) ? "\"" + text + "\"" : text);
            }
            if (args.Length > MaxArguments) parts.Add("(+" + (args.Length - MaxArguments) + " more)");
            return String.Join(" ", parts);
        }

        #endregion
    }
}
