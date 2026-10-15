namespace Hugo.Connectors.Kubernetes
{
    using System;

    /// <summary>
    /// A resolved call target: the resource type and the namespace that passed the policy check (null for
    /// cluster-scoped types). Not thread-safe.
    /// </summary>
    public class KubernetesTarget
    {
        #region Public-Members

        /// <summary>
        /// Resource type.
        /// </summary>
        public KubernetesResourceType Type
        {
            get
            {
                return _Type;
            }
            set
            {
                _Type = value ?? throw new ArgumentNullException(nameof(Type));
            }
        }

        /// <summary>
        /// Namespace, or null for cluster-scoped types.
        /// </summary>
        public string? Namespace { get; set; } = null;

        #endregion

        #region Private-Members

        private KubernetesResourceType _Type = new KubernetesResourceType();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Label such as <c>deployments.apps hugo-test/web</c>.
        /// </summary>
        /// <param name="name">Object name, or null for the collection.</param>
        /// <returns>Label.</returns>
        public string Label(string? name)
        {
            string obj = String.IsNullOrEmpty(name) ? String.Empty : " " + KubernetesFormat.Ref(Namespace, name);
            if (String.IsNullOrEmpty(name) && !String.IsNullOrEmpty(Namespace)) obj = " in namespace " + Namespace;
            return _Type.DisplayName + obj;
        }

        #endregion
    }
}
