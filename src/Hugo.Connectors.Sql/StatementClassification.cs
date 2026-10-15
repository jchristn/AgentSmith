namespace Hugo.Connectors.Sql
{
    using System;
    using System.Collections.Generic;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Result of classifying SQL text with <see cref="StatementClassifier"/>. Not thread-safe for mutation.
    /// </summary>
    public class StatementClassification
    {
        #region Public-Members

        /// <summary>
        /// Effect of the most severe statement. Destructive when the text could not be parsed.
        /// </summary>
        public EffectEnum Effect { get; set; } = EffectEnum.Destructive;

        /// <summary>
        /// Short human-readable reason (for example <c>statement type DELETE</c>).
        /// </summary>
        public string Reason { get; set; } = String.Empty;

        /// <summary>
        /// Statement type label of each statement in order (for example <c>SELECT</c>, <c>EXPLAIN ANALYZE DELETE</c>).
        /// Never null.
        /// </summary>
        public List<string> StatementTypes
        {
            get
            {
                return _StatementTypes;
            }
            set
            {
                _StatementTypes = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Number of non-empty statements found.
        /// </summary>
        public int StatementCount { get; set; } = 0;

        /// <summary>
        /// Whether the text was tokenized and every statement recognized.
        /// </summary>
        public bool Parsed { get; set; } = false;

        /// <summary>
        /// Whether the text must be rejected regardless of access mode (multiple statements when batches are not
        /// allowed, or empty text).
        /// </summary>
        public bool Rejected { get; set; } = false;

        /// <summary>
        /// Statement type label of the first statement, or empty.
        /// </summary>
        public string PrimaryType
        {
            get
            {
                return _StatementTypes.Count > 0 ? _StatementTypes[0] : String.Empty;
            }
        }

        #endregion

        #region Private-Members

        private List<string> _StatementTypes = new List<string>();

        #endregion
    }
}
