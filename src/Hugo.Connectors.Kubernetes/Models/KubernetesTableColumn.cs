namespace Hugo.Connectors.Kubernetes.Models
{
    /// <summary>
    /// One column of a server-side table. Not thread-safe.
    /// </summary>
    public class KubernetesTableColumn
    {
        /// <summary>
        /// Column name, for example <c>Ready</c>.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Column type, for example <c>string</c> or <c>integer</c>.
        /// </summary>
        public string? Type { get; set; } = null;

        /// <summary>
        /// Priority: 0 for default columns, higher for wide-only columns.
        /// </summary>
        public int Priority { get; set; } = 0;
    }
}
