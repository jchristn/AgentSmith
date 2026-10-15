namespace Hugo.Connectors.Docker.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using global::Docker.DotNet.Models;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>docker_events</c>: daemon events in a bounded past time window (never a live stream). Always Read.
    /// Thread-safe.
    /// </summary>
    public class DockerEventsTool : DockerToolBase
    {
        #region Private-Members

        private const string _Schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""since"": { ""type"": ""string"", ""maxLength"": 64, ""description"": ""Start of the window: seconds ago (900), a duration (15m, 2h), or an ISO 8601 time. Default 15m. The window is capped by the resource's maxEventsWindowSeconds."" },
    ""until"": { ""type"": ""string"", ""maxLength"": 64, ""description"": ""End of the window in the same formats. Default now. Never in the future."" },
    ""type"": { ""type"": ""string"", ""enum"": [""container"", ""image"", ""volume"", ""network"", ""daemon"", ""plugin"", ""service"", ""node"", ""secret"", ""config""], ""description"": ""Only events about this kind of object."" },
    ""container"": { ""type"": ""string"", ""maxLength"": 256, ""description"": ""Only events about this container name or ID."" },
    ""event"": { ""type"": ""string"", ""maxLength"": 64, ""description"": ""Only this action, for example die, oom, kill, restart, start, stop, health_status."" },
    ""limit"": { ""type"": ""integer"", ""minimum"": 1, ""maximum"": 5000, ""description"": ""Most events to return. Default 200."" }
  },
  ""additionalProperties"": false
}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DockerEventsTool() : base(new ToolDescriptor
        {
            Name = "docker_events",
            Title = "List daemon events",
            Description = "List Docker daemon events in a past time window: container starts, stops, deaths with exit codes, OOM kills, restarts, health changes, image pulls. "
                + "Use type: container and event: die or oom to find crashes. The window defaults to the last 15 minutes and is always bounded.",
            InputSchemaJson = _Schema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "A table of events, oldest first."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override EffectClassification ClassifyCall(ConnectorInvocationContext? context, ToolArguments arguments, DockerOptions options)
        {
            return Read("reading events is read-only", "List Docker events on " + TargetName(context));
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, DockerOptions options, CancellationToken token)
        {
            DateTime now = DateTime.UtcNow;
            DateTime until = DockerFormat.ParseTime(Optional(arguments, "until"), now, "until") ?? now;
            if (until > now) until = now;
            DateTime since = DockerFormat.ParseTime(Optional(arguments, "since"), now, "since") ?? now.AddMinutes(-15);
            if (since >= until) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "since must be before until.");
            bool clipped = false;
            if ((until - since).TotalSeconds > options.MaxEventsWindowSeconds)
            {
                since = until.AddSeconds(-options.MaxEventsWindowSeconds);
                clipped = true;
            }
            int limit = Math.Min(arguments.GetInt("limit", 200, 1, 5000), context.Limits.MaxRows);

            Dictionary<string, IDictionary<string, bool>> filters = new Dictionary<string, IDictionary<string, bool>>(StringComparer.Ordinal);
            string? type = Optional(arguments, "type");
            string? container = Optional(arguments, "container") == null ? null : Container(arguments);
            string? action = Optional(arguments, "event");
            if (type != null) filters["type"] = new Dictionary<string, bool> { { type, true } };
            if (container != null) filters["container"] = new Dictionary<string, bool> { { container, true } };
            if (action != null) filters["event"] = new Dictionary<string, bool> { { action, true } };

            DockerSession session = await GetSessionAsync(context, options, token).ConfigureAwait(false);
            DockerEventCollector collector;
            using (CancellationTokenSource full = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                collector = new DockerEventCollector(limit, full);
                ContainerEventsParameters parameters = new ContainerEventsParameters { Since = DockerFormat.UnixSeconds(since), Until = DockerFormat.UnixSeconds(until), Filters = filters };
                try
                {
                    await session.Client.System.MonitorEventsAsync(parameters, collector, full.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested && collector.Overflowed)
                {
                }
            }

            List<Message> events = collector.Events();
            List<IList<string?>> rows = new List<IList<string?>>();
            foreach (Message m in events)
            {
                DateTime at = m.TimeNano.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(m.TimeNano.Value / 1000000).UtcDateTime : (m.Time.HasValue ? DateTimeOffset.FromUnixTimeSeconds(m.Time.Value).UtcDateTime : now);
                string name = String.Empty;
                if (m.Actor?.Attributes != null && m.Actor.Attributes.TryGetValue("name", out string? n)) name = n;
                string detail = String.Empty;
                if (m.Actor?.Attributes != null)
                {
                    List<string> parts = new List<string>();
                    foreach (string key in new[] { "exitCode", "image", "signal", "health_status" })
                        if (m.Actor.Attributes.TryGetValue(key, out string? v) && !String.IsNullOrEmpty(v)) parts.Add(key + "=" + v);
                    detail = String.Join(" ", parts);
                }
                rows.Add(new List<string?> { at.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture), m.Type, m.Action, name.Length > 0 ? name : DockerFormat.ShortId(m.Actor?.ID), detail });
            }

            StringBuilder sb = new StringBuilder();
            sb.Append(events.Count).Append(" event(s) from ").Append(since.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)).Append(" to ").Append(until.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            if (clipped) sb.Append(" (window capped at ").Append(options.MaxEventsWindowSeconds).Append(" seconds)");
            if (collector.Overflowed) sb.Append(" (more events exist; narrow the window or filters)");
            sb.Append('\n');
            if (rows.Count > 0) sb.Append(TextTable.Render(new List<string> { "TIME", "TYPE", "ACTION", "ACTOR", "DETAILS" }, rows));
            ToolResult result = new ToolResult();
            result.Text = OutputLimiter.Head(sb.ToString(), context.Limits.MaxBytes, context.Limits.MaxLines, result);
            result.Truncated = result.Truncated || collector.Overflowed;
            result.Metrics["rows"] = rows.Count;
            return result;
        }

        #endregion
    }
}
