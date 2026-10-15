namespace Hugo.Connectors.Kubernetes
{
    /// <summary>
    /// Setting keys, secret slot names, and secret field names of the <c>kubernetes</c> connector. These are part of the
    /// resource contract and are not configurable. Thread-safe.
    /// </summary>
    public static class KubernetesSettingKeys
    {
        /// <summary>Kubeconfig context to use. Empty means the kubeconfig's current-context.</summary>
        public const string Context = "context";

        /// <summary>API server URL. Required with token authentication; overrides the kubeconfig server when set.</summary>
        public const string ServerUrl = "serverUrl";

        /// <summary>Namespaces the resource may touch, one per line. Empty means all namespaces.</summary>
        public const string AllowedNamespaces = "allowedNamespaces";

        /// <summary>Namespace used when a call names none.</summary>
        public const string DefaultNamespace = "defaultNamespace";

        /// <summary>Whether writes to cluster-scoped kinds (nodes, namespaces, cluster roles, CRDs) are allowed.</summary>
        public const string AllowClusterScoped = "allowClusterScoped";

        /// <summary>Whether reads of cluster-scoped kinds (nodes, namespaces, CRDs) are allowed.</summary>
        public const string ClusterScopedReads = "clusterScopedReads";

        /// <summary>Kinds whose apply is always Destructive, one per line.</summary>
        public const string DestructiveKinds = "destructiveKinds";

        /// <summary>Most log lines returned by <c>k8s_logs</c>.</summary>
        public const string MaxLogLines = "maxLogLines";

        /// <summary>Per-request timeout in seconds.</summary>
        public const string RequestTimeoutSeconds = "requestTimeoutSeconds";

        /// <summary>Most objects returned by one list call.</summary>
        public const string MaxObjects = "maxObjects";

        /// <summary>Default and maximum run time of <c>k8s_exec</c> in seconds.</summary>
        public const string ExecTimeoutSeconds = "execTimeoutSeconds";

        /// <summary>Whether to skip TLS verification of the API server (testing only).</summary>
        public const string InsecureSkipTlsVerify = "insecureSkipTlsVerify";

        /// <summary>Field manager name used for server-side apply and patches.</summary>
        public const string FieldManager = "fieldManager";

        /// <summary>Secret slot holding a Kubeconfig secret.</summary>
        public const string KubeconfigSlot = "kubeconfig";

        /// <summary>Secret slot holding an ApiToken secret (a service account bearer token).</summary>
        public const string ServiceAccountTokenSlot = "serviceAccountToken";

        /// <summary>Secret slot holding a Certificate secret with the cluster CA.</summary>
        public const string CertificateAuthoritySlot = "certificateAuthority";

        /// <summary>Field of a Kubeconfig secret holding the kubeconfig text.</summary>
        public const string KubeconfigField = "kubeconfig";

        /// <summary>Field of an ApiToken secret holding the token.</summary>
        public const string TokenField = "token";

        /// <summary>Field of a Certificate secret holding the PEM certificate.</summary>
        public const string CertificateField = "certificate";

        /// <summary>Optional field of a Certificate secret holding a CA certificate (PEM).</summary>
        public const string CaCertificateField = "caCertificate";
    }
}
