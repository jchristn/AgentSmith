namespace Hugo.Connectors.Ssh.Classification
{
    using System;
    using System.Globalization;

    /// <summary>
    /// One shell redirection found in a simple command (for example <c>&gt; /tmp/out</c> or <c>2&gt;&amp;1</c>).
    /// Treat as immutable after construction.
    /// </summary>
    public class SshRedirection
    {
        #region Public-Members

        /// <summary>
        /// Redirection operator: one of <c>&lt;</c>, <c>&lt;&lt;&lt;</c>, <c>&lt;&amp;</c>, <c>&lt;&gt;</c>, <c>&gt;</c>,
        /// <c>&gt;&gt;</c>, <c>&gt;|</c>, <c>&gt;&amp;</c>, <c>&amp;&gt;</c>, <c>&amp;&gt;&gt;</c>.
        /// </summary>
        public string Operator { get; set; } = String.Empty;

        /// <summary>
        /// Explicit file descriptor number written before the operator (for example 2 in <c>2&gt;</c>), or null.
        /// </summary>
        public int? FileDescriptor { get; set; } = null;

        /// <summary>
        /// Redirection target with quotes removed (a file path, a descriptor number, or <c>-</c>).
        /// </summary>
        public string Target { get; set; } = String.Empty;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Render the redirection as text for reasons and pattern matching.
        /// </summary>
        /// <returns>Text such as <c>2&gt; /tmp/err</c>.</returns>
        public override string ToString()
        {
            return (FileDescriptor.HasValue ? FileDescriptor.Value.ToString(CultureInfo.InvariantCulture) : String.Empty) + Operator + " " + Target;
        }

        #endregion
    }
}
