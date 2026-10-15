namespace Hugo.Connectors.Kubernetes
{
    using System;

    /// <summary>
    /// Rollout progress of a workload, as <c>kubectl rollout status</c> reports it. Not thread-safe.
    /// </summary>
    public class KubernetesRolloutState
    {
        #region Public-Members

        /// <summary>
        /// Whether the rollout is complete.
        /// </summary>
        public bool Done { get; set; } = false;

        /// <summary>
        /// Whether the rollout has failed (for example its progress deadline was exceeded).
        /// </summary>
        public bool Failed { get; set; } = false;

        /// <summary>
        /// Status message. Never null.
        /// </summary>
        public string Message
        {
            get
            {
                return _Message;
            }
            set
            {
                _Message = value ?? String.Empty;
            }
        }

        #endregion

        #region Private-Members

        private string _Message = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public KubernetesRolloutState()
        {
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="done">Whether the rollout is complete.</param>
        /// <param name="message">Message.</param>
        public KubernetesRolloutState(bool done, string message)
        {
            Done = done;
            Message = message;
        }

        #endregion
    }
}
