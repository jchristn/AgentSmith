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
    /// <c>redis_client_list</c>: connected clients (CLIENT LIST) with a summary by client name. Read. Thread-safe.
    /// </summary>
    public class RedisClientListTool : RedisToolBase
    {
        #region Public-Members

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string Schema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{"
            + "\"limit\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":10000,\"description\":\"Most clients listed, longest idle last. Default 100. Capped by the row limit. The summary always counts every client.\"},"
            + "\"node\":{\"type\":\"string\",\"description\":\"host:port of one node. Default: every primary.\"}"
            + "}}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RedisClientListTool() : base(new ToolDescriptor
        {
            Name = "redis_client_list",
            Title = "Redis clients",
            Description = "List connected clients (CLIENT LIST): address, name, age, idle time, database, last command, and flags, with counts by client name and by last command. "
                + "Use it for connection leaks, blocked clients, and who is running what.",
            InputSchemaJson = Schema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "Summary lines and a table node, id, addr, name, ageS, idleS, db, cmd, flags plus structured rows."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, RedisSession session, ToolArguments arguments, CancellationToken token)
        {
            int limit = arguments.GetInt("limit", 100, 1, Math.Min(10000, context.Limits.MaxRows));
            List<IServer> servers = session.SelectServers(arguments.GetString("node"));
            List<List<string?>> all = new List<List<string?>>();
            List<long> idles = new List<long>();
            Dictionary<string, int> byName = new Dictionary<string, int>(StringComparer.Ordinal);
            Dictionary<string, int> byCommand = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (IServer server in servers)
            {
                ClientInfo[] clients = await server.ClientListAsync(CommandFlags.None).WaitAsync(token).ConfigureAwait(false);
                string node = RedisSession.Format(server);
                foreach (ClientInfo c in clients)
                {
                    string name = String.IsNullOrEmpty(c.Name) ? "(unnamed)" : c.Name;
                    byName[name] = byName.TryGetValue(name, out int n) ? n + 1 : 1;
                    string cmd = String.IsNullOrEmpty(c.LastCommand) ? "(none)" : c.LastCommand;
                    byCommand[cmd] = byCommand.TryGetValue(cmd, out int m) ? m + 1 : 1;
                    all.Add(new List<string?>
                    {
                        node,
                        c.Id.ToString(CultureInfo.InvariantCulture),
                        c.Address?.ToString(),
                        c.Name,
                        c.AgeSeconds.ToString(CultureInfo.InvariantCulture),
                        c.IdleSeconds.ToString(CultureInfo.InvariantCulture),
                        c.Database.ToString(CultureInfo.InvariantCulture),
                        c.LastCommand,
                        c.FlagsRaw
                    });
                    idles.Add(c.IdleSeconds);
                }
            }

            List<int> order = Enumerable.Range(0, all.Count).OrderBy(i => idles[i]).ToList();
            RedisRowsPayload rows = new RedisRowsPayload
            {
                Tool = Descriptor.Name,
                Columns = new List<string> { "node", "id", "addr", "name", "ageS", "idleS", "db", "cmd", "flags" },
                Total = all.Count
            };
            foreach (int i in order.Take(limit)) rows.Rows.Add(all[i]);
            rows.Truncated = all.Count > rows.Rows.Count;
            rows.Notes = new Dictionary<string, string>
            {
                { "clientsByName", String.Join(", ", byName.OrderByDescending(p => p.Value).Take(20).Select(p => p.Key + "=" + p.Value)) },
                { "clientsByLastCommand", String.Join(", ", byCommand.OrderByDescending(p => p.Value).Take(20).Select(p => p.Key + "=" + p.Value)) }
            };
            string header = all.Count.ToString(CultureInfo.InvariantCulture) + " client(s) connected.\n";
            ToolResult result = RedisOutput.Table(header, rows, context.Limits);
            result.Metrics["clients"] = all.Count;
            return result;
        }

        #endregion
    }
}
