namespace Hugo.Core.Secrets
{
    using System.Collections.Generic;
    using YamlDotNet.Serialization;

    /// <summary>
    /// A kubeconfig cluster body.
    /// </summary>
    public class KubeconfigClusterEntry
    {
        /// <summary>
        /// API server URL.
        /// </summary>
        [YamlMember(Alias = "server")]
        public string? Server { get; set; } = null;
    }
}
