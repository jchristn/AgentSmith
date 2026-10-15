namespace Hugo.Connectors.Kubernetes
{
    using System;
    using System.Linq;
    using Hugo.Connectors.Abstractions;
    using k8s.Models;

    /// <summary>
    /// Rollout helpers: the workload kinds that roll out (Deployment, StatefulSet, DaemonSet) and the same status
    /// rules <c>kubectl rollout status</c> applies. Thread-safe.
    /// </summary>
    public static class KubernetesRollout
    {
        #region Public-Methods

        /// <summary>
        /// Resolve a rollout kind name to its apps/v1 type.
        /// </summary>
        /// <param name="kind">Kind name, or null for Deployment.</param>
        /// <returns>Type.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_argument</c> for other kinds.</exception>
        public static KubernetesResourceType Kind(string? kind)
        {
            KubernetesResourceType? t = KubernetesKnownKinds.Find(String.IsNullOrWhiteSpace(kind) ? "Deployment" : kind.Trim());
            if (t == null || !String.Equals(t.Group, "apps", StringComparison.Ordinal) || (t.Kind != "Deployment" && t.Kind != "StatefulSet" && t.Kind != "DaemonSet"))
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Rollouts apply to Deployment, StatefulSet, and DaemonSet, not '" + kind + "'.");
            return t;
        }

        /// <summary>
        /// Rollout status of a Deployment.
        /// </summary>
        /// <param name="d">Deployment.</param>
        /// <returns>State.</returns>
        public static KubernetesRolloutState Evaluate(V1Deployment d)
        {
            ArgumentNullException.ThrowIfNull(d);
            string name = d.Metadata?.Name ?? "?";
            if ((d.Metadata?.Generation ?? 0) > (d.Status?.ObservedGeneration ?? 0))
                return new KubernetesRolloutState(false, "Waiting for deployment \"" + name + "\" spec update to be observed...");
            V1DeploymentCondition? progressing = d.Status?.Conditions?.FirstOrDefault(c => c.Type == "Progressing");
            if (progressing != null && progressing.Reason == "ProgressDeadlineExceeded")
                return new KubernetesRolloutState(false, "deployment \"" + name + "\" exceeded its progress deadline") { Failed = true };
            int desired = d.Spec?.Replicas ?? 1;
            int updated = d.Status?.UpdatedReplicas ?? 0;
            int total = d.Status?.Replicas ?? 0;
            int available = d.Status?.AvailableReplicas ?? 0;
            if (updated < desired)
                return new KubernetesRolloutState(false, "Waiting for deployment \"" + name + "\" rollout to finish: " + updated + " out of " + desired + " new replicas have been updated...");
            if (total > updated)
                return new KubernetesRolloutState(false, "Waiting for deployment \"" + name + "\" rollout to finish: " + (total - updated) + " old replicas are pending termination...");
            if (available < updated)
                return new KubernetesRolloutState(false, "Waiting for deployment \"" + name + "\" rollout to finish: " + available + " of " + updated + " updated replicas are available...");
            return new KubernetesRolloutState(true, "deployment \"" + name + "\" successfully rolled out (" + available + "/" + desired + " available)");
        }

        /// <summary>
        /// Rollout status of a StatefulSet.
        /// </summary>
        /// <param name="s">StatefulSet.</param>
        /// <returns>State.</returns>
        public static KubernetesRolloutState Evaluate(V1StatefulSet s)
        {
            ArgumentNullException.ThrowIfNull(s);
            string name = s.Metadata?.Name ?? "?";
            if (String.Equals(s.Spec?.UpdateStrategy?.Type, "OnDelete", StringComparison.Ordinal))
                return new KubernetesRolloutState(true, "statefulset \"" + name + "\" uses the OnDelete update strategy, so rollout status is not tracked; pods update when deleted");
            if ((s.Metadata?.Generation ?? 0) > (s.Status?.ObservedGeneration ?? 0))
                return new KubernetesRolloutState(false, "Waiting for statefulset \"" + name + "\" spec update to be observed...");
            int desired = s.Spec?.Replicas ?? 1;
            int ready = s.Status?.ReadyReplicas ?? 0;
            if (ready < desired)
                return new KubernetesRolloutState(false, "Waiting for " + (desired - ready) + " pods to be ready for statefulset \"" + name + "\"...");
            int? partition = s.Spec?.UpdateStrategy?.RollingUpdate?.Partition;
            if (partition.HasValue && partition.Value > 0)
            {
                int updated = s.Status?.UpdatedReplicas ?? 0;
                if (updated < desired - partition.Value)
                    return new KubernetesRolloutState(false, "Waiting for partitioned roll out to finish: " + updated + " out of " + (desired - partition.Value) + " new pods have been updated...");
                return new KubernetesRolloutState(true, "partitioned roll out complete: " + updated + " new pods have been updated");
            }
            if (!String.Equals(s.Status?.UpdateRevision, s.Status?.CurrentRevision, StringComparison.Ordinal))
                return new KubernetesRolloutState(false, "Waiting for statefulset \"" + name + "\" rolling update to complete " + (s.Status?.UpdatedReplicas ?? 0) + " pods at revision " + s.Status?.UpdateRevision + "...");
            return new KubernetesRolloutState(true, "statefulset \"" + name + "\" rolling update complete " + ready + " pods at revision " + s.Status?.CurrentRevision);
        }

        /// <summary>
        /// Rollout status of a DaemonSet.
        /// </summary>
        /// <param name="d">DaemonSet.</param>
        /// <returns>State.</returns>
        public static KubernetesRolloutState Evaluate(V1DaemonSet d)
        {
            ArgumentNullException.ThrowIfNull(d);
            string name = d.Metadata?.Name ?? "?";
            if (String.Equals(d.Spec?.UpdateStrategy?.Type, "OnDelete", StringComparison.Ordinal))
                return new KubernetesRolloutState(true, "daemonset \"" + name + "\" uses the OnDelete update strategy, so rollout status is not tracked");
            if ((d.Metadata?.Generation ?? 0) > (d.Status?.ObservedGeneration ?? 0))
                return new KubernetesRolloutState(false, "Waiting for daemonset \"" + name + "\" spec update to be observed...");
            int desired = d.Status?.DesiredNumberScheduled ?? 0;
            int updated = d.Status?.UpdatedNumberScheduled ?? 0;
            int available = d.Status?.NumberAvailable ?? 0;
            if (updated < desired)
                return new KubernetesRolloutState(false, "Waiting for daemon set \"" + name + "\" rollout to finish: " + updated + " out of " + desired + " new pods have been updated...");
            if (available < desired)
                return new KubernetesRolloutState(false, "Waiting for daemon set \"" + name + "\" rollout to finish: " + available + " of " + desired + " updated pods are available...");
            return new KubernetesRolloutState(true, "daemon set \"" + name + "\" successfully rolled out");
        }

        #endregion
    }
}
