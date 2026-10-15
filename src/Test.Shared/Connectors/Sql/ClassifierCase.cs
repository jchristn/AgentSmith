namespace Test.Shared.Connectors.Sql
{
    using System;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Sql;

    /// <summary>
    /// One row of the statement classifier table. Immutable.
    /// </summary>
    public class ClassifierCase
    {
        /// <summary>
        /// Dialect.
        /// </summary>
        public SqlEngineEnum Engine { get; }

        /// <summary>
        /// SQL text.
        /// </summary>
        public string Sql { get; }

        /// <summary>
        /// Expected effect.
        /// </summary>
        public EffectEnum Expected { get; }

        /// <summary>
        /// Whether the text must be rejected.
        /// </summary>
        public bool Rejected { get; }

        /// <summary>
        /// Whether the resource allows multiple statements.
        /// </summary>
        public bool AllowMultiple { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="engine">Dialect.</param>
        /// <param name="sql">SQL text.</param>
        /// <param name="expected">Expected effect.</param>
        /// <param name="rejected">Whether rejection is expected.</param>
        /// <param name="allowMultiple">Whether batches are allowed.</param>
        public ClassifierCase(SqlEngineEnum engine, string sql, EffectEnum expected, bool rejected, bool allowMultiple)
        {
            Engine = engine;
            Sql = sql ?? String.Empty;
            Expected = expected;
            Rejected = rejected;
            AllowMultiple = allowMultiple;
        }
    }
}
