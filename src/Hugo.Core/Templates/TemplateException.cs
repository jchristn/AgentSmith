namespace Hugo.Core.Templates
{
    using System;

    /// <summary>
    /// Raised when a template cannot be parsed, validated, or rendered. Carries the template name and the 1-based
    /// line and column of the problem when known.
    /// </summary>
    public class TemplateException : Exception
    {
        /// <summary>
        /// Template name, when known.
        /// </summary>
        public string? TemplateName { get; }

        /// <summary>
        /// 1-based line, or 0 when unknown.
        /// </summary>
        public int Line { get; }

        /// <summary>
        /// 1-based column, or 0 when unknown.
        /// </summary>
        public int Column { get; }

        /// <summary>
        /// Message without position information.
        /// </summary>
        public string Detail { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="detail">Problem description.</param>
        /// <param name="templateName">Template name.</param>
        /// <param name="line">1-based line.</param>
        /// <param name="column">1-based column.</param>
        public TemplateException(string detail, string? templateName = null, int line = 0, int column = 0)
            : base(Format(detail, templateName, line, column))
        {
            Detail = detail ?? String.Empty;
            TemplateName = templateName;
            Line = line;
            Column = column;
        }

        private static string Format(string detail, string? templateName, int line, int column)
        {
            string where = String.IsNullOrEmpty(templateName) ? "template" : "template '" + templateName + "'";
            if (line > 0) where += " at line " + line + ", column " + column;
            return where + ": " + detail;
        }
    }
}
