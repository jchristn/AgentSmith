namespace Hugo.Connectors.Kubernetes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Typed snapshot of a Kubernetes resource's settings, with defaults and range clamping applied. Not thread-safe for
    /// mutation; build one per call with <see cref="FromSettings"/>.
    /// </summary>
    public class KubernetesOptions
    {
        #region Public-Members

        /// <summary>
        /// Kubeconfig context, or null for the kubeconfig's current-context.
        /// </summary>
        public string? Context { get; set; } = null;

        /// <summary>
        /// API server URL, or null. Required for token authentication; overrides the kubeconfig server when set.
        /// </summary>
        public string? ServerUrl { get; set; } = null;

        /// <summary>
        /// Allowed namespaces. Empty means every namespace is allowed. Never null.
        /// </summary>
        public List<string> AllowedNamespaces
        {
            get
            {
                return _AllowedNamespaces;
            }
            set
            {
                _AllowedNamespaces = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Namespace used when a call names none, or null. When null, the first allowed namespace is used, then
        /// <c>default</c>.
        /// </summary>
        public string? DefaultNamespace { get; set; } = null;

        /// <summary>
        /// Whether writes to cluster-scoped kinds are allowed. Default false.
        /// </summary>
        public bool AllowClusterScoped { get; set; } = false;

        /// <summary>
        /// Whether reads of cluster-scoped kinds are allowed. Default true.
        /// </summary>
        public bool ClusterScopedReads { get; set; } = true;

        /// <summary>
        /// Kinds whose apply or delete is always Destructive (case-insensitive). Never null. <see cref="FromSettings"/>
        /// always includes <see cref="DefaultDestructiveKinds"/> and adds the resource's <c>destructiveKinds</c> to them;
        /// the setting can extend the list but never shrink it.
        /// </summary>
        public List<string> DestructiveKinds
        {
            get
            {
                return _DestructiveKinds;
            }
            set
            {
                _DestructiveKinds = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Most log lines returned by <c>k8s_logs</c>. Minimum 1, maximum 100000, default 500.
        /// </summary>
        public int MaxLogLines
        {
            get
            {
                return _MaxLogLines;
            }
            set
            {
                _MaxLogLines = Math.Clamp(value, 1, 100000);
            }
        }

        /// <summary>
        /// Per-request timeout in seconds. Minimum 1, maximum 600, default 30.
        /// </summary>
        public int RequestTimeoutSeconds
        {
            get
            {
                return _RequestTimeoutSeconds;
            }
            set
            {
                _RequestTimeoutSeconds = Math.Clamp(value, 1, 600);
            }
        }

        /// <summary>
        /// Most objects returned by one list call. Minimum 1, maximum 5000, default 200.
        /// </summary>
        public int MaxObjects
        {
            get
            {
                return _MaxObjects;
            }
            set
            {
                _MaxObjects = Math.Clamp(value, 1, 5000);
            }
        }

        /// <summary>
        /// Default and maximum run time of <c>k8s_exec</c> in seconds. Minimum 1, maximum 900, default 60.
        /// </summary>
        public int ExecTimeoutSeconds
        {
            get
            {
                return _ExecTimeoutSeconds;
            }
            set
            {
                _ExecTimeoutSeconds = Math.Clamp(value, 1, 900);
            }
        }

        /// <summary>
        /// Whether TLS verification of the API server is skipped. Default false. Use only for test clusters.
        /// </summary>
        public bool InsecureSkipTlsVerify { get; set; } = false;

        /// <summary>
        /// Field manager name for server-side apply and patches. Default <c>hugo</c>.
        /// </summary>
        public string FieldManager
        {
            get
            {
                return _FieldManager;
            }
            set
            {
                _FieldManager = String.IsNullOrWhiteSpace(value) ? "hugo" : value.Trim();
            }
        }

        /// <summary>
        /// Default destructive kinds: kinds whose apply or delete can break the cluster or widen access. Namespace, Node,
        /// CustomResourceDefinition, and APIService changes reach every workload (deleting one cascades to everything in
        /// it or served by it). The RBAC kinds and ServiceAccount grant or revoke access. The storage kinds hold or bind
        /// data, so a change can orphan or delete volumes. NetworkPolicy, ResourceQuota, LimitRange, and PriorityClass
        /// change what runs and what it can reach across a namespace. Admission webhooks and policies can block or
        /// rewrite every later API request.
        /// </summary>
        public static IReadOnlyList<string> DefaultDestructiveKinds { get; } = new List<string>
        {
            "Namespace",
            "Node",
            "CustomResourceDefinition",
            "APIService",
            "ClusterRole",
            "ClusterRoleBinding",
            "Role",
            "RoleBinding",
            "ServiceAccount",
            "PersistentVolume",
            "PersistentVolumeClaim",
            "StorageClass",
            "NetworkPolicy",
            "ResourceQuota",
            "LimitRange",
            "PriorityClass",
            "MutatingWebhookConfiguration",
            "ValidatingWebhookConfiguration",
            "ValidatingAdmissionPolicy",
            "ValidatingAdmissionPolicyBinding"
        };

        #endregion

        #region Private-Members

        private List<string> _AllowedNamespaces = new List<string>();
        private List<string> _DestructiveKinds = new List<string>(DefaultDestructiveKinds);
        private int _MaxLogLines = 500;
        private int _RequestTimeoutSeconds = 30;
        private int _MaxObjects = 200;
        private int _ExecTimeoutSeconds = 60;
        private string _FieldManager = "hugo";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build options from resource settings, applying defaults and clamping.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <returns>Options.</returns>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public static KubernetesOptions FromSettings(ResourceSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            KubernetesOptions o = new KubernetesOptions();
            o.Context = Trimmed(settings.GetString(KubernetesSettingKeys.Context));
            o.ServerUrl = Trimmed(settings.GetString(KubernetesSettingKeys.ServerUrl));
            o.AllowedNamespaces = settings.GetStringList(KubernetesSettingKeys.AllowedNamespaces)
                .SelectMany(s => s.Split(new char[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            o.DefaultNamespace = Trimmed(settings.GetString(KubernetesSettingKeys.DefaultNamespace));
            o.AllowClusterScoped = settings.GetBool(KubernetesSettingKeys.AllowClusterScoped, false);
            o.ClusterScopedReads = settings.GetBool(KubernetesSettingKeys.ClusterScopedReads, true);
            o.DestructiveKinds = DefaultDestructiveKinds
                .Concat(settings.GetStringList(KubernetesSettingKeys.DestructiveKinds)
                    .SelectMany(s => s.Split(new char[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            o.MaxLogLines = settings.GetInt(KubernetesSettingKeys.MaxLogLines, 500, 1, 100000);
            o.RequestTimeoutSeconds = settings.GetInt(KubernetesSettingKeys.RequestTimeoutSeconds, 30, 1, 600);
            o.MaxObjects = settings.GetInt(KubernetesSettingKeys.MaxObjects, 200, 1, 5000);
            o.ExecTimeoutSeconds = settings.GetInt(KubernetesSettingKeys.ExecTimeoutSeconds, 60, 1, 900);
            o.InsecureSkipTlsVerify = settings.GetBool(KubernetesSettingKeys.InsecureSkipTlsVerify, false);
            o.FieldManager = settings.GetString(KubernetesSettingKeys.FieldManager, "hugo") ?? "hugo";
            return o;
        }

        /// <summary>
        /// Whether a kind is in <see cref="DestructiveKinds"/> (case-insensitive).
        /// </summary>
        /// <param name="kind">Kind, for example <c>ClusterRole</c>.</param>
        /// <returns>True when listed.</returns>
        public bool IsDestructiveKind(string? kind)
        {
            if (String.IsNullOrEmpty(kind)) return false;
            return _DestructiveKinds.Any(k => String.Equals(k, kind, StringComparison.OrdinalIgnoreCase));
        }

        #endregion

        #region Private-Methods

        private static string? Trimmed(string? value)
        {
            if (value == null) return null;
            string t = value.Trim();
            return t.Length == 0 ? null : t;
        }

        #endregion
    }
}
