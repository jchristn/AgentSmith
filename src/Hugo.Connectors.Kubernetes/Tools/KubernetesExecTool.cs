namespace Hugo.Connectors.Kubernetes.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using k8s;
    using k8s.Models;

    /// <summary>
    /// <c>k8s_exec</c>: run one command in a container (no terminal, no stdin) with a timeout and bounded output.
    /// Always Destructive: the command runs with the container's privileges and cannot be classified reliably.
    /// Thread-safe.
    /// </summary>
    public class KubernetesExecTool : KubernetesToolBase
    {
        #region Private-Members

        private const string _Schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""pod"": { ""type"": ""string"", ""minLength"": 1, ""maxLength"": 253, ""description"": ""Name of a running pod. Find it with k8s_list kind Pod."" },
    ""namespace"": { ""type"": ""string"", ""maxLength"": 63, ""description"": ""Namespace. Defaults to the resource's default namespace."" },
    ""container"": { ""type"": ""string"", ""maxLength"": 253, ""description"": ""Container name. Defaults to the pod's default container."" },
    ""command"": { ""type"": ""array"", ""minItems"": 1, ""maxItems"": 64, ""items"": { ""type"": ""string"", ""maxLength"": 8192 }, ""description"": ""Program and arguments, for example [\""cat\"", \""/etc/resolv.conf\""]. No shell is involved; use [\""sh\"", \""-c\"", \""...\""] when you need one."" },
    ""timeoutSeconds"": { ""type"": ""integer"", ""minimum"": 1, ""maximum"": 900, ""description"": ""Stop waiting after this many seconds. Default and maximum: the resource's execTimeoutSeconds (60 unless configured)."" }
  },
  ""required"": [""pod"", ""command""],
  ""additionalProperties"": false
}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public KubernetesExecTool() : base(new ToolDescriptor
        {
            Name = "k8s_exec",
            Title = "Run a command in a container",
            Description = "Run one command inside a running container, without a terminal or stdin, and return its exit code, stdout, and stderr (the end is kept when output is large). "
                + "Always Destructive and normally needs approval: state the command and why before calling. Prefer k8s_logs, k8s_describe, and k8s_get for investigation.",
            InputSchemaJson = _Schema,
            BaseEffect = EffectEnum.Destructive,
            OutputDescription = "Exit code, stdout, and stderr."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override EffectClassification ClassifyCall(ConnectorInvocationContext? context, ToolArguments arguments, KubernetesOptions options)
        {
            string pod = Optional(arguments, "pod") ?? "?";
            string? ns = Optional(arguments, "namespace");
            string? container = Optional(arguments, "container");
            List<string> command = arguments.GetStringList("command");
            string summary = "Run \"" + KubernetesFormat.Shorten(String.Join(" ", command), 200) + "\" in pod " + KubernetesFormat.Ref(ns ?? KubernetesNamespacePolicy.Effective(options, null), pod)
                + (container == null ? String.Empty : " container " + container) + " on " + TargetName(context);
            return Scoped(options, null, null, ns, EffectEnum.Destructive, "exec runs an arbitrary command inside a container", summary);
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, KubernetesOptions options, CancellationToken token)
        {
            string podName = Required(arguments, "pod");
            string ns = KubernetesNamespacePolicy.Require(options, Optional(arguments, "namespace"));
            string? container = Optional(arguments, "container");
            List<string> command = arguments.GetStringList("command");
            if (command.Count == 0 || String.IsNullOrWhiteSpace(command[0])) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "command must name a program, for example [\"ls\", \"-la\", \"/tmp\"].");
            int timeoutSeconds = Math.Min(arguments.GetInt("timeoutSeconds", options.ExecTimeoutSeconds, 1, 900), options.ExecTimeoutSeconds);
            TimeSpan timeout = TimeSpan.FromSeconds(timeoutSeconds);
            if (context.Timeout < timeout) timeout = context.Timeout;

            KubernetesSession session = await GetSessionAsync(context, options, token).ConfigureAwait(false);
            string podPath = "/api/v1/namespaces/" + Uri.EscapeDataString(ns) + "/pods/" + Uri.EscapeDataString(podName);
            V1Pod pod = KubernetesFormat.Typed<V1Pod>(await session.GetAsync(podPath, "pod " + ns + "/" + podName, token).ConfigureAwait(false), "pod");
            List<string> containers = (pod.Spec?.Containers ?? new List<V1Container>()).Select(c => c.Name).ToList();
            if (container == null)
            {
                string? annotated = null;
                if (pod.Metadata?.Annotations != null) pod.Metadata.Annotations.TryGetValue("kubectl.kubernetes.io/default-container", out annotated);
                container = !String.IsNullOrEmpty(annotated) && containers.Contains(annotated) ? annotated : containers.FirstOrDefault();
            }
            if (container == null || !containers.Contains(container))
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Pod " + ns + "/" + podName + " has no container '" + container + "'. Containers: " + String.Join(", ", containers) + ".");
            if (!String.Equals(pod.Status?.Phase, "Running", StringComparison.Ordinal))
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Pod " + ns + "/" + podName + " is " + (pod.Status?.Phase ?? "not running") + "; exec needs a running container.");

            int budget = Math.Max(1024, OutputBudget(context));
            KubernetesTailBuffer stdout = new KubernetesTailBuffer(budget);
            KubernetesTailBuffer stderr = new KubernetesTailBuffer(budget);
            int? exitCode = null;
            bool timedOut = false;
            string? failure = null;

            IStreamDemuxer muxer;
            try
            {
                muxer = await session.Client.MuxedStreamNamespacedPodExecAsync(podName, ns, command, container, true, false, true, false, "v4.channel.k8s.io", null, token).ConfigureAwait(false);
            }
            catch (Exception e) when (!(e is OperationCanceledException) && !(e is ConnectorException))
            {
                throw KubernetesErrorMapper.FromException(e, "exec in pod " + ns + "/" + podName, session.Server);
            }

            KubernetesTailBuffer status = new KubernetesTailBuffer(64 * 1024);
            try
            {
                Stream outStream = muxer.GetStream(ChannelIndex.StdOut, null);
                Stream errStream = muxer.GetStream(ChannelIndex.StdErr, null);
                Stream statusStream = muxer.GetStream(ChannelIndex.Error, null);
                muxer.Start();
                Task pumps = Task.WhenAll(stdout.PumpAsync(outStream, CancellationToken.None), stderr.PumpAsync(errStream, CancellationToken.None), status.PumpAsync(statusStream, CancellationToken.None));
                using (CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    Task wait = Task.Delay(timeout, limit.Token);
                    Task first = await Task.WhenAny(pumps, wait).ConfigureAwait(false);
                    limit.Cancel();
                    token.ThrowIfCancellationRequested();
                    if (first != pumps)
                    {
                        timedOut = true;
                    }
                    else
                    {
                        await pumps.ConfigureAwait(false);
                        V1Status? final = ParseExecStatus(status.Text());
                        if (final == null || String.Equals(final.Status, "Success", StringComparison.Ordinal)) exitCode = 0;
                        else
                        {
                            V1StatusCause? cause = final.Details?.Causes?.FirstOrDefault(c => String.Equals(c.Reason, "ExitCode", StringComparison.Ordinal));
                            if (cause != null && Int32.TryParse(cause.Message, NumberStyles.Integer, CultureInfo.InvariantCulture, out int code)) exitCode = code;
                            else failure = final.Message ?? "the command failed";
                        }
                    }
                }
            }
            catch (IOException e)
            {
                throw KubernetesErrorMapper.FromException(e, "exec in pod " + ns + "/" + podName, session.Server);
            }
            finally
            {
                if (muxer is IDisposable disposable) disposable.Dispose();
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("pod: ").Append(ns).Append('/').Append(podName).Append(", container: ").Append(container).Append('\n');
            sb.Append("command: ").Append(KubernetesFormat.Shorten(String.Join(" ", command), 500)).Append('\n');
            sb.Append("exit_code: ").Append(exitCode.HasValue ? exitCode.Value.ToString(CultureInfo.InvariantCulture) : (timedOut ? "none (timed out after " + (int)timeout.TotalSeconds + " seconds)" : "none")).Append('\n');
            if (failure != null) sb.Append("error: ").Append(KubernetesFormat.Shorten(failure, 500)).Append('\n');
            ToolResult result = new ToolResult();
            AppendStream(sb, "stdout", stdout, budget / 2, context.Limits.MaxLines, result);
            AppendStream(sb, "stderr", stderr, budget / 2, context.Limits.MaxLines, result);
            result.Text = sb.ToString();
            result.StructuredJson = new JsonObject
            {
                ["pod"] = podName,
                ["namespace"] = ns,
                ["container"] = container,
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
            else if (failure != null)
            {
                result.IsError = true;
                result.ErrorCode = "command_failed";
            }
            return result;
        }

        #endregion

        #region Private-Methods

        private static V1Status? ParseExecStatus(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) return null;
            try
            {
                return KubernetesJson.Deserialize<V1Status>(text);
            }
            catch (System.Text.Json.JsonException)
            {
                return new V1Status { Status = "Failure", Message = KubernetesFormat.Shorten(text, 500) };
            }
        }

        private static void AppendStream(StringBuilder sb, string label, KubernetesTailBuffer buffer, int maxBytes, int maxLines, ToolResult result)
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
