namespace Hugo.Connectors.Kubernetes.Models
{
    /// <summary>
    /// The metadata fields Hugo reads from table rows. Not thread-safe.
    /// </summary>
    public class KubernetesObjectMeta
    {
        /// <summary>
        /// Name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Namespace, or null for cluster-scoped objects.
        /// </summary>
        public string? Namespace { get; set; } = null;
    }
}
