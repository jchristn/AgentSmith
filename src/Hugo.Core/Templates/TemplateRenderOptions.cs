namespace Hugo.Core.Templates
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Options and outputs for one render call. Not thread-safe; use one instance per render.
    /// </summary>
    public class TemplateRenderOptions
    {
        #region Public-Members

        /// <summary>
        /// Template name for error messages.
        /// </summary>
        public string? TemplateName { get; set; } = null;

        /// <summary>
        /// Whether {{secret.*}} references are allowed. Only harness profile environment, files, and MCP server
        /// headers allow secrets; prompts never do. Default false.
        /// </summary>
        public bool AllowSecrets { get; set; } = false;

        /// <summary>
        /// Resolves an included template's content by name; null means includes are not allowed.
        /// </summary>
        public Func<string, string?>? IncludeResolver { get; set; } = null;

        /// <summary>
        /// Maximum include depth. Minimum 1, maximum 32, default 8.
        /// </summary>
        public int MaxIncludeDepth
        {
            get
            {
                return _MaxIncludeDepth;
            }
            set
            {
                _MaxIncludeDepth = Math.Clamp(value, 1, 32);
            }
        }

        /// <summary>
        /// Maximum rendered length in characters. Minimum 1024, maximum 67108864, default 8388608.
        /// </summary>
        public int MaxOutputLength
        {
            get
            {
                return _MaxOutputLength;
            }
            set
            {
                _MaxOutputLength = Math.Clamp(value, 1024, 67108864);
            }
        }

        /// <summary>
        /// Output: non-empty secret values the render read (for scrubbing).
        /// </summary>
        public HashSet<string> UsedSecretValues { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Output: include names that were rendered.
        /// </summary>
        public HashSet<string> UsedIncludes { get; } = new HashSet<string>(StringComparer.Ordinal);

        #endregion

        #region Private-Members

        private int _MaxIncludeDepth = 8;
        private int _MaxOutputLength = 8388608;

        #endregion
    }
}
