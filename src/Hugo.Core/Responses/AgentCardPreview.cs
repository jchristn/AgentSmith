namespace Hugo.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Preview of the A2A agent card a profile will publish (the A2A surface serves the real card). Not thread-safe.
    /// </summary>
    public class AgentCardPreview
    {
        #region Public-Members

        /// <summary>
        /// Card name (the profile display name).
        /// </summary>
        public string Name
        {
            get
            {
                return _Name;
            }
            set
            {
                _Name = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Summary description.
        /// </summary>
        public string Description
        {
            get
            {
                return _Description;
            }
            set
            {
                _Description = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Interface URL.
        /// </summary>
        public string Url
        {
            get
            {
                return _Url;
            }
            set
            {
                _Url = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Hugo version.
        /// </summary>
        public string Version
        {
            get
            {
                return _Version;
            }
            set
            {
                _Version = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Skills: one per bound family plus agent-smith.
        /// </summary>
        public List<AgentCardSkill> Skills { get; set; } = new List<AgentCardSkill>();

        #endregion

        #region Private-Members

        private string _Name = String.Empty;
        private string _Description = String.Empty;
        private string _Url = String.Empty;
        private string _Version = String.Empty;

        #endregion
    }
}
