namespace Test.Shared.Connectors.Kubernetes
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One row of the <c>k8s_apply</c> manifest classification table.
    /// </summary>
    public class KubernetesManifestCase
    {
        #region Public-Members

        /// <summary>
        /// Expected verdict: <c>R</c>, <c>W</c>, <c>D</c>, or <c>X</c> (rejected).
        /// </summary>
        public string Expected { get; set; } = "D";

        /// <summary>
        /// Description.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Manifest text.
        /// </summary>
        public string Manifest { get; set; } = String.Empty;

        /// <summary>
        /// Resource settings.
        /// </summary>
        public Dictionary<string, string> Settings { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// The call's namespace argument, or null.
        /// </summary>
        public string? Namespace { get; set; } = null;

        /// <summary>
        /// Whether the call is a dry run.
        /// </summary>
        public bool DryRun { get; set; } = false;

        /// <summary>
        /// Whether the call forces conflicts.
        /// </summary>
        public bool Force { get; set; } = false;

        /// <summary>
        /// Whether the call allows field removal.
        /// </summary>
        public bool AllowFieldRemoval { get; set; } = false;

        #endregion
    }
}
