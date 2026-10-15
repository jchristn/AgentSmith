namespace Hugo.Sdk.Models
{
    /// <summary>
    /// A context found in a parsed kubeconfig.
    /// </summary>
    public class KubeconfigContext
    {
        #region Public-Members

        /// <summary>
        /// Name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Cluster.
        /// </summary>
        public string? Cluster { get; set; } = null;

        /// <summary>
        /// Server.
        /// </summary>
        public string? Server { get; set; } = null;

        /// <summary>
        /// User.
        /// </summary>
        public string? User { get; set; } = null;

        /// <summary>
        /// Namespace.
        /// </summary>
        public string? Namespace { get; set; } = null;

        #endregion
    }
}
