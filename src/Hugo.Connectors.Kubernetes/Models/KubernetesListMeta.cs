namespace Hugo.Connectors.Kubernetes.Models
{
    /// <summary>
    /// List metadata: the continue token and the remaining item count. Not thread-safe.
    /// </summary>
    public class KubernetesListMeta
    {
        /// <summary>
        /// Continue token for the next page, or null.
        /// </summary>
        public string? Continue { get; set; } = null;

        /// <summary>
        /// Approximate number of items after this page, or null.
        /// </summary>
        public long? RemainingItemCount { get; set; } = null;
    }
}
