namespace Hugo.Core.Secrets
{
    using System.Collections.Generic;
    using YamlDotNet.Serialization;

    /// <summary>
    /// A named kubeconfig cluster.
    /// </summary>
    public class KubeconfigNamedCluster
    {
        /// <summary>
        /// Cluster name.
        /// </summary>
        [YamlMember(Alias = "name")]
        public string? Name { get; set; } = null;

        /// <summary>
        /// Cluster body.
        /// </summary>
        [YamlMember(Alias = "cluster")]
        public KubeconfigClusterEntry? Cluster { get; set; } = null;
    }
}
