namespace Hugo.Connectors.Docker.Tools
{
    using System;
    using System.Globalization;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using global::Docker.DotNet;
    using global::Docker.DotNet.Models;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>docker_logs</c>: the last lines of a container's log (stdout and stderr interleaved as written). Always Read.
    /// Thread-safe.
    /// </summary>
    public class DockerLogsTool : DockerToolBase
    {
        #region Private-Members

        private const string _Schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""container"": { ""type"": ""string"", ""minLength"": 1, ""maxLength"": 256, ""description"": ""Container name or ID. Find it with docker_ps."" },
    ""tail"": { ""type"": ""integer"", ""minimum"": 1, ""maximum"": 100000, ""description"": ""Lines from the end. Default and maximum: the resource's maxLogLines (500 unless configured)."" },
    ""since"": { ""type"": ""string"", ""maxLength"": 64, ""description"": ""Only lines newer than this: seconds (300), a duration (10m, 2h, 1d), or an ISO 8601 time."" },
    ""until"": { ""type"": ""string"", ""maxLength"": 64, ""description"": ""Only lines older than this, in the same formats as since."" },
    ""timestamps"": { ""type"": ""boolean"", ""description"": ""Prefix each line with its timestamp. Default false."" },
    ""stream"": { ""type"": ""string"", ""enum"": [""both"", ""stdout"", ""stderr""], ""description"": ""Which output to read. Default both."" }
  },
  ""required"": [""container""],
  ""additionalProperties"": false
}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DockerLogsTool() : base(new ToolDescriptor
        {
            Name = "docker_logs",
            Title = "Read container logs",
            Description = "Return the last lines of a container's log, stdout and stderr interleaved as written. Works for stopped containers too, which is how you read why one exited. "
                + "Bound the read with tail and since; output keeps the newest lines when it is cut.",
            InputSchemaJson = _Schema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "Log text, newest lines last, with a header naming the container."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override EffectClassification ClassifyCall(ConnectorInvocationContext? context, ToolArguments arguments, DockerOptions options)
        {
            return Read("reading logs is read-only", "Read logs of container " + DockerFormat.Shorten(Optional(arguments, "container"), 100) + " on " + TargetName(context));
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, DockerOptions options, CancellationToken token)
        {
            string container = Container(arguments);
            int tail = Math.Min(arguments.GetInt("tail", options.MaxLogLines, 1, 100000), options.MaxLogLines);
            DateTime now = DateTime.UtcNow;
            DateTime? since = DockerFormat.ParseTime(Optional(arguments, "since"), now, "since");
            DateTime? until = DockerFormat.ParseTime(Optional(arguments, "until"), now, "until");
            string stream = (Optional(arguments, "stream") ?? "both").ToLowerInvariant();
            if (stream != "both" && stream != "stdout" && stream != "stderr") throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "stream must be both, stdout, or stderr.");

            DockerSession session = await GetSessionAsync(context, options, token).ConfigureAwait(false);
            ContainerInspectResponse inspected = await session.Client.Containers.InspectContainerAsync(container, token).ConfigureAwait(false);
            ContainerLogsParameters parameters = new ContainerLogsParameters
            {
                ShowStdout = stream != "stderr",
                ShowStderr = stream != "stdout",
                Tail = tail.ToString(CultureInfo.InvariantCulture),
                Timestamps = arguments.GetBool("timestamps", false),
                Follow = false,
                Since = since.HasValue ? DockerFormat.UnixSeconds(since.Value) : null,
                Until = until.HasValue ? DockerFormat.UnixSeconds(until.Value) : null
            };

            int budget = Math.Max(1024, context.Limits.MaxBytes);
            DockerTailBuffer buffer = new DockerTailBuffer(budget * 2);
            using (MultiplexedStream logs = await session.Client.Containers.GetContainerLogsAsync(inspected.ID, parameters, token).ConfigureAwait(false))
            {
                await DockerStreamReader.ReadAsync(logs, buffer, buffer, token).ConfigureAwait(false);
            }

            StringBuilder header = new StringBuilder();
            header.Append("container: ").Append(inspected.Name?.TrimStart('/')).Append(" (").Append(DockerFormat.ShortId(inspected.ID)).Append("), state: ").Append(inspected.State?.Status);
            if (inspected.State != null && !inspected.State.Running) header.Append(", exit code: ").Append(inspected.State.ExitCode.ToString(CultureInfo.InvariantCulture));
            header.Append(", tail: ").Append(tail);
            if (stream != "both") header.Append(", stream: ").Append(stream);
            header.Append('\n');

            ToolResult result = new ToolResult();
            if (buffer.Dropped)
            {
                result.Truncated = true;
                result.OriginalBytes = buffer.TotalBytes;
            }
            string text = buffer.Text();
            string body = text.Length == 0 ? "(no log lines)\n" : OutputLimiter.Tail(text, Math.Max(1024, budget - header.Length), Math.Min(context.Limits.MaxLines, options.MaxLogLines), result);
            result.Text = header.ToString() + body;
            result.Metrics["bytes"] = buffer.TotalBytes;
            return result;
        }

        #endregion
    }
}
