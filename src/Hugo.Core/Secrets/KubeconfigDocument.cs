namespace Hugo.Core.Secrets
{
    using System.Collections.Generic;
    using YamlDotNet.Serialization;

    /// <summary>
    /// The parts of a kubeconfig file the importer reads (YAML or JSON).
    /// </summary>
    public class KubeconfigDocument
    {
        /// <summary>
        /// API version (v1).
        /// </summary>
        [YamlMember(Alias = "apiVersion")]
        public string? ApiVersion { get; set; } = null;

        /// <summary>
        /// Kind (Config).
        /// </summary>
        [YamlMember(Alias = "kind")]
        public string? Kind { get; set; } = null;

        /// <summary>
        /// Current context name.
        /// </summary>
        [YamlMember(Alias = "current-context")]
        public string? CurrentContext { get; set; } = null;

        /// <summary>
        /// Contexts.
        /// </summary>
        [YamlMember(Alias = "contexts")]
        public List<KubeconfigNamedContext> Contexts { get; set; } = new List<KubeconfigNamedContext>();

        /// <summary>
        /// Clusters.
        /// </summary>
        [YamlMember(Alias = "clusters")]
        public List<KubeconfigNamedCluster> Clusters { get; set; } = new List<KubeconfigNamedCluster>();
    }
}
