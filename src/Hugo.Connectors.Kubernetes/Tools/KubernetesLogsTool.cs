namespace Hugo.Connectors.Kubernetes.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using k8s.Models;

    /// <summary>
    /// <c>k8s_logs</c>: the last lines of a container's log, optionally from the previous (crashed) instance. Always
    /// Read. Thread-safe.
    /// </summary>
    public class KubernetesLogsTool : KubernetesToolBase
    {
        #region Private-Members

        private const string _Schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""pod"": { ""type"": ""string"", ""minLength"": 1, ""maxLength"": 253, ""description"": ""Pod name. Find it with k8s_list kind Pod."" },
    ""namespace"": { ""type"": ""string"", ""maxLength"": 63, ""description"": ""Namespace. Defaults to the resource's default namespace."" },
    ""container"": { ""type"": ""string"", ""maxLength"": 253, ""description"": ""Container name. Defaults to the pod's default container (the kubectl.kubernetes.io/default-container annotation, else the first)."" },
    ""tailLines"": { ""type"": ""integer"", ""minimum"": 1, ""maximum"": 100000, ""description"": ""Lines from the end. Default and maximum: the resource's maxLogLines (500 unless configured)."" },
    ""sinceSeconds"": { ""type"": ""integer"", ""minimum"": 1, ""maximum"": 2592000, ""description"": ""Only lines newer than this many seconds."" },
    ""previous"": { ""type"": ""boolean"", ""description"": ""Read the previous, terminated instance of the container. Use it for crash loops. Default false."" },
    ""timestamps"": { ""type"": ""boolean"", ""description"": ""Prefix each line with its timestamp. Default false."" }
  },
  ""required"": [""pod""],
  ""additionalProperties"": false
}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public KubernetesLogsTool() : base(new ToolDescriptor
        {
            Name = "k8s_logs",
            Title = "Read container logs",
            Description = "Return the last lines of one container's log. Use previous: true to read the instance that crashed in a CrashLoopBackOff. "
                + "Bound the read with tailLines and sinceSeconds; output is capped by the resource's maxLogLines and the call's output limit, keeping the newest lines.",
            InputSchemaJson = _Schema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "Log text, newest lines last, with a header naming the pod and container."
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
            string summary = "Read logs of pod " + KubernetesFormat.Ref(ns, pod) + " on " + TargetName(context);
            return Scoped(options, null, null, ns, EffectEnum.Read, "reading logs is read-only", summary);
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, KubernetesOptions options, CancellationToken token)
        {
            string podName = Required(arguments, "pod");
            string ns = KubernetesNamespacePolicy.Require(options, Optional(arguments, "namespace"));
            string? container = Optional(arguments, "container");
            int tailLines = Math.Min(arguments.GetInt("tailLines", options.MaxLogLines, 1, 100000), options.MaxLogLines);
            int? sinceSeconds = arguments.Has("sinceSeconds") ? arguments.GetInt("sinceSeconds", 0, 1, 2592000) : null;
            bool previous = arguments.GetBool("previous", false);
            bool timestamps = arguments.GetBool("timestamps", false);

            KubernetesSession session = await GetSessionAsync(context, options, token).ConfigureAwait(false);
            string podPath = "/api/v1/namespaces/" + Uri.EscapeDataString(ns) + "/pods/" + Uri.EscapeDataString(podName);
            V1Pod pod = KubernetesFormat.Typed<V1Pod>(await session.GetAsync(podPath, "pod " + ns + "/" + podName, token).ConfigureAwait(false), "pod");
            List<string> containers = (pod.Spec?.Containers ?? new List<V1Container>()).Select(c => c.Name).ToList();
            List<string> initContainers = (pod.Spec?.InitContainers ?? new List<V1Container>()).Select(c => c.Name).ToList();
            if (container == null)
            {
                string? annotated = null;
                if (pod.Metadata?.Annotations != null) pod.Metadata.Annotations.TryGetValue("kubectl.kubernetes.io/default-container", out annotated);
                container = !String.IsNullOrEmpty(annotated) && containers.Contains(annotated) ? annotated : containers.FirstOrDefault();
                if (container == null) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Pod " + ns + "/" + podName + " has no containers.");
            }
            else if (!containers.Contains(container) && !initContainers.Contains(container))
            {
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Pod " + ns + "/" + podName + " has no container '" + container + "'. Containers: " + String.Join(", ", containers.Concat(initContainers)) + ".");
            }

            string path = podPath + "/log" + KubernetesSession.Query(
                "container", container,
                "tailLines", KubernetesSession.Number(tailLines),
                "sinceSeconds", KubernetesSession.Number(sinceSeconds),
                "previous", previous ? "true" : null,
                "timestamps", timestamps ? "true" : null);
            KubernetesApiResponse response = await session.SendAsync(HttpMethod.Get, path, null, null, "text/plain, */*", token).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                ConnectorException mapped = KubernetesErrorMapper.FromResponse(response, "logs of " + ns + "/" + podName + " container " + container);
                if (previous && mapped.Code == ConnectorErrorCodes.InvalidArgument)
                    throw new ConnectorException(ConnectorErrorCodes.NotFound, "Container " + container + " in pod " + ns + "/" + podName + " has no previous instance (it has not restarted). " + mapped.Message);
                throw mapped;
            }

            V1ContainerStatus? status = pod.Status?.ContainerStatuses?.FirstOrDefault(s => s.Name == container);
            StringBuilder header = new StringBuilder();
            header.Append("pod: ").Append(ns).Append('/').Append(podName).Append(", container: ").Append(container);
            if (previous) header.Append(" (previous instance)");
            if (status != null) header.Append(", restarts: ").Append(status.RestartCount);
            if (containers.Count > 1) header.Append(", other containers: ").Append(String.Join(", ", containers.Where(c => c != container)));
            header.Append(", tailLines: ").Append(tailLines);
            if (sinceSeconds.HasValue) header.Append(", sinceSeconds: ").Append(sinceSeconds.Value);
            header.Append('\n');

            ToolResult result = new ToolResult();
            int maxLines = Math.Min(context.Limits.MaxLines, options.MaxLogLines);
            string body = response.Body.Length == 0 ? "(no log lines)\n" : OutputLimiter.Tail(response.Body, Math.Max(1024, OutputBudget(context) - header.Length), maxLines, result);
            result.Text = header.ToString() + body;
            result.Metrics["bytes"] = Encoding.UTF8.GetByteCount(response.Body);
            return result;
        }

        #endregion
    }
}
