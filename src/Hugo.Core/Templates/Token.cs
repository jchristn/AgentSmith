namespace Hugo.Core.Templates
{
    using System;

    /// <summary>
    /// Lexical token produced while parsing a template: literal text or the inside of a {{ }} tag.
    /// </summary>
    public class Token
    {
        /// <summary>
        /// Whether this is literal text.
        /// </summary>
        public bool IsText { get; set; } = true;

        /// <summary>
        /// Text or tag body.
        /// </summary>
        public string Text { get; set; } = String.Empty;

        /// <summary>
        /// 1-based line.
        /// </summary>
        public int Line { get; set; } = 1;

        /// <summary>
        /// 1-based column.
        /// </summary>
        public int Column { get; set; } = 1;

        /// <summary>
        /// Create a text token.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="line">Line.</param>
        /// <param name="column">Column.</param>
        /// <returns>Token.</returns>
        public static Token TextToken(string text, int line, int column)
        {
            Token token = new Token();
            token.IsText = true;
            token.Text = text ?? String.Empty;
            token.Line = line;
            token.Column = column;
            return token;
        }

        /// <summary>
        /// Create a tag token.
        /// </summary>
        /// <param name="body">Tag body without braces.</param>
        /// <param name="line">Line.</param>
        /// <param name="column">Column.</param>
        /// <returns>Token.</returns>
        public static Token TagToken(string body, int line, int column)
        {
            Token token = new Token();
            token.IsText = false;
            token.Text = body ?? String.Empty;
            token.Line = line;
            token.Column = column;
            return token;
        }
    }
}
