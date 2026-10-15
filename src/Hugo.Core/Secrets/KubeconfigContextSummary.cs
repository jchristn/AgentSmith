namespace Hugo.Core.Secrets
{
    using System;

    /// <summary>
    /// One context of an imported kubeconfig.
    /// </summary>
    public class KubeconfigContextSummary
    {
        /// <summary>
        /// Context name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Cluster name, or null.
        /// </summary>
        public string? Cluster { get; set; } = null;

        /// <summary>
        /// Cluster API server URL, or null.
        /// </summary>
        public string? Server { get; set; } = null;

        /// <summary>
        /// User name, or null.
        /// </summary>
        public string? User { get; set; } = null;

        /// <summary>
        /// Default namespace, or null.
        /// </summary>
        public string? Namespace { get; set; } = null;
    }
}
