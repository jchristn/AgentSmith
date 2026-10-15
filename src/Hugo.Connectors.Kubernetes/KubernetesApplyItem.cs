namespace Hugo.Connectors.Kubernetes
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Nodes;

    /// <summary>
    /// One object of a <c>k8s_apply</c> call as it moves through preparation, dry run, and apply. Not thread-safe.
    /// </summary>
    public class KubernetesApplyItem
    {
        #region Public-Members

        /// <summary>
        /// Resource type.
        /// </summary>
        public KubernetesResourceType Type
        {
            get
            {
                return _Type;
            }
            set
            {
                _Type = value ?? throw new ArgumentNullException(nameof(Type));
            }
        }

        /// <summary>
        /// Namespace, or null for cluster-scoped objects.
        /// </summary>
        public string? Namespace { get; set; } = null;

        /// <summary>
        /// Object name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// REST path of the object.
        /// </summary>
        public string Path { get; set; } = String.Empty;

        /// <summary>
        /// Label for messages, for example <c>deployment.apps hugo-test/web</c>.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// JSON body sent as the apply patch.
        /// </summary>
        public string Body { get; set; } = "{}";

        /// <summary>
        /// Whether the object existed before the call.
        /// </summary>
        public bool Existed { get; set; } = false;

        /// <summary>
        /// Live object before the call, or null.
        /// </summary>
        public JsonNode? Live { get; set; } = null;

        /// <summary>
        /// Resource version before the call, or null.
        /// </summary>
        public string? ResourceVersionBefore { get; set; } = null;

        /// <summary>
        /// Server dry-run result, or null.
        /// </summary>
        public JsonNode? DryRunResult { get; set; } = null;

        /// <summary>
        /// Fields the apply removes from the live object. Never null.
        /// </summary>
        public List<string> RemovedFields
        {
            get
            {
                return _RemovedFields;
            }
            set
            {
                _RemovedFields = value ?? new List<string>();
            }
        }

        #endregion

        #region Private-Members

        private KubernetesResourceType _Type = new KubernetesResourceType();
        private List<string> _RemovedFields = new List<string>();

        #endregion
    }
}
