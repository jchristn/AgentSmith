namespace Hugo.Connectors.Redis.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using StackExchange.Redis;

    /// <summary>
    /// <c>redis_info</c>: INFO sections from each primary (or one node). Read. Thread-safe.
    /// </summary>
    public class RedisInfoTool : RedisToolBase
    {
        #region Public-Members

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string Schema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{"
            + "\"sections\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"maxItems\":16,\"description\":\"INFO sections such as server, clients, memory, persistence, stats, replication, cpu, commandstats, latencystats, errorstats, keyspace, cluster, modules, or all. Default: the default set. Ask only for what you need; commandstats is long.\"},"
            + "\"node\":{\"type\":\"string\",\"description\":\"host:port of one node (see redis_info server section or a cluster's node list). Default: every primary.\"}"
            + "}}";

        #endregion

        #region Private-Members

        private static readonly Regex _Section = new Regex("^[a-z]{2,32}$", RegexOptions.CultureInvariant);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RedisInfoTool() : base(new ToolDescriptor
        {
            Name = "redis_info",
            Title = "Redis server info",
            Description = "Return INFO from the Redis server (each primary of a cluster): version, memory use and fragmentation, connected and blocked clients, ops/sec, hit ratio, evictions, persistence, replication, and keyspace sizes. "
                + "Start an investigation here. Pass sections (for example [\"memory\",\"clients\",\"stats\"]) to keep the output short.",
            InputSchemaJson = Schema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "INFO text per node plus structured JSON {nodes:[{node, sections:{name:{field:value}}}]}."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void ValidateArguments(ConnectorInvocationContext context, RedisConnectionOptions options, ToolArguments arguments)
        {
            foreach (string s in arguments.GetStringList("sections"))
            {
                if (!_Section.IsMatch(s.Trim().ToLowerInvariant()))
                    throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Section '" + s + "' is not a valid INFO section name.");
            }
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, RedisSession session, ToolArguments arguments, CancellationToken token)
        {
            List<string> sections = arguments.GetStringList("sections").Select(s => s.Trim().ToLowerInvariant()).Distinct().ToList();
            List<IServer> servers = session.SelectServers(arguments.GetString("node"));
            StringBuilder sb = new StringBuilder();
            RedisInfoPayload payload = new RedisInfoPayload();

            foreach (IServer server in servers)
            {
                token.ThrowIfCancellationRequested();
                string node = RedisSession.Format(server);
                StringBuilder raw = new StringBuilder();
                if (!sections.Any())
                {
                    raw.Append(await server.InfoRawAsync(default(RedisValue), CommandFlags.None).WaitAsync(token).ConfigureAwait(false));
                }
                else
                {
                    foreach (string section in sections)
                    {
                        raw.Append(await server.InfoRawAsync(section, CommandFlags.None).WaitAsync(token).ConfigureAwait(false));
                    }
                }

                RedisInfoNode info = new RedisInfoNode { Node = node, Sections = Parse(raw.ToString()) };
                payload.Nodes.Add(info);
                if (servers.Count > 1 || session.IsCluster) sb.Append("## node ").Append(node).Append('\n');
                sb.Append(raw.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).Trim()).Append("\n\n");
            }

            ToolResult result = RedisOutput.Text(sb.ToString().TrimEnd() + "\n", payload, context.Limits);
            result.Metrics["nodes"] = servers.Count;
            return result;
        }

        #endregion

        #region Private-Methods

        private static Dictionary<string, Dictionary<string, string>> Parse(string raw)
        {
            Dictionary<string, Dictionary<string, string>> sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            Dictionary<string, string> current = new Dictionary<string, string>(StringComparer.Ordinal);
            sections["default"] = current;
            foreach (string line in raw.Split('\n'))
            {
                string l = line.TrimEnd('\r');
                if (l.Length == 0) continue;
                if (l.StartsWith('#'))
                {
                    string name = l.TrimStart('#').Trim().ToLowerInvariant();
                    if (!sections.TryGetValue(name, out Dictionary<string, string>? existing))
                    {
                        existing = new Dictionary<string, string>(StringComparer.Ordinal);
                        sections[name] = existing;
                    }
                    current = existing;
                    continue;
                }
                int colon = l.IndexOf(':');
                if (colon <= 0) continue;
                current[l.Substring(0, colon)] = l.Substring(colon + 1);
            }
            if (!sections["default"].Any()) sections.Remove("default");
            return sections;
        }

        #endregion
    }
}
