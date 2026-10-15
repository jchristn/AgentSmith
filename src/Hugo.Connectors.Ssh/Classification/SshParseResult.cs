namespace Hugo.Connectors.Ssh.Classification
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Result of parsing a shell command line with <see cref="SshCommandParser"/>. Not thread-safe for mutation.
    /// </summary>
    public class SshParseResult
    {
        #region Public-Members

        /// <summary>
        /// Whether the command was understood. When false, <see cref="Error"/> explains why and callers must fail closed.
        /// </summary>
        public bool Success { get; set; } = true;

        /// <summary>
        /// Parse error message when <see cref="Success"/> is false.
        /// </summary>
        public string Error { get; set; } = String.Empty;

        /// <summary>
        /// Simple command segments in order. Never null.
        /// </summary>
        public List<SshCommandSegment> Segments
        {
            get
            {
                return _Segments;
            }
            set
            {
                _Segments = value ?? new List<SshCommandSegment>();
            }
        }

        /// <summary>
        /// Inner command text of every command substitution (<c>$(...)</c>, backticks) and process substitution
        /// (<c>&lt;(...)</c>, <c>&gt;(...)</c>) found at this level. Callers classify each one recursively. Never null.
        /// </summary>
        public List<string> Substitutions
        {
            get
            {
                return _Substitutions;
            }
            set
            {
                _Substitutions = value ?? new List<string>();
            }
        }

        #endregion

        #region Private-Members

        private List<SshCommandSegment> _Segments = new List<SshCommandSegment>();
        private List<string> _Substitutions = new List<string>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build a failed result.
        /// </summary>
        /// <param name="error">Error message.</param>
        /// <returns>Result.</returns>
        public static SshParseResult Fail(string error)
        {
            return new SshParseResult { Success = false, Error = error ?? "unknown parse error" };
        }

        #endregion
    }
}
