namespace Hugo.Connectors.Redis.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using StackExchange.Redis;

    /// <summary>
    /// <c>redis_latency</c>: the latency monitor (LATENCY LATEST, HISTORY, DOCTOR) and its threshold. Read. Thread-safe.
    /// </summary>
    public class RedisLatencyTool : RedisToolBase
    {
        #region Public-Members

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string Schema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{"
            + "\"event\":{\"type\":\"string\",\"description\":\"Latency event name (for example command, fast-command, expire-cycle, eviction-cycle, fork, aof-fsync-always) to get its history instead of the latest spikes.\"},"
            + "\"doctor\":{\"type\":\"boolean\",\"description\":\"Also include the server's LATENCY DOCTOR analysis. Default false.\"},"
            + "\"node\":{\"type\":\"string\",\"description\":\"host:port of one node. Default: every primary.\"}"
            + "}}";

        #endregion

        #region Private-Members

        private static readonly Regex _Event = new Regex("^[a-z][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RedisLatencyTool() : base(new ToolDescriptor
        {
            Name = "redis_latency",
            Title = "Redis latency monitor",
            Description = "Report latency spikes recorded by the server's latency monitor: the latest spike per event (with its all-time maximum), or one event's history, and optionally LATENCY DOCTOR's advice. "
                + "Reports the latency-monitor-threshold; when it is 0 the monitor is off and nothing is recorded.",
            InputSchemaJson = Schema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "Table of events or history samples plus notes (threshold, doctor text) and structured rows."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void ValidateArguments(ConnectorInvocationContext context, RedisConnectionOptions options, ToolArguments arguments)
        {
            string? ev = arguments.GetString("event");
            if (!String.IsNullOrWhiteSpace(ev) && !_Event.IsMatch(ev.Trim()))
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "event must be a latency event name such as command or fork.");
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, RedisSession session, ToolArguments arguments, CancellationToken token)
        {
            string? ev = arguments.GetString("event");
            bool doctor = arguments.GetBool("doctor", false);
            List<IServer> servers = session.SelectServers(arguments.GetString("node"));
            RedisRowsPayload rows = new RedisRowsPayload { Tool = Descriptor.Name, Notes = new Dictionary<string, string>(StringComparer.Ordinal) };
            StringBuilder doctorText = new StringBuilder();

            if (String.IsNullOrWhiteSpace(ev)) rows.Columns = new List<string> { "node", "event", "latestUtc", "latestMs", "maxMs" };
            else rows.Columns = new List<string> { "node", "timeUtc", "ms" };

            foreach (IServer server in servers)
            {
                string node = RedisSession.Format(server);
                KeyValuePair<string, string>[] threshold = await server.ConfigGetAsync("latency-monitor-threshold", CommandFlags.None).WaitAsync(token).ConfigureAwait(false);
                rows.Notes["latency-monitor-threshold " + node] = threshold.Any() ? threshold[0].Value + " ms" + (threshold[0].Value == "0" ? " (monitor off)" : String.Empty) : "unknown";

                if (String.IsNullOrWhiteSpace(ev))
                {
                    LatencyLatestEntry[] latest = await server.LatencyLatestAsync(CommandFlags.None).WaitAsync(token).ConfigureAwait(false);
                    foreach (LatencyLatestEntry e in latest)
                    {
                        rows.Rows.Add(new List<string?>
                        {
                            node,
                            e.EventName,
                            e.Timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                            e.DurationMilliseconds.ToString(CultureInfo.InvariantCulture),
                            e.MaxDurationMilliseconds.ToString(CultureInfo.InvariantCulture)
                        });
                    }
                }
                else
                {
                    LatencyHistoryEntry[] history = await server.LatencyHistoryAsync(ev.Trim(), CommandFlags.None).WaitAsync(token).ConfigureAwait(false);
                    foreach (LatencyHistoryEntry e in history)
                    {
                        rows.Rows.Add(new List<string?>
                        {
                            node,
                            e.Timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                            e.DurationMilliseconds.ToString(CultureInfo.InvariantCulture)
                        });
                    }
                }

                if (doctor)
                {
                    string advice = await server.LatencyDoctorAsync(CommandFlags.None).WaitAsync(token).ConfigureAwait(false);
                    doctorText.Append("## LATENCY DOCTOR ").Append(node).Append('\n').Append(advice.Trim()).Append('\n');
                }
            }

            string header = rows.Rows.Any() ? String.Empty : (String.IsNullOrWhiteSpace(ev) ? "No latency spikes are recorded.\n" : "No history for event " + ev.Trim() + ".\n");
            ToolResult result = RedisOutput.Table(header, rows, context.Limits);
            if (doctorText.Length > 0)
            {
                result.Text = OutputLimiter.Head(result.Text + doctorText.ToString(), context.Limits.MaxBytes, context.Limits.MaxLines, result);
            }
            return result;
        }

        #endregion
    }
}
