namespace Hugo.Connectors.Ssh.Classification
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// One simple command of a parsed shell command line: the words (quotes removed) and its redirections. Pipelines,
    /// lists (<c>;</c>, <c>&amp;&amp;</c>, <c>||</c>, <c>&amp;</c>), and subshell groups are split into separate segments.
    /// Not thread-safe for mutation.
    /// </summary>
    public class SshCommandSegment
    {
        #region Public-Members

        /// <summary>
        /// Words in order, with quoting removed. Never null.
        /// </summary>
        public List<string> Words
        {
            get
            {
                return _Words;
            }
            set
            {
                _Words = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Redirections in order. Never null.
        /// </summary>
        public List<SshRedirection> Redirections
        {
            get
            {
                return _Redirections;
            }
            set
            {
                _Redirections = value ?? new List<SshRedirection>();
            }
        }

        #endregion

        #region Private-Members

        private List<string> _Words = new List<string>();
        private List<SshRedirection> _Redirections = new List<SshRedirection>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Render the segment as normalized text (words joined by single spaces, then redirections).
        /// </summary>
        /// <returns>Text.</returns>
        public override string ToString()
        {
            List<string> parts = new List<string>(_Words);
            parts.AddRange(_Redirections.Select(r => r.ToString()));
            return String.Join(" ", parts);
        }

        #endregion
    }
}
