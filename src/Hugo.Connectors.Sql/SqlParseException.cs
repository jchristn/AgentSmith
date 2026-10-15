namespace Hugo.Connectors.Sql
{
    using System;

    /// <summary>
    /// Raised when SQL text cannot be tokenized or parsed (for example an unterminated string or comment).
    /// The classifier converts it to a Destructive classification (fail closed).
    /// </summary>
    public class SqlParseException : Exception
    {
        /// <summary>
        /// Zero-based character offset where the problem was detected.
        /// </summary>
        public int Position { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">Message.</param>
        /// <param name="position">Source offset.</param>
        public SqlParseException(string message, int position) : base(message)
        {
            Position = position;
        }
    }
}
