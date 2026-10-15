namespace Test.Shared.Connectors.Kubernetes
{
    using System.Collections.Generic;

    /// <summary>
    /// Classification table for <c>k8s_apply</c> manifests: Write by default, Destructive for destructive kinds, explicit
    /// nulls, force, field removal, and unparseable input, Read for dry runs, and Rejected for namespace or cluster-scope
    /// policy violations.
    /// </summary>
    public static class KubernetesManifestTable
    {
        #region Public-Methods

        /// <summary>
        /// The cases.
        /// </summary>
        /// <returns>Cases.</returns>
        public static List<KubernetesManifestCase> Cases()
        {
            string deployment = "apiVersion: apps/v1\nkind: Deployment\nmetadata:\n  name: web\nspec:\n  replicas: 2\n";
            string configMap = "apiVersion: v1\nkind: ConfigMap\nmetadata:\n  name: settings\ndata:\n  a: b\n";
            string role = "apiVersion: rbac.authorization.k8s.io/v1\nkind: Role\nmetadata:\n  name: reader\nrules: []\n";
            string clusterRole = "apiVersion: rbac.authorization.k8s.io/v1\nkind: ClusterRole\nmetadata:\n  name: reader\nrules: []\n";
            string ns = "apiVersion: v1\nkind: Namespace\nmetadata:\n  name: hugo-test\n";
            string crd = "apiVersion: apiextensions.k8s.io/v1\nkind: CustomResourceDefinition\nmetadata:\n  name: widgets.example.com\nspec: {}\n";
            string widget = "apiVersion: example.com/v1\nkind: Widget\nmetadata:\n  name: w1\nspec:\n  color: blue\n";
            Dictionary<string, string> restricted = new Dictionary<string, string> { { "allowedNamespaces", "hugo-test" } };
            Dictionary<string, string> open = new Dictionary<string, string>();
            Dictionary<string, string> cluster = new Dictionary<string, string> { { "allowClusterScoped", "true" } };
            Dictionary<string, string> restrictedCluster = new Dictionary<string, string> { { "allowedNamespaces", "hugo-test" }, { "allowClusterScoped", "true" } };
            Dictionary<string, string> customKinds = new Dictionary<string, string> { { "destructiveKinds", "ConfigMap\nWidget" } };

            return new List<KubernetesManifestCase>
            {
                Case("W", "deployment in the default namespace", deployment, open),
                Case("W", "configmap", configMap, open),
                Case("W", "deployment in an allowed namespace", deployment, restricted),
                Case("W", "explicit allowed namespace in the manifest", deployment.Replace("  name: web\n", "  name: web\n  namespace: hugo-test\n"), restricted),
                Case("W", "namespace argument matches the allow list", deployment, restricted, "hugo-test"),
                Case("X", "namespace argument outside the allow list", deployment, restricted, "kube-system"),
                Case("X", "manifest namespace outside the allow list", deployment.Replace("  name: web\n", "  name: web\n  namespace: kube-system\n"), restricted),
                Case("X", "manifest namespace differs from the argument", deployment.Replace("  name: web\n", "  name: web\n  namespace: other\n"), open, "hugo-test"),
                Case("D", "Role is a destructive kind", role, open),
                Case("X", "ClusterRole without allowClusterScoped", clusterRole, open),
                Case("D", "ClusterRole with allowClusterScoped", clusterRole, cluster),
                Case("X", "Namespace without allowClusterScoped", ns, open),
                Case("D", "Namespace with allowClusterScoped", ns, cluster),
                Case("D", "Namespace in the allow list with allowClusterScoped", ns, restrictedCluster),
                Case("X", "Namespace outside the allow list", ns.Replace("hugo-test", "kube-system"), restrictedCluster),
                Case("X", "CRD without allowClusterScoped", crd, open),
                Case("D", "CRD with allowClusterScoped", crd, cluster),
                Case("W", "custom resource of an unknown kind", widget, open),
                Case("D", "custom destructive kinds include ConfigMap", configMap, customKinds),
                Case("D", "custom destructive kinds include a custom resource", widget, customKinds),
                Case("W", "custom destructive kinds drop Role", role, customKinds),
                Case("D", "explicit null removes a field", "apiVersion: apps/v1\nkind: Deployment\nmetadata:\n  name: web\nspec:\n  replicas: null\n", open),
                Case("D", "empty value is null", "apiVersion: v1\nkind: ConfigMap\nmetadata:\n  name: settings\ndata:\n  a:\n", open),
                Case("W", "quoted null is a string", "apiVersion: v1\nkind: ConfigMap\nmetadata:\n  name: settings\ndata:\n  a: \"null\"\n", open),
                Case("D", "force takes over fields", deployment, open, null, false, true),
                Case("D", "allowFieldRemoval", deployment, open, null, false, false, true),
                Case("R", "dry run", deployment, open, null, true),
                Case("R", "dry run of a destructive kind", role, open, null, true),
                Case("X", "dry run outside the allow list is still rejected", deployment, restricted, "kube-system", true),
                Case("R", "dry run of a cluster-scoped kind only needs reads", clusterRole, open, null, true),
                Case("D", "unparseable YAML fails closed", "kind: [unclosed", open),
                Case("D", "empty manifest fails closed", "   ", open),
                Case("D", "missing name fails closed", "apiVersion: v1\nkind: ConfigMap\nmetadata: {}\n", open),
                Case("D", "missing kind fails closed", "apiVersion: v1\nmetadata:\n  name: x\n", open),
                Case("D", "worst document wins", configMap + "---\n" + role, open),
                Case("X", "any rejected document rejects the call", configMap + "---\n" + clusterRole, open),
                Case("W", "JSON manifest", "{\"apiVersion\":\"v1\",\"kind\":\"ConfigMap\",\"metadata\":{\"name\":\"j\"},\"data\":{\"a\":\"b\"}}", open),
                Case("D", "JSON with null", "{\"apiVersion\":\"v1\",\"kind\":\"ConfigMap\",\"metadata\":{\"name\":\"j\"},\"data\":null}", open),
                Case("W", "List kind is expanded", "apiVersion: v1\nkind: List\nitems:\n  - apiVersion: v1\n    kind: ConfigMap\n    metadata:\n      name: a\n  - apiVersion: v1\n    kind: ConfigMap\n    metadata:\n      name: b\n", open),
                Case("D", "List containing a Role", "apiVersion: v1\nkind: List\nitems:\n  - apiVersion: rbac.authorization.k8s.io/v1\n    kind: Role\n    metadata:\n      name: a\n", open),
                Case("D", "YAML merge keys are refused", "apiVersion: v1\nkind: ConfigMap\nmetadata:\n  name: a\ndata:\n  <<: {x: y}\n", open),
                Case("D", "ServiceAccount is a destructive kind", "apiVersion: v1\nkind: ServiceAccount\nmetadata:\n  name: sa\n", open),
                Case("D", "PersistentVolumeClaim is a destructive kind", "apiVersion: v1\nkind: PersistentVolumeClaim\nmetadata:\n  name: data\nspec: {}\n", open),
                Case("X", "PersistentVolume without allowClusterScoped", "apiVersion: v1\nkind: PersistentVolume\nmetadata:\n  name: pv\nspec: {}\n", open),
                Case("W", "Service", "apiVersion: v1\nkind: Service\nmetadata:\n  name: web\nspec:\n  ports:\n    - port: 80\n", open),
                Case("W", "multi-document workload", deployment + "---\napiVersion: v1\nkind: Service\nmetadata:\n  name: web\n---\n" + configMap, restricted)
            };
        }

        #endregion

        #region Private-Methods

        private static KubernetesManifestCase Case(string expected, string name, string manifest, Dictionary<string, string> settings, string? ns = null, bool dryRun = false, bool force = false, bool allowFieldRemoval = false)
        {
            return new KubernetesManifestCase
            {
                Expected = expected,
                Name = name,
                Manifest = manifest,
                Settings = new Dictionary<string, string>(settings),
                Namespace = ns,
                DryRun = dryRun,
                Force = force,
                AllowFieldRemoval = allowFieldRemoval
            };
        }

        #endregion
    }
}
