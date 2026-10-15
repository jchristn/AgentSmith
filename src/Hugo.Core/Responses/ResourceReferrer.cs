namespace Hugo.Core.Responses
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// A binding that uses a resource. Not thread-safe.
    /// </summary>
    public class ResourceReferrer
    {
        #region Public-Members

        /// <summary>
        /// Profile identifier.
        /// </summary>
        public string AgentProfileId
        {
            get
            {
                return _AgentProfileId;
            }
            set
            {
                _AgentProfileId = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Profile name.
        /// </summary>
        public string AgentProfileName
        {
            get
            {
                return _AgentProfileName;
            }
            set
            {
                _AgentProfileName = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Binding identifier.
        /// </summary>
        public string BindingId
        {
            get
            {
                return _BindingId;
            }
            set
            {
                _BindingId = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Alias.
        /// </summary>
        public string Alias
        {
            get
            {
                return _Alias;
            }
            set
            {
                _Alias = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Access mode.
        /// </summary>
        public AccessModeEnum AccessMode { get; set; } = AccessModeEnum.ReadOnly;

        /// <summary>
        /// Whether the binding is enabled.
        /// </summary>
        public bool Enabled { get; set; } = true;

        #endregion

        #region Private-Members

        private string _AgentProfileId = String.Empty;
        private string _AgentProfileName = String.Empty;
        private string _BindingId = String.Empty;
        private string _Alias = String.Empty;

        #endregion
    }
}
