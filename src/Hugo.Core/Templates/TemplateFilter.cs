namespace Hugo.Core.Templates
{
    using System;

    /// <summary>
    /// A filter applied to a variable, for example default: "x" or indent: 2.
    /// </summary>
    public class TemplateFilter
    {
        /// <summary>
        /// Filter name (lowercase).
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Argument text, or null when the filter has no argument.
        /// </summary>
        public string? Argument { get; set; } = null;

        /// <summary>
        /// Whether the argument is a literal (quoted string or number) rather than a variable path.
        /// </summary>
        public bool ArgumentIsLiteral { get; set; } = true;
    }
}
