namespace Hugo.Core.Secrets
{
    using System.Collections.Generic;
    using YamlDotNet.Serialization;

    /// <summary>
    /// A named kubeconfig context.
    /// </summary>
    public class KubeconfigNamedContext
    {
        /// <summary>
        /// Context name.
        /// </summary>
        [YamlMember(Alias = "name")]
        public string? Name { get; set; } = null;

        /// <summary>
        /// Context body.
        /// </summary>
        [YamlMember(Alias = "context")]
        public KubeconfigContextEntry? Context { get; set; } = null;
    }
}
