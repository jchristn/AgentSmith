namespace Hugo.Connectors.Kubernetes.Models
{
    /// <summary>
    /// Partial object metadata attached to a table row. Not thread-safe.
    /// </summary>
    public class KubernetesPartialObject
    {
        /// <summary>
        /// Metadata. May be null.
        /// </summary>
        public KubernetesObjectMeta? Metadata { get; set; } = null;
    }
}
