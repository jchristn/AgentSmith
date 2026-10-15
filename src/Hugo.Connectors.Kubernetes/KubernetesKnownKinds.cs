namespace Hugo.Connectors.Kubernetes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Built-in table of well-known Kubernetes kinds. Classification uses it without contacting the cluster (to know
    /// whether a kind is cluster-scoped), and discovery falls back to it when the server's discovery documents are not
    /// readable. Thread-safe.
    /// </summary>
    public static class KubernetesKnownKinds
    {
        #region Public-Members

        /// <summary>
        /// Every well-known kind.
        /// </summary>
        public static IReadOnlyList<KubernetesResourceType> All { get; } = Build();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Find a well-known kind by name (kind, plural, singular, or short name), optionally constrained by apiVersion.
        /// </summary>
        /// <param name="name">Name, for example <c>deploy</c> or <c>Deployment</c>.</param>
        /// <param name="apiVersion">apiVersion such as <c>apps/v1</c>, or null.</param>
        /// <returns>A copy of the type, or null when unknown.</returns>
        public static KubernetesResourceType? Find(string? name, string? apiVersion = null)
        {
            if (String.IsNullOrWhiteSpace(name)) return null;
            KubernetesResourceType? found = All.FirstOrDefault(t => t.Matches(name) && (String.IsNullOrEmpty(apiVersion) || String.Equals(t.ApiVersion, apiVersion, StringComparison.OrdinalIgnoreCase) || String.Equals(t.Group, GroupOf(apiVersion), StringComparison.OrdinalIgnoreCase)));
            if (found == null) return null;
            KubernetesResourceType copy = found.Clone();
            if (!String.IsNullOrEmpty(apiVersion) && String.Equals(copy.Group, GroupOf(apiVersion), StringComparison.OrdinalIgnoreCase)) copy.Version = VersionOf(apiVersion);
            return copy;
        }

        /// <summary>
        /// Whether a kind is known to be cluster-scoped. Unknown kinds return false.
        /// </summary>
        /// <param name="name">Kind or resource name.</param>
        /// <param name="apiVersion">apiVersion, or null.</param>
        /// <returns>True when the kind is a known cluster-scoped kind.</returns>
        public static bool IsClusterScoped(string? name, string? apiVersion = null)
        {
            KubernetesResourceType? t = Find(name, apiVersion);
            return t != null && !t.Namespaced;
        }

        /// <summary>
        /// The group part of an apiVersion (<c>apps/v1</c> gives <c>apps</c>; <c>v1</c> gives the empty core group).
        /// </summary>
        /// <param name="apiVersion">apiVersion.</param>
        /// <returns>Group.</returns>
        public static string GroupOf(string? apiVersion)
        {
            if (String.IsNullOrEmpty(apiVersion)) return String.Empty;
            int slash = apiVersion.IndexOf('/');
            return slash < 0 ? String.Empty : apiVersion.Substring(0, slash);
        }

        /// <summary>
        /// The version part of an apiVersion (<c>apps/v1</c> gives <c>v1</c>).
        /// </summary>
        /// <param name="apiVersion">apiVersion.</param>
        /// <returns>Version.</returns>
        public static string VersionOf(string? apiVersion)
        {
            if (String.IsNullOrEmpty(apiVersion)) return "v1";
            int slash = apiVersion.IndexOf('/');
            return slash < 0 ? apiVersion : apiVersion.Substring(slash + 1);
        }

        #endregion

        #region Private-Methods

        private static List<KubernetesResourceType> Build()
        {
            List<KubernetesResourceType> list = new List<KubernetesResourceType>();
            Add(list, "", "v1", "Pod", "pods", true, "po");
            Add(list, "", "v1", "Service", "services", true, "svc");
            Add(list, "", "v1", "ConfigMap", "configmaps", true, "cm");
            Add(list, "", "v1", "Secret", "secrets", true);
            Add(list, "", "v1", "ServiceAccount", "serviceaccounts", true, "sa");
            Add(list, "", "v1", "Endpoints", "endpoints", true, "ep");
            Add(list, "", "v1", "Event", "events", true, "ev");
            Add(list, "", "v1", "PersistentVolumeClaim", "persistentvolumeclaims", true, "pvc");
            Add(list, "", "v1", "ReplicationController", "replicationcontrollers", true, "rc");
            Add(list, "", "v1", "ResourceQuota", "resourcequotas", true, "quota");
            Add(list, "", "v1", "LimitRange", "limitranges", true, "limits");
            Add(list, "", "v1", "PodTemplate", "podtemplates", true);
            Add(list, "", "v1", "Namespace", "namespaces", false, "ns");
            Add(list, "", "v1", "Node", "nodes", false, "no");
            Add(list, "", "v1", "PersistentVolume", "persistentvolumes", false, "pv");
            Add(list, "", "v1", "ComponentStatus", "componentstatuses", false, "cs");
            Add(list, "apps", "v1", "Deployment", "deployments", true, "deploy");
            Add(list, "apps", "v1", "StatefulSet", "statefulsets", true, "sts");
            Add(list, "apps", "v1", "DaemonSet", "daemonsets", true, "ds");
            Add(list, "apps", "v1", "ReplicaSet", "replicasets", true, "rs");
            Add(list, "apps", "v1", "ControllerRevision", "controllerrevisions", true);
            Add(list, "batch", "v1", "Job", "jobs", true);
            Add(list, "batch", "v1", "CronJob", "cronjobs", true, "cj");
            Add(list, "autoscaling", "v2", "HorizontalPodAutoscaler", "horizontalpodautoscalers", true, "hpa");
            Add(list, "policy", "v1", "PodDisruptionBudget", "poddisruptionbudgets", true, "pdb");
            Add(list, "networking.k8s.io", "v1", "Ingress", "ingresses", true, "ing");
            Add(list, "networking.k8s.io", "v1", "NetworkPolicy", "networkpolicies", true, "netpol");
            Add(list, "networking.k8s.io", "v1", "IngressClass", "ingressclasses", false);
            Add(list, "discovery.k8s.io", "v1", "EndpointSlice", "endpointslices", true);
            Add(list, "events.k8s.io", "v1", "Event", "events", true, "ev");
            Add(list, "coordination.k8s.io", "v1", "Lease", "leases", true);
            Add(list, "rbac.authorization.k8s.io", "v1", "Role", "roles", true);
            Add(list, "rbac.authorization.k8s.io", "v1", "RoleBinding", "rolebindings", true);
            Add(list, "rbac.authorization.k8s.io", "v1", "ClusterRole", "clusterroles", false);
            Add(list, "rbac.authorization.k8s.io", "v1", "ClusterRoleBinding", "clusterrolebindings", false);
            Add(list, "storage.k8s.io", "v1", "StorageClass", "storageclasses", false, "sc");
            Add(list, "storage.k8s.io", "v1", "CSIDriver", "csidrivers", false);
            Add(list, "storage.k8s.io", "v1", "CSINode", "csinodes", false);
            Add(list, "storage.k8s.io", "v1", "VolumeAttachment", "volumeattachments", false);
            Add(list, "scheduling.k8s.io", "v1", "PriorityClass", "priorityclasses", false, "pc");
            Add(list, "apiextensions.k8s.io", "v1", "CustomResourceDefinition", "customresourcedefinitions", false, "crd", "crds");
            Add(list, "apiregistration.k8s.io", "v1", "APIService", "apiservices", false);
            Add(list, "admissionregistration.k8s.io", "v1", "MutatingWebhookConfiguration", "mutatingwebhookconfigurations", false);
            Add(list, "admissionregistration.k8s.io", "v1", "ValidatingWebhookConfiguration", "validatingwebhookconfigurations", false);
            Add(list, "admissionregistration.k8s.io", "v1", "ValidatingAdmissionPolicy", "validatingadmissionpolicies", false);
            Add(list, "admissionregistration.k8s.io", "v1", "ValidatingAdmissionPolicyBinding", "validatingadmissionpolicybindings", false);
            Add(list, "certificates.k8s.io", "v1", "CertificateSigningRequest", "certificatesigningrequests", false, "csr");
            Add(list, "node.k8s.io", "v1", "RuntimeClass", "runtimeclasses", false);
            Add(list, "flowcontrol.apiserver.k8s.io", "v1", "FlowSchema", "flowschemas", false);
            Add(list, "flowcontrol.apiserver.k8s.io", "v1", "PriorityLevelConfiguration", "prioritylevelconfigurations", false);
            return list;
        }

        private static void Add(List<KubernetesResourceType> list, string group, string version, string kind, string plural, bool namespaced, params string[] shortNames)
        {
            KubernetesResourceType t = new KubernetesResourceType();
            t.Group = group;
            t.Version = version;
            t.Kind = kind;
            t.Plural = plural;
            t.Singular = kind.ToLowerInvariant();
            t.Namespaced = namespaced;
            t.ShortNames = new List<string>(shortNames);
            if (plural == "deployments" || plural == "statefulsets" || plural == "replicasets" || plural == "replicationcontrollers") t.Subresources.Add("scale");
            list.Add(t);
        }

        #endregion
    }
}
