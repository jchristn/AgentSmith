namespace Test.Shared.Connectors.Observability
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One row of the index guard table.
    /// </summary>
    public class EsIndexGuardCase
    {
        /// <summary>
        /// Requested index expression.
        /// </summary>
        public string Expression { get; set; } = String.Empty;

        /// <summary>
        /// Allowed patterns of the resource.
        /// </summary>
        public List<string> Allowed { get; set; } = new List<string>();

        /// <summary>
        /// Expected outcome: <c>A</c> allowed, <c>D</c> permission_denied, <c>I</c> invalid_argument.
        /// </summary>
        public string Expected { get; set; } = "A";

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="expression">Expression.</param>
        /// <param name="allowed">Allowed patterns, newline separated.</param>
        /// <param name="expected">Expected outcome.</param>
        public EsIndexGuardCase(string expression, string allowed, string expected)
        {
            Expression = expression;
            Allowed = new List<string>(allowed.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            Expected = expected;
        }
    }
}
