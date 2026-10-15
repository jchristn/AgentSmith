namespace Hugo.Connectors.Sql
{
    using System;

    /// <summary>
    /// One SQL token. Immutable and thread-safe.
    /// </summary>
    public class SqlToken
    {
        #region Public-Members

        /// <summary>
        /// Token kind.
        /// </summary>
        public SqlTokenKindEnum Kind { get; }

        /// <summary>
        /// Token text. Upper case for <see cref="SqlTokenKindEnum.Word"/>; the raw source text otherwise.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Content of a string literal or quoted identifier without delimiters; equal to <see cref="Text"/> otherwise.
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// Zero-based character offset in the source.
        /// </summary>
        public int Position { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="kind">Kind.</param>
        /// <param name="text">Text.</param>
        /// <param name="value">Content; null means the same as text.</param>
        /// <param name="position">Source offset.</param>
        public SqlToken(SqlTokenKindEnum kind, string text, string? value, int position)
        {
            Kind = kind;
            Text = text ?? String.Empty;
            Value = value ?? Text;
            Position = position;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether this is a word with the given upper-case text.
        /// </summary>
        /// <param name="word">Upper-case word.</param>
        /// <returns>True on match.</returns>
        public bool IsWord(string word)
        {
            return Kind == SqlTokenKindEnum.Word && String.Equals(Text, word, StringComparison.Ordinal);
        }

        /// <summary>
        /// Whether this is the given symbol.
        /// </summary>
        /// <param name="symbol">Symbol character.</param>
        /// <returns>True on match.</returns>
        public bool IsSymbol(char symbol)
        {
            return Kind == SqlTokenKindEnum.Symbol && Text.Length == 1 && Text[0] == symbol;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Kind + ":" + Text;
        }

        #endregion
    }
}
