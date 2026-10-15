namespace Hugo.Connectors.Kubernetes
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Text.Json.Nodes;
    using k8s.Models;

    /// <summary>
    /// Builds a <c>kubectl describe</c>-like summary. Common kinds (Pod, Deployment, StatefulSet, DaemonSet, ReplicaSet,
    /// Job, Service, Node, ConfigMap, Secret) use the typed KubernetesClient models; every other kind gets a generic
    /// summary of metadata, owners, phase, and conditions. Secret values never appear: only key names and sizes.
    /// Thread-safe.
    /// </summary>
    public static class KubernetesDescriber
    {
        #region Public-Methods

        /// <summary>
        /// Describe an object.
        /// </summary>
        /// <param name="type">Resource type.</param>
        /// <param name="body">Raw JSON of the object.</param>
        /// <param name="now">Current time (UTC) for ages.</param>
        /// <returns>Summary text.</returns>
        public static string Describe(KubernetesResourceType type, string body, DateTime now)
        {
            ArgumentNullException.ThrowIfNull(type);
            StringBuilder sb = new StringBuilder();
            bool core = type.Group.Length == 0;
            bool apps = String.Equals(type.Group, "apps", StringComparison.Ordinal);
            bool batch = String.Equals(type.Group, "batch", StringComparison.Ordinal);
            if (core && type.Kind == "Pod") DescribePod(sb, KubernetesFormat.Typed<V1Pod>(body, "pod"), now);
            else if (apps && type.Kind == "Deployment") DescribeDeployment(sb, KubernetesFormat.Typed<V1Deployment>(body, "deployment"), now);
            else if (apps && type.Kind == "StatefulSet") DescribeStatefulSet(sb, KubernetesFormat.Typed<V1StatefulSet>(body, "statefulset"), now);
            else if (apps && type.Kind == "DaemonSet") DescribeDaemonSet(sb, KubernetesFormat.Typed<V1DaemonSet>(body, "daemonset"), now);
            else if (apps && type.Kind == "ReplicaSet") DescribeReplicaSet(sb, KubernetesFormat.Typed<V1ReplicaSet>(body, "replicaset"), now);
            else if (batch && type.Kind == "Job") DescribeJob(sb, KubernetesFormat.Typed<V1Job>(body, "job"), now);
            else if (core && type.Kind == "Service") DescribeService(sb, KubernetesFormat.Typed<V1Service>(body, "service"), now);
            else if (core && type.Kind == "Node") DescribeNode(sb, KubernetesFormat.Typed<V1Node>(body, "node"), now);
            else if (core && type.Kind == "ConfigMap") DescribeConfigMap(sb, KubernetesFormat.Typed<V1ConfigMap>(body, "configmap"), now);
            else if (core && type.Kind == "Secret") DescribeSecret(sb, KubernetesFormat.Typed<V1Secret>(body, "secret"), now);
            else DescribeGeneric(sb, type, KubernetesFormat.ParseNode(body, type.DisplayName), now);
            return sb.ToString();
        }

        #endregion

        #region Private-Methods

        private static void Header(StringBuilder sb, string kind, V1ObjectMeta? meta, DateTime now)
        {
            sb.Append("Kind:         ").Append(kind).Append('\n');
            sb.Append("Name:         ").Append(meta?.Name ?? String.Empty).Append('\n');
            if (!String.IsNullOrEmpty(meta?.NamespaceProperty)) sb.Append("Namespace:    ").Append(meta.NamespaceProperty).Append('\n');
            sb.Append("Created:      ").Append(Time(meta?.CreationTimestamp)).Append(" (age ").Append(KubernetesFormat.Age(meta?.CreationTimestamp, now)).Append(")\n");
            sb.Append("Labels:       ").Append(KubernetesFormat.Map(meta?.Labels)).Append('\n');
            Dictionary<string, string> annotations = (meta?.Annotations ?? new Dictionary<string, string>())
                .Where(kv => kv.Key != KubernetesObjectSanitizer.LastAppliedAnnotation)
                .ToDictionary(kv => kv.Key, kv => KubernetesFormat.Shorten(kv.Value, 120));
            sb.Append("Annotations:  ").Append(KubernetesFormat.Map(annotations)).Append('\n');
            if (meta?.OwnerReferences != null && meta.OwnerReferences.Count > 0)
                sb.Append("Controlled By: ").Append(String.Join(", ", meta.OwnerReferences.Select(o => o.Kind + "/" + o.Name))).Append('\n');
            if (meta?.DeletionTimestamp != null) sb.Append("Terminating:  since ").Append(Time(meta.DeletionTimestamp)).Append('\n');
        }

        private static void DescribePod(StringBuilder sb, V1Pod pod, DateTime now)
        {
            Header(sb, "Pod", pod.Metadata, now);
            sb.Append("Node:         ").Append(pod.Spec?.NodeName ?? "<none>").Append('\n');
            sb.Append("Status:       ").Append(pod.Status?.Phase ?? "Unknown");
            if (!String.IsNullOrEmpty(pod.Status?.Reason)) sb.Append(" (").Append(pod.Status.Reason).Append(')');
            sb.Append('\n');
            if (!String.IsNullOrEmpty(pod.Status?.Message)) sb.Append("Message:      ").Append(KubernetesFormat.Shorten(pod.Status.Message, 300)).Append('\n');
            sb.Append("IP:           ").Append(pod.Status?.PodIP ?? "<none>").Append('\n');
            sb.Append("QoS Class:    ").Append(pod.Status?.QosClass ?? "<none>").Append('\n');
            sb.Append("Service Account: ").Append(pod.Spec?.ServiceAccountName ?? "default").Append('\n');
            sb.Append("Containers:\n");
            foreach (V1Container c in pod.Spec?.Containers ?? new List<V1Container>())
            {
                V1ContainerStatus? cs = pod.Status?.ContainerStatuses?.FirstOrDefault(s => s.Name == c.Name);
                sb.Append("  ").Append(c.Name).Append(":\n");
                sb.Append("    Image:          ").Append(c.Image).Append('\n');
                if (c.Ports != null && c.Ports.Count > 0) sb.Append("    Ports:          ").Append(String.Join(", ", c.Ports.Select(p => p.ContainerPort.ToString(CultureInfo.InvariantCulture) + "/" + (p.Protocol ?? "TCP")))).Append('\n');
                if (cs != null)
                {
                    sb.Append("    State:          ").Append(State(cs.State, now)).Append('\n');
                    if (cs.LastState != null && (cs.LastState.Terminated != null || cs.LastState.Waiting != null)) sb.Append("    Last State:     ").Append(State(cs.LastState, now)).Append('\n');
                    sb.Append("    Ready:          ").Append(cs.Ready ? "True" : "False").Append('\n');
                    sb.Append("    Restart Count:  ").Append(cs.RestartCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
                }
                string resources = Resources(c.Resources);
                if (resources.Length > 0) sb.Append("    Resources:      ").Append(resources).Append('\n');
            }
            Conditions(sb, pod.Status?.Conditions?.Select(c => c.Type + "=" + c.Status + (String.IsNullOrEmpty(c.Reason) ? String.Empty : " (" + c.Reason + ")")));
            if (pod.Spec?.Volumes != null && pod.Spec.Volumes.Count > 0) sb.Append("Volumes:      ").Append(String.Join(", ", pod.Spec.Volumes.Select(v => v.Name))).Append('\n');
        }

        private static void DescribeDeployment(StringBuilder sb, V1Deployment d, DateTime now)
        {
            Header(sb, "Deployment", d.Metadata, now);
            sb.Append("Selector:     ").Append(KubernetesFormat.Map(d.Spec?.Selector?.MatchLabels)).Append('\n');
            sb.Append("Replicas:     ").Append(N(d.Spec?.Replicas)).Append(" desired | ").Append(N(d.Status?.UpdatedReplicas)).Append(" updated | ")
                .Append(N(d.Status?.Replicas)).Append(" total | ").Append(N(d.Status?.AvailableReplicas)).Append(" available | ").Append(N(d.Status?.UnavailableReplicas)).Append(" unavailable\n");
            sb.Append("Strategy:     ").Append(d.Spec?.Strategy?.Type ?? "RollingUpdate").Append('\n');
            sb.Append("Revision:     ").Append(Annotation(d.Metadata, "deployment.kubernetes.io/revision") ?? "<none>").Append('\n');
            Template(sb, d.Spec?.Template);
            Conditions(sb, d.Status?.Conditions?.Select(c => c.Type + "=" + c.Status + (String.IsNullOrEmpty(c.Reason) ? String.Empty : " (" + c.Reason + ")") + (String.IsNullOrEmpty(c.Message) ? String.Empty : ": " + KubernetesFormat.Shorten(c.Message, 160))));
        }

        private static void DescribeStatefulSet(StringBuilder sb, V1StatefulSet s, DateTime now)
        {
            Header(sb, "StatefulSet", s.Metadata, now);
            sb.Append("Selector:     ").Append(KubernetesFormat.Map(s.Spec?.Selector?.MatchLabels)).Append('\n');
            sb.Append("Replicas:     ").Append(N(s.Spec?.Replicas)).Append(" desired | ").Append(N(s.Status?.Replicas)).Append(" total | ")
                .Append(N(s.Status?.ReadyReplicas)).Append(" ready | ").Append(N(s.Status?.UpdatedReplicas)).Append(" updated\n");
            sb.Append("Update Strategy: ").Append(s.Spec?.UpdateStrategy?.Type ?? "RollingUpdate").Append('\n');
            sb.Append("Revisions:    current ").Append(s.Status?.CurrentRevision ?? "<none>").Append(", update ").Append(s.Status?.UpdateRevision ?? "<none>").Append('\n');
            Template(sb, s.Spec?.Template);
            Conditions(sb, s.Status?.Conditions?.Select(c => c.Type + "=" + c.Status + (String.IsNullOrEmpty(c.Reason) ? String.Empty : " (" + c.Reason + ")")));
        }

        private static void DescribeDaemonSet(StringBuilder sb, V1DaemonSet d, DateTime now)
        {
            Header(sb, "DaemonSet", d.Metadata, now);
            sb.Append("Selector:     ").Append(KubernetesFormat.Map(d.Spec?.Selector?.MatchLabels)).Append('\n');
            sb.Append("Scheduled:    ").Append(N(d.Status?.CurrentNumberScheduled)).Append(" current | ").Append(N(d.Status?.DesiredNumberScheduled)).Append(" desired | ")
                .Append(N(d.Status?.NumberReady)).Append(" ready | ").Append(N(d.Status?.UpdatedNumberScheduled)).Append(" updated | ").Append(N(d.Status?.NumberAvailable)).Append(" available | ")
                .Append(N(d.Status?.NumberMisscheduled)).Append(" misscheduled\n");
            Template(sb, d.Spec?.Template);
            Conditions(sb, d.Status?.Conditions?.Select(c => c.Type + "=" + c.Status));
        }

        private static void DescribeReplicaSet(StringBuilder sb, V1ReplicaSet r, DateTime now)
        {
            Header(sb, "ReplicaSet", r.Metadata, now);
            sb.Append("Selector:     ").Append(KubernetesFormat.Map(r.Spec?.Selector?.MatchLabels)).Append('\n');
            sb.Append("Replicas:     ").Append(N(r.Status?.Replicas)).Append(" current / ").Append(N(r.Spec?.Replicas)).Append(" desired, ")
                .Append(N(r.Status?.ReadyReplicas)).Append(" ready, ").Append(N(r.Status?.AvailableReplicas)).Append(" available\n");
            Template(sb, r.Spec?.Template);
            Conditions(sb, r.Status?.Conditions?.Select(c => c.Type + "=" + c.Status));
        }

        private static void DescribeJob(StringBuilder sb, V1Job j, DateTime now)
        {
            Header(sb, "Job", j.Metadata, now);
            sb.Append("Completions:  ").Append(N(j.Spec?.Completions)).Append(", parallelism ").Append(N(j.Spec?.Parallelism)).Append(", backoffLimit ").Append(N(j.Spec?.BackoffLimit)).Append('\n');
            sb.Append("Pods:         ").Append(N(j.Status?.Active)).Append(" active | ").Append(N(j.Status?.Succeeded)).Append(" succeeded | ").Append(N(j.Status?.Failed)).Append(" failed\n");
            sb.Append("Start Time:   ").Append(Time(j.Status?.StartTime)).Append('\n');
            if (j.Status?.CompletionTime != null) sb.Append("Completed At: ").Append(Time(j.Status.CompletionTime)).Append('\n');
            Template(sb, j.Spec?.Template);
            Conditions(sb, j.Status?.Conditions?.Select(c => c.Type + "=" + c.Status + (String.IsNullOrEmpty(c.Reason) ? String.Empty : " (" + c.Reason + ")")));
        }

        private static void DescribeService(StringBuilder sb, V1Service s, DateTime now)
        {
            Header(sb, "Service", s.Metadata, now);
            sb.Append("Type:         ").Append(s.Spec?.Type ?? "ClusterIP").Append('\n');
            sb.Append("Selector:     ").Append(KubernetesFormat.Map(s.Spec?.Selector)).Append('\n');
            sb.Append("Cluster IP:   ").Append(s.Spec?.ClusterIP ?? "<none>").Append('\n');
            if (s.Spec?.ExternalIPs != null && s.Spec.ExternalIPs.Count > 0) sb.Append("External IPs: ").Append(String.Join(", ", s.Spec.ExternalIPs)).Append('\n');
            if (s.Status?.LoadBalancer?.Ingress != null && s.Status.LoadBalancer.Ingress.Count > 0)
                sb.Append("Load Balancer: ").Append(String.Join(", ", s.Status.LoadBalancer.Ingress.Select(i => i.Ip ?? i.Hostname))).Append('\n');
            foreach (V1ServicePort p in s.Spec?.Ports ?? new List<V1ServicePort>())
            {
                sb.Append("Port:         ").Append(String.IsNullOrEmpty(p.Name) ? "<unnamed>" : p.Name).Append(' ').Append(p.Port.ToString(CultureInfo.InvariantCulture)).Append('/').Append(p.Protocol ?? "TCP")
                    .Append(" -> ").Append(p.TargetPort?.Value ?? p.Port.ToString(CultureInfo.InvariantCulture));
                if (p.NodePort.HasValue) sb.Append(" (node port ").Append(p.NodePort.Value.ToString(CultureInfo.InvariantCulture)).Append(')');
                sb.Append('\n');
            }
        }

        private static void DescribeNode(StringBuilder sb, V1Node n, DateTime now)
        {
            Header(sb, "Node", n.Metadata, now);
            sb.Append("Unschedulable: ").Append(n.Spec?.Unschedulable == true ? "true (cordoned)" : "false").Append('\n');
            if (n.Spec?.Taints != null && n.Spec.Taints.Count > 0) sb.Append("Taints:       ").Append(String.Join(", ", n.Spec.Taints.Select(t => t.Key + (String.IsNullOrEmpty(t.Value) ? String.Empty : "=" + t.Value) + ":" + t.Effect))).Append('\n');
            else sb.Append("Taints:       <none>\n");
            if (n.Status?.Addresses != null) sb.Append("Addresses:    ").Append(String.Join(", ", n.Status.Addresses.Select(a => a.Type + "=" + a.Address))).Append('\n');
            if (n.Status?.NodeInfo != null)
                sb.Append("System:       kubelet ").Append(n.Status.NodeInfo.KubeletVersion).Append(", ").Append(n.Status.NodeInfo.OsImage).Append(", ").Append(n.Status.NodeInfo.ContainerRuntimeVersion).Append(", ").Append(n.Status.NodeInfo.Architecture).Append('\n');
            sb.Append("Capacity:     ").Append(Quantities(n.Status?.Capacity)).Append('\n');
            sb.Append("Allocatable:  ").Append(Quantities(n.Status?.Allocatable)).Append('\n');
            Conditions(sb, n.Status?.Conditions?.Select(c => c.Type + "=" + c.Status + (String.IsNullOrEmpty(c.Reason) ? String.Empty : " (" + c.Reason + ")")));
        }

        private static void DescribeConfigMap(StringBuilder sb, V1ConfigMap c, DateTime now)
        {
            Header(sb, "ConfigMap", c.Metadata, now);
            sb.Append("Data:\n");
            foreach (KeyValuePair<string, string> kv in (c.Data ?? new Dictionary<string, string>()).OrderBy(kv => kv.Key, StringComparer.Ordinal))
                sb.Append("  ").Append(kv.Key).Append(": ").Append(Encoding.UTF8.GetByteCount(kv.Value ?? String.Empty).ToString(CultureInfo.InvariantCulture)).Append(" bytes\n");
            foreach (KeyValuePair<string, byte[]> kv in (c.BinaryData ?? new Dictionary<string, byte[]>()).OrderBy(kv => kv.Key, StringComparer.Ordinal))
                sb.Append("  ").Append(kv.Key).Append(": ").Append((kv.Value?.Length ?? 0).ToString(CultureInfo.InvariantCulture)).Append(" bytes (binary)\n");
            sb.Append("Use k8s_get to read the values.\n");
        }

        private static void DescribeSecret(StringBuilder sb, V1Secret s, DateTime now)
        {
            Header(sb, "Secret", s.Metadata, now);
            sb.Append("Type:         ").Append(s.Type ?? "Opaque").Append('\n');
            sb.Append("Data (values are never shown):\n");
            foreach (KeyValuePair<string, byte[]> kv in (s.Data ?? new Dictionary<string, byte[]>()).OrderBy(kv => kv.Key, StringComparer.Ordinal))
                sb.Append("  ").Append(kv.Key).Append(": ").Append((kv.Value?.Length ?? 0).ToString(CultureInfo.InvariantCulture)).Append(" bytes\n");
        }

        private static void DescribeGeneric(StringBuilder sb, KubernetesResourceType type, JsonNode node, DateTime now)
        {
            KubernetesObjectSanitizer.Sanitize(node, type.Kind);
            JsonObject? metadata = node["metadata"] as JsonObject;
            V1ObjectMeta meta = new V1ObjectMeta();
            meta.Name = KubernetesManifestParser.StringOf(metadata?["name"]);
            meta.NamespaceProperty = KubernetesManifestParser.StringOf(metadata?["namespace"]);
            string? created = KubernetesManifestParser.StringOf(metadata?["creationTimestamp"]);
            if (created != null && DateTime.TryParse(created, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime createdAt)) meta.CreationTimestamp = createdAt;
            meta.Labels = StringMap(metadata?["labels"]);
            meta.Annotations = StringMap(metadata?["annotations"]);
            if (metadata?["ownerReferences"] is JsonArray owners)
            {
                meta.OwnerReferences = owners.OfType<JsonObject>()
                    .Select(o => new V1OwnerReference { Kind = KubernetesManifestParser.StringOf(o["kind"]), Name = KubernetesManifestParser.StringOf(o["name"]) })
                    .ToList();
            }
            Header(sb, type.Kind + " (" + type.ApiVersion + ")", meta, now);

            JsonObject? status = node["status"] as JsonObject;
            string? phase = KubernetesManifestParser.StringOf(status?["phase"]);
            if (phase != null) sb.Append("Phase:        ").Append(phase).Append('\n');
            if (status?["conditions"] is JsonArray conditions)
            {
                Conditions(sb, conditions.OfType<JsonObject>().Select(c => KubernetesManifestParser.StringOf(c["type"]) + "=" + KubernetesManifestParser.StringOf(c["status"])
                    + (KubernetesManifestParser.StringOf(c["reason"]) is string r && r.Length > 0 ? " (" + r + ")" : String.Empty)
                    + (KubernetesManifestParser.StringOf(c["message"]) is string m && m.Length > 0 ? ": " + KubernetesFormat.Shorten(m, 160) : String.Empty)));
            }
            if (node["spec"] is JsonObject spec)
            {
                sb.Append("Spec:\n");
                foreach (string line in KubernetesYamlWriter.Write(spec).Split('\n').Take(40))
                {
                    if (line.Length > 0) sb.Append("  ").Append(line).Append('\n');
                }
            }
        }

        private static void Template(StringBuilder sb, V1PodTemplateSpec? template)
        {
            if (template?.Spec?.Containers == null) return;
            sb.Append("Pod Template:\n");
            foreach (V1Container c in template.Spec.Containers)
            {
                sb.Append("  ").Append(c.Name).Append(": ").Append(c.Image);
                string resources = Resources(c.Resources);
                if (resources.Length > 0) sb.Append(" (").Append(resources).Append(')');
                sb.Append('\n');
            }
        }

        private static void Conditions(StringBuilder sb, IEnumerable<string>? conditions)
        {
            List<string> list = conditions?.ToList() ?? new List<string>();
            if (list.Count == 0) return;
            sb.Append("Conditions:\n");
            foreach (string c in list) sb.Append("  ").Append(c).Append('\n');
        }

        private static string State(V1ContainerState? state, DateTime now)
        {
            if (state == null) return "Unknown";
            if (state.Running != null) return "Running since " + Time(state.Running.StartedAt) + " (" + KubernetesFormat.Age(state.Running.StartedAt, now) + ")";
            if (state.Waiting != null) return "Waiting: " + (state.Waiting.Reason ?? "unknown") + (String.IsNullOrEmpty(state.Waiting.Message) ? String.Empty : " (" + KubernetesFormat.Shorten(state.Waiting.Message, 200) + ")");
            if (state.Terminated != null)
                return "Terminated: " + (state.Terminated.Reason ?? "unknown") + ", exit code " + state.Terminated.ExitCode.ToString(CultureInfo.InvariantCulture)
                    + (state.Terminated.FinishedAt.HasValue ? ", finished " + Time(state.Terminated.FinishedAt) : String.Empty);
            return "Unknown";
        }

        private static string Resources(V1ResourceRequirements? r)
        {
            List<string> parts = new List<string>();
            if (r?.Requests != null && r.Requests.Count > 0) parts.Add("requests " + Quantities(r.Requests));
            if (r?.Limits != null && r.Limits.Count > 0) parts.Add("limits " + Quantities(r.Limits));
            return String.Join("; ", parts);
        }

        private static string Quantities(IDictionary<string, ResourceQuantity>? q)
        {
            if (q == null || q.Count == 0) return "<none>";
            return String.Join(", ", q.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value));
        }

        private static string? Annotation(V1ObjectMeta? meta, string key)
        {
            if (meta?.Annotations == null) return null;
            return meta.Annotations.TryGetValue(key, out string? value) ? value : null;
        }

        private static Dictionary<string, string> StringMap(JsonNode? node)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (node is JsonObject obj)
            {
                foreach (KeyValuePair<string, JsonNode?> kv in obj) map[kv.Key] = KubernetesManifestParser.StringOf(kv.Value) ?? kv.Value?.ToJsonString() ?? String.Empty;
            }
            return map;
        }

        private static string N(int? value)
        {
            return (value ?? 0).ToString(CultureInfo.InvariantCulture);
        }

        private static string Time(DateTime? value)
        {
            return value.HasValue ? value.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) : "<unknown>";
        }

        #endregion
    }
}
