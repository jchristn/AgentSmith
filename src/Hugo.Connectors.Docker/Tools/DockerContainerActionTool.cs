namespace Hugo.Connectors.Docker.Tools
{
    using System;
    using System.Globalization;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using global::Docker.DotNet.Models;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>docker_container_action</c>: start, restart, stop, kill, or remove a container. Classified per call: start
    /// and restart are Write; stop, kill, and remove are Destructive; an unknown action fails closed as Destructive.
    /// Thread-safe.
    /// </summary>
    public class DockerContainerActionTool : DockerToolBase
    {
        #region Private-Members

        private const string _Schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""container"": { ""type"": ""string"", ""minLength"": 1, ""maxLength"": 256, ""description"": ""Container name or ID. Check it with docker_ps first."" },
    ""action"": { ""type"": ""string"", ""enum"": [""start"", ""restart"", ""stop"", ""kill"", ""remove""], ""description"": ""start and restart are Write; stop, kill, and remove are Destructive."" },
    ""timeoutSeconds"": { ""type"": ""integer"", ""minimum"": 0, ""maximum"": 300, ""description"": ""For stop and restart: seconds to wait for a graceful exit before killing. Default: the container's own stop timeout."" },
    ""signal"": { ""type"": ""string"", ""pattern"": ""^(SIG)?[A-Z0-9]+$"", ""maxLength"": 16, ""description"": ""For kill: the signal to send, for example SIGTERM or SIGHUP. Default SIGKILL."" },
    ""force"": { ""type"": ""boolean"", ""description"": ""For remove: remove a running container by killing it first. Default false."" },
    ""removeVolumes"": { ""type"": ""boolean"", ""description"": ""For remove: also remove anonymous volumes attached to the container. Default false."" }
  },
  ""required"": [""container"", ""action""],
  ""additionalProperties"": false
}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DockerContainerActionTool() : base(new ToolDescriptor
        {
            Name = "docker_container_action",
            Title = "Start, restart, stop, kill, or remove a container",
            Description = "Change a container's lifecycle state. start and restart are Write; stop, kill, and remove are Destructive and usually need approval. "
                + "State the container, the action, and why before calling, and confirm the result with docker_ps or docker_inspect afterwards.",
            InputSchemaJson = _Schema,
            BaseEffect = EffectEnum.Write,
            ClassifiedPerCall = true,
            OutputDescription = "The container's state after the action."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override EffectClassification ClassifyCall(ConnectorInvocationContext? context, ToolArguments arguments, DockerOptions options)
        {
            string action = (Optional(arguments, "action") ?? String.Empty).ToLowerInvariant();
            string container = DockerFormat.Shorten(Optional(arguments, "container") ?? "?", 100);
            bool force = arguments.GetBool("force", false);
            string verb = action.Length == 0 ? "Change" : Char.ToUpperInvariant(action[0]) + action.Substring(1);
            string summary = verb + " container " + container + (action == "remove" && force ? " (force)" : String.Empty) + " on " + TargetName(context);
            switch (action)
            {
                case "start":
                    return new EffectClassification(EffectEnum.Write, "starting a container is Write", summary);
                case "restart":
                    return new EffectClassification(EffectEnum.Write, "restarting a container is Write", summary);
                case "stop":
                    return new EffectClassification(EffectEnum.Destructive, "stopping a container takes it offline", summary);
                case "kill":
                    return new EffectClassification(EffectEnum.Destructive, "killing a container ends it without a graceful shutdown", summary);
                case "remove":
                    return new EffectClassification(EffectEnum.Destructive, "removing a container deletes it" + (force ? " even while running" : String.Empty), summary);
                default:
                    return new EffectClassification(EffectEnum.Destructive, "unknown action '" + DockerFormat.Shorten(action, 40) + "', treated as Destructive", summary);
            }
        }

        /// <inheritdoc />
        protected override TimeSpan CallTimeout(ConnectorInvocationContext context, DockerOptions options, ToolArguments arguments)
        {
            TimeSpan t = TimeSpan.FromSeconds(options.RequestTimeoutSeconds + arguments.GetInt("timeoutSeconds", 10, 0, 300) + 15);
            return context.Timeout < t ? context.Timeout : t;
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, DockerOptions options, CancellationToken token)
        {
            string container = Container(arguments);
            string action = (Optional(arguments, "action") ?? String.Empty).ToLowerInvariant();
            uint? wait = arguments.Has("timeoutSeconds") ? (uint)arguments.GetInt("timeoutSeconds", 10, 0, 300) : null;
            DockerSession session = await GetSessionAsync(context, options, token).ConfigureAwait(false);
            ContainerInspectResponse before = await session.Client.Containers.InspectContainerAsync(container, token).ConfigureAwait(false);
            string label = (before.Name ?? container).TrimStart('/');
            string note;

            switch (action)
            {
                case "start":
                    bool started = await session.Client.Containers.StartContainerAsync(before.ID, new ContainerStartParameters(), token).ConfigureAwait(false);
                    note = started ? "started" : "was already running";
                    break;
                case "restart":
                    await session.Client.Containers.RestartContainerAsync(before.ID, new ContainerRestartParameters { WaitBeforeKillSeconds = wait }, token).ConfigureAwait(false);
                    note = "restarted";
                    break;
                case "stop":
                    bool stopped = await session.Client.Containers.StopContainerAsync(before.ID, new ContainerStopParameters { WaitBeforeKillSeconds = wait }, token).ConfigureAwait(false);
                    note = stopped ? "stopped" : "was already stopped";
                    break;
                case "kill":
                    if (before.State == null || !before.State.Running) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Container " + label + " is not running, so it cannot be killed.");
                    string signal = (Optional(arguments, "signal") ?? "SIGKILL").ToUpperInvariant();
                    await session.Client.Containers.KillContainerAsync(before.ID, new ContainerKillParameters { Signal = signal }, token).ConfigureAwait(false);
                    note = "sent " + signal;
                    break;
                case "remove":
                    bool force = arguments.GetBool("force", false);
                    if (before.State != null && before.State.Running && !force)
                        throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Container " + label + " is running. Stop it first, or pass force: true to kill and remove it.");
                    await session.Client.Containers.RemoveContainerAsync(before.ID, new ContainerRemoveParameters { Force = force, RemoveVolumes = arguments.GetBool("removeVolumes", false) }, token).ConfigureAwait(false);
                    JsonObject removed = new JsonObject { ["container"] = label, ["id"] = DockerFormat.ShortId(before.ID), ["action"] = action, ["state"] = "removed" };
                    return ToolResult.FromText("Container " + label + " (" + DockerFormat.ShortId(before.ID) + ") removed.", removed.ToJsonString());
                default:
                    throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "action must be start, restart, stop, kill, or remove.");
            }

            ContainerInspectResponse after = await session.Client.Containers.InspectContainerAsync(before.ID, token).ConfigureAwait(false);
            string state = after.State?.Status ?? "unknown";
            string text = "Container " + label + " (" + DockerFormat.ShortId(before.ID) + ") " + note + ". State before: " + (before.State?.Status ?? "unknown") + ", after: " + state
                + (after.State != null && !after.State.Running ? ", exit code " + after.State.ExitCode.ToString(CultureInfo.InvariantCulture) : String.Empty) + ".";
            JsonObject structured = new JsonObject { ["container"] = label, ["id"] = DockerFormat.ShortId(before.ID), ["action"] = action, ["stateBefore"] = before.State?.Status, ["state"] = state };
            return ToolResult.FromText(text, structured.ToJsonString());
        }

        #endregion
    }
}
