namespace Hugo.Connectors.Docker.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using global::Docker.DotNet;
    using global::Docker.DotNet.Models;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>docker_exec</c>: run one command in a running container (no terminal, no stdin) with a timeout and bounded
    /// output. Always Destructive. Thread-safe.
    /// </summary>
    public class DockerExecTool : DockerToolBase
    {
        #region Private-Members

        private const string _Schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""container"": { ""type"": ""string"", ""minLength"": 1, ""maxLength"": 256, ""description"": ""Name or ID of a running container."" },
    ""command"": { ""type"": ""array"", ""minItems"": 1, ""maxItems"": 64, ""items"": { ""type"": ""string"", ""maxLength"": 8192 }, ""description"": ""Program and arguments, for example [\""cat\"", \""/etc/hosts\""]. No shell is involved; use [\""sh\"", \""-c\"", \""...\""] when you need one."" },
    ""user"": { ""type"": ""string"", ""maxLength"": 64, ""description"": ""Run as this user (name or uid[:gid]). Default: the container's user."" },
    ""workingDir"": { ""type"": ""string"", ""maxLength"": 1024, ""description"": ""Working directory inside the container. Default: the container's working directory."" },
    ""timeoutSeconds"": { ""type"": ""integer"", ""minimum"": 1, ""maximum"": 900, ""description"": ""Stop waiting after this many seconds. Default and maximum: the resource's execTimeoutSeconds (60 unless configured)."" }
  },
  ""required"": [""container"", ""command""],
  ""additionalProperties"": false
}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DockerExecTool() : base(new ToolDescriptor
        {
            Name = "docker_exec",
            Title = "Run a command in a container",
            Description = "Run one command inside a running container without a terminal or stdin, and return its exit code, stdout, and stderr (the end is kept when output is large). "
                + "Always Destructive and normally needs approval: state the command and why before calling. Prefer docker_logs and docker_inspect for investigation.",
            InputSchemaJson = _Schema,
            BaseEffect = EffectEnum.Destructive,
            OutputDescription = "Exit code, stdout, and stderr."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override EffectClassification ClassifyCall(ConnectorInvocationContext? context, ToolArguments arguments, DockerOptions options)
        {
            List<string> command = arguments.GetStringList("command");
            string summary = "Run \"" + DockerFormat.Shorten(String.Join(" ", command), 200) + "\" in container " + DockerFormat.Shorten(Optional(arguments, "container") ?? "?", 100)
                + (Optional(arguments, "user") == null ? String.Empty : " as " + Optional(arguments, "user")) + " on " + TargetName(context);
            return new EffectClassification(EffectEnum.Destructive, "exec runs an arbitrary command inside a container", summary);
        }

        /// <inheritdoc />
        protected override TimeSpan CallTimeout(ConnectorInvocationContext context, DockerOptions options, ToolArguments arguments)
        {
            TimeSpan t = TimeSpan.FromSeconds(options.ExecTimeoutSeconds + options.RequestTimeoutSeconds);
            return context.Timeout < t ? context.Timeout : t;
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, DockerOptions options, CancellationToken token)
        {
            string container = Container(arguments);
            List<string> command = arguments.GetStringList("command");
            if (command.Count == 0 || String.IsNullOrWhiteSpace(command[0])) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "command must name a program, for example [\"ls\", \"-la\", \"/tmp\"].");
            int timeoutSeconds = Math.Min(arguments.GetInt("timeoutSeconds", options.ExecTimeoutSeconds, 1, 900), options.ExecTimeoutSeconds);
            TimeSpan timeout = TimeSpan.FromSeconds(timeoutSeconds);
            if (context.Timeout < timeout) timeout = context.Timeout;

            DockerSession session = await GetSessionAsync(context, options, token).ConfigureAwait(false);
            ContainerInspectResponse inspected = await session.Client.Containers.InspectContainerAsync(container, token).ConfigureAwait(false);
            string label = (inspected.Name ?? container).TrimStart('/');
            if (inspected.State == null || !inspected.State.Running)
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Container " + label + " is " + (inspected.State?.Status ?? "not running") + "; exec needs a running container.");

            ContainerExecCreateResponse created = await session.Client.Exec.CreateContainerExecAsync(inspected.ID, new ContainerExecCreateParameters
            {
                AttachStdout = true,
                AttachStderr = true,
                AttachStdin = false,
                TTY = false,
                Cmd = command,
                User = Optional(arguments, "user") ?? String.Empty,
                WorkingDir = Optional(arguments, "workingDir") ?? String.Empty
            }, token).ConfigureAwait(false);

            int budget = Math.Max(1024, context.Limits.MaxBytes);
            DockerTailBuffer stdout = new DockerTailBuffer(budget);
            DockerTailBuffer stderr = new DockerTailBuffer(budget);
            bool timedOut = false;
            using (CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                limit.CancelAfter(timeout);
                MultiplexedStream stream = await session.Client.Exec.StartContainerExecAsync(created.ID, new ContainerExecStartParameters { Detach = false, TTY = false }, limit.Token).ConfigureAwait(false);
                try
                {
                    Task pump = DockerStreamReader.ReadAsync(stream, stdout, stderr, limit.Token);
                    Task wait = Task.Delay(Timeout.InfiniteTimeSpan, limit.Token);
                    Task first = await Task.WhenAny(pump, wait).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (first == pump && pump.IsCompletedSuccessfully) await pump.ConfigureAwait(false);
                    else if (first == pump && pump.IsFaulted && !limit.IsCancellationRequested) await pump.ConfigureAwait(false);
                    else timedOut = true;
                }
                finally
                {
                    stream.Dispose();
                }
            }

            long? exitCode = null;
            bool running = false;
            if (!timedOut)
            {
                ContainerExecInspectResponse status = await session.Client.Exec.InspectContainerExecAsync(created.ID, token).ConfigureAwait(false);
                exitCode = status.ExitCode;
                running = status.Running;
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("container: ").Append(label).Append('\n');
            sb.Append("command: ").Append(DockerFormat.Shorten(String.Join(" ", command), 500)).Append('\n');
            sb.Append("exit_code: ").Append(exitCode.HasValue ? exitCode.Value.ToString(CultureInfo.InvariantCulture) : (timedOut ? "none (timed out after " + (int)timeout.TotalSeconds + " seconds; the process may still be running in the container)" : (running ? "none (still running)" : "none"))).Append('\n');
            ToolResult result = new ToolResult();
            AppendStream(sb, "stdout", stdout, budget / 2, context.Limits.MaxLines, result);
            AppendStream(sb, "stderr", stderr, budget / 2, context.Limits.MaxLines, result);
            result.Text = sb.ToString();
            result.StructuredJson = new JsonObject
            {
                ["container"] = label,
                ["exitCode"] = exitCode,
                ["timedOut"] = timedOut,
                ["stdoutBytes"] = stdout.TotalBytes,
                ["stderrBytes"] = stderr.TotalBytes
            }.ToJsonString();
            if (timedOut)
            {
                result.IsError = true;
                result.ErrorCode = ConnectorErrorCodes.Timeout;
            }
            else if (exitCode == 126 || exitCode == 127)
            {
                result.IsError = true;
                result.ErrorCode = "command_failed";
            }
            return result;
        }

        #endregion

        #region Private-Methods

        private static void AppendStream(StringBuilder sb, string label, DockerTailBuffer buffer, int maxBytes, int maxLines, ToolResult result)
        {
            string text = buffer.Text();
            sb.Append("--- ").Append(label).Append(" (").Append(buffer.TotalBytes.ToString(CultureInfo.InvariantCulture)).Append(" bytes) ---\n");
            if (text.Length == 0) return;
            if (buffer.Dropped)
            {
                result.Truncated = true;
                result.OriginalBytes = Math.Max(result.OriginalBytes, buffer.TotalBytes);
            }
            string bounded = OutputLimiter.Tail(text, Math.Max(512, maxBytes), maxLines, result);
            sb.Append(bounded);
            if (!bounded.EndsWith('\n')) sb.Append('\n');
        }

        #endregion
    }
}
