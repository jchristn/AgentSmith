namespace Hugo.Connectors.Kubernetes
{
    using System;
    using System.Collections.Generic;
    using k8s.KubeConfigModels;

    /// <summary>
    /// Result of inspecting a kubeconfig: the parsed document, the selected context, the server URL, and any problems
    /// that make it unsafe or unusable on a shared server. Never contains credential values. Not thread-safe.
    /// </summary>
    public class KubeconfigInspection
    {
        #region Public-Members

        /// <summary>
        /// Parsed kubeconfig, or null when it could not be parsed.
        /// </summary>
        public K8SConfiguration? Configuration { get; set; } = null;

        /// <summary>
        /// Name of the selected context, or null.
        /// </summary>
        public string? ContextName { get; set; } = null;

        /// <summary>
        /// Server URL of the selected context's cluster, or null.
        /// </summary>
        public string? Server { get; set; } = null;

        /// <summary>
        /// Namespace of the selected context, or null.
        /// </summary>
        public string? Namespace { get; set; } = null;

        /// <summary>
        /// Names of every context in the kubeconfig. Never null.
        /// </summary>
        public List<string> Contexts
        {
            get
            {
                return _Contexts;
            }
            set
            {
                _Contexts = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Problems found. Empty when the kubeconfig is usable. Never null.
        /// </summary>
        public List<string> Errors
        {
            get
            {
                return _Errors;
            }
            set
            {
                _Errors = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Whether the kubeconfig is usable.
        /// </summary>
        public bool IsValid
        {
            get
            {
                return Configuration != null && _Errors.Count == 0;
            }
        }

        #endregion

        #region Private-Members

        private List<string> _Contexts = new List<string>();
        private List<string> _Errors = new List<string>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// All errors as one sentence list.
        /// </summary>
        /// <returns>Text.</returns>
        public string ErrorText()
        {
            return String.Join(" ", _Errors);
        }

        #endregion
    }
}
