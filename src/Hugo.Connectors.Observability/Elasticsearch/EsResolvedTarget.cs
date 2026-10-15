namespace Hugo.Connectors.Observability
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The index expression a search or count actually sends, after aliases and wildcards were resolved against the
    /// allow list. Immutable and thread-safe.
    /// </summary>
    public class EsResolvedTarget
    {
        #region Public-Members

        /// <summary>
        /// Expression to send: the caller's expression when everything it resolves to is allowed, otherwise the
        /// comma-separated list of the allowed concrete targets.
        /// </summary>
        public string Expression { get; }

        /// <summary>
        /// Concrete indexes or data streams the expression reached that are outside the allow list, and were left out.
        /// Empty when nothing was left out.
        /// </summary>
        public IReadOnlyList<string> Excluded { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="expression">Expression to send.</param>
        /// <param name="excluded">Excluded targets, or null for none.</param>
        /// <exception cref="ArgumentNullException">Thrown when expression is null.</exception>
        public EsResolvedTarget(string expression, IReadOnlyList<string>? excluded)
        {
            Expression = expression ?? throw new ArgumentNullException(nameof(expression));
            Excluded = excluded ?? new List<string>();
        }

        #endregion
    }
}
