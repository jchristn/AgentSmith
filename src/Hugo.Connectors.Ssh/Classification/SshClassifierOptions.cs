namespace Hugo.Connectors.Ssh.Classification
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Resource-level inputs to <see cref="SshCommandClassifier"/>. Patterns are .NET regular expressions matched
    /// case-insensitively. Not thread-safe for mutation; treat as immutable once handed to the classifier.
    /// </summary>
    public class SshClassifierOptions
    {
        #region Public-Members

        /// <summary>
        /// Whether <c>sudo</c>, <c>doas</c>, <c>sudoedit</c>, <c>pkexec</c>, and <c>su</c> are permitted. When false, any
        /// command that uses them is rejected. When true, <c>sudo</c>, <c>doas</c>, and <c>sudoedit</c> must still be
        /// called with <c>-n</c> (non-interactive), because there is no terminal to type a password into. Default false.
        /// </summary>
        public bool AllowSudo { get; set; } = false;

        /// <summary>
        /// Patterns that reject a command outright. Each is searched (not anchored) in the raw command and in every
        /// normalized segment. Never null.
        /// </summary>
        public List<string> DeniedPatterns
        {
            get
            {
                return _DeniedPatterns;
            }
            set
            {
                _DeniedPatterns = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Additional patterns that mark a segment Destructive. Each is searched (not anchored) in the normalized
        /// segment text. Never null.
        /// </summary>
        public List<string> DestructivePatterns
        {
            get
            {
                return _DestructivePatterns;
            }
            set
            {
                _DestructivePatterns = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Additional patterns that mark a segment Read. Each must match the whole normalized segment text (the
        /// classifier anchors it). They extend the built-in read-only list and never override a Destructive verdict.
        /// Never null.
        /// </summary>
        public List<string> ReadOnlyPatterns
        {
            get
            {
                return _ReadOnlyPatterns;
            }
            set
            {
                _ReadOnlyPatterns = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Maximum nesting depth for command substitutions, <c>sh -c</c> strings, and wrappers. Deeper input is
        /// classified Destructive. Minimum 1, maximum 32, default 8.
        /// </summary>
        public int MaxDepth
        {
            get
            {
                return _MaxDepth;
            }
            set
            {
                _MaxDepth = Math.Clamp(value, 1, 32);
            }
        }

        /// <summary>
        /// Timeout for each regular expression evaluation. A pattern that times out fails closed (deny patterns reject,
        /// destructive patterns match, read-only patterns do not match). Minimum 10 ms, maximum 5 seconds, default 100 ms.
        /// </summary>
        public TimeSpan RegexTimeout
        {
            get
            {
                return _RegexTimeout;
            }
            set
            {
                if (value < TimeSpan.FromMilliseconds(10)) value = TimeSpan.FromMilliseconds(10);
                if (value > TimeSpan.FromSeconds(5)) value = TimeSpan.FromSeconds(5);
                _RegexTimeout = value;
            }
        }

        #endregion

        #region Private-Members

        private List<string> _DeniedPatterns = new List<string>();
        private List<string> _DestructivePatterns = new List<string>();
        private List<string> _ReadOnlyPatterns = new List<string>();
        private int _MaxDepth = 8;
        private TimeSpan _RegexTimeout = TimeSpan.FromMilliseconds(100);

        #endregion
    }
}
