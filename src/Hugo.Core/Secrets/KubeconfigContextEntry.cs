namespace Hugo.Core.Secrets
{
    using System.Collections.Generic;
    using YamlDotNet.Serialization;

    /// <summary>
    /// A kubeconfig context body.
    /// </summary>
    public class KubeconfigContextEntry
    {
        /// <summary>
        /// Cluster name.
        /// </summary>
        [YamlMember(Alias = "cluster")]
        public string? Cluster { get; set; } = null;

        /// <summary>
        /// User name.
        /// </summary>
        [YamlMember(Alias = "user")]
        public string? User { get; set; } = null;

        /// <summary>
        /// Default namespace.
        /// </summary>
        [YamlMember(Alias = "namespace")]
        public string? Namespace { get; set; } = null;
    }
}
