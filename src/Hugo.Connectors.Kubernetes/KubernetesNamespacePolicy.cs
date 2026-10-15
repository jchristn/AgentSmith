namespace Hugo.Connectors.Kubernetes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Enforces a resource's namespace allow list and cluster-scoped policy. Every tool calls it before contacting the
    /// cluster, and classifiers call the <c>Explain</c> methods to reject calls early. Thread-safe.
    /// </summary>
    public static class KubernetesNamespacePolicy
    {
        #region Public-Methods

        /// <summary>
        /// Whether a namespace is allowed (always true when the allow list is empty).
        /// </summary>
        /// <param name="options">Options.</param>
        /// <param name="ns">Namespace.</param>
        /// <returns>True when allowed.</returns>
        public static bool IsAllowed(KubernetesOptions options, string? ns)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (options.AllowedNamespaces.Count == 0) return true;
            if (String.IsNullOrEmpty(ns)) return false;
            return options.AllowedNamespaces.Contains(ns, StringComparer.Ordinal);
        }

        /// <summary>
        /// Resolve the namespace for a namespaced call: the requested one, else <c>defaultNamespace</c>, else the first
        /// allowed namespace, else <c>default</c>.
        /// </summary>
        /// <param name="options">Options.</param>
        /// <param name="requested">Requested namespace, or null.</param>
        /// <returns>Namespace.</returns>
        public static string Effective(KubernetesOptions options, string? requested)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (!String.IsNullOrWhiteSpace(requested)) return requested.Trim();
            if (!String.IsNullOrEmpty(options.DefaultNamespace)) return options.DefaultNamespace;
            if (options.AllowedNamespaces.Count > 0) return options.AllowedNamespaces[0];
            return "default";
        }

        /// <summary>
        /// Resolve and check the namespace for a namespaced call.
        /// </summary>
        /// <param name="options">Options.</param>
        /// <param name="requested">Requested namespace, or null.</param>
        /// <returns>Allowed namespace.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>permission_denied</c> when the namespace is not allowed.</exception>
        public static string Require(KubernetesOptions options, string? requested)
        {
            string ns = Effective(options, requested);
            if (!IsAllowed(options, ns)) throw Denied(options, ns);
            return ns;
        }

        /// <summary>
        /// Namespaces a list call should cover when the caller asks for all namespaces: null means one cluster-wide call
        /// (allow list empty); otherwise one call per allowed namespace.
        /// </summary>
        /// <param name="options">Options.</param>
        /// <returns>Namespaces, or null for a cluster-wide call.</returns>
        public static List<string>? AllNamespaces(KubernetesOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (options.AllowedNamespaces.Count == 0) return null;
            return new List<string>(options.AllowedNamespaces);
        }

        /// <summary>
        /// Check access to a cluster-scoped type.
        /// </summary>
        /// <param name="options">Options.</param>
        /// <param name="type">Resource type.</param>
        /// <param name="write">True for a write.</param>
        /// <exception cref="ConnectorException">Thrown with <c>permission_denied</c> when the policy forbids it.</exception>
        public static void RequireClusterScoped(KubernetesOptions options, KubernetesResourceType type, bool write)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(type);
            string? reason = ExplainClusterScoped(options, type.Kind, write);
            if (reason != null) throw new ConnectorException(ConnectorErrorCodes.PermissionDenied, reason);
        }

        /// <summary>
        /// Explain why a call on a cluster-scoped kind is not allowed, or return null when it is.
        /// </summary>
        /// <param name="options">Options.</param>
        /// <param name="kind">Kind.</param>
        /// <param name="write">True for a write.</param>
        /// <returns>Reason, or null.</returns>
        public static string? ExplainClusterScoped(KubernetesOptions options, string kind, bool write)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (write && !options.AllowClusterScoped)
                return "Changing the cluster-scoped kind " + kind + " is not allowed on this resource (allowClusterScoped is false).";
            if (!write && !options.ClusterScopedReads)
                return "Reading the cluster-scoped kind " + kind + " is not allowed on this resource (clusterScopedReads is false).";
            return null;
        }

        /// <summary>
        /// Explain why a namespace is not allowed, or return null when it is.
        /// </summary>
        /// <param name="options">Options.</param>
        /// <param name="ns">Namespace.</param>
        /// <returns>Reason, or null.</returns>
        public static string? ExplainNamespace(KubernetesOptions options, string? ns)
        {
            if (IsAllowed(options, ns)) return null;
            return Denied(options, ns ?? String.Empty).Message;
        }

        /// <summary>
        /// Build the standard denial for a namespace outside the allow list.
        /// </summary>
        /// <param name="options">Options.</param>
        /// <param name="ns">Namespace.</param>
        /// <returns>Exception with code <c>permission_denied</c>.</returns>
        public static ConnectorException Denied(KubernetesOptions options, string ns)
        {
            ArgumentNullException.ThrowIfNull(options);
            return new ConnectorException(ConnectorErrorCodes.PermissionDenied,
                "Namespace '" + ns + "' is not in this resource's allowed namespaces (" + String.Join(", ", options.AllowedNamespaces) + ").");
        }

        #endregion
    }
}
