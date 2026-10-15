namespace Hugo.Connectors.Sql
{
    /// <summary>
    /// Kind of a SQL token produced by <see cref="SqlTokenizer"/>.
    /// </summary>
    public enum SqlTokenKindEnum
    {
        /// <summary>
        /// Unquoted keyword or identifier. <see cref="SqlToken.Text"/> is upper case.
        /// </summary>
        Word = 0,

        /// <summary>
        /// Quoted identifier (double quotes, backticks, or brackets, depending on the dialect).
        /// </summary>
        QuotedIdentifier = 1,

        /// <summary>
        /// String literal, including dollar-quoted strings. <see cref="SqlToken.Value"/> holds the content.
        /// </summary>
        String = 2,

        /// <summary>
        /// Numeric literal.
        /// </summary>
        Number = 3,

        /// <summary>
        /// Variable or parameter marker (for example <c>@id</c>, <c>@@VERSION</c>, <c>$1</c>).
        /// </summary>
        Variable = 4,

        /// <summary>
        /// Single punctuation or operator character, including the statement separator <c>;</c>.
        /// </summary>
        Symbol = 5
    }
}
