namespace Hugo.Connectors.Kubernetes.Models
{
    using System.Collections.Generic;
    using System.Text.Json;

    /// <summary>
    /// One row of a server-side table. Cells are heterogeneous scalars (string, number, boolean), so they are kept as
    /// raw JSON values and converted to text for display. Not thread-safe.
    /// </summary>
    public class KubernetesTableRow
    {
        /// <summary>
        /// Cell values, one per column. May be null.
        /// </summary>
        public List<JsonElement>? Cells { get; set; } = null;

        /// <summary>
        /// Object metadata (requested with <c>includeObject=Metadata</c>). May be null.
        /// </summary>
        public KubernetesPartialObject? Object { get; set; } = null;
    }
}
