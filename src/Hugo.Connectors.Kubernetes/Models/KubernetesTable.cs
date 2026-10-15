namespace Hugo.Connectors.Kubernetes.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// The server-side printed table (<c>meta.k8s.io/v1 Table</c>) returned when a list asks for
    /// <c>application/json;as=Table</c>. It carries the same columns <c>kubectl get</c> prints, including a custom
    /// resource's additional printer columns. Not thread-safe.
    /// </summary>
    public class KubernetesTable
    {
        /// <summary>
        /// Kind, <c>Table</c>.
        /// </summary>
        public string? Kind { get; set; } = null;

        /// <summary>
        /// Column definitions. May be null.
        /// </summary>
        public List<KubernetesTableColumn>? ColumnDefinitions { get; set; } = null;

        /// <summary>
        /// Rows. May be null.
        /// </summary>
        public List<KubernetesTableRow>? Rows { get; set; } = null;

        /// <summary>
        /// List metadata (continue token). May be null.
        /// </summary>
        public KubernetesListMeta? Metadata { get; set; } = null;
    }
}
