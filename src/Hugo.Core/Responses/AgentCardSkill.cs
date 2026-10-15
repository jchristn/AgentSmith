namespace Hugo.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One skill of an agent card preview. Not thread-safe.
    /// </summary>
    public class AgentCardSkill
    {
        #region Public-Members

        /// <summary>
        /// Skill identifier.
        /// </summary>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                _Id = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Name.
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
        /// Description.
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
        /// Tags.
        /// </summary>
        public List<string> Tags { get; set; } = new List<string>();

        #endregion

        #region Private-Members

        private string _Id = String.Empty;
        private string _Name = String.Empty;
        private string _Description = String.Empty;

        #endregion
    }
}
