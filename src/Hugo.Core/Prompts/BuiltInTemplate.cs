namespace Hugo.Core.Prompts
{
    using System;

    /// <summary>
    /// One Built-in prompt template (plan section 10.2): immutable content shipped in an assembly. Not thread-safe.
    /// </summary>
    public class BuiltInTemplate
    {
        /// <summary>
        /// Template key (for example persona.agent-smith or connector.ssh.instructions).
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Content.
        /// </summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>
        /// SHA-256 of the content, lowercase hex.
        /// </summary>
        public string Hash { get; set; } = String.Empty;

        /// <summary>
        /// Where it came from: hugo for Hugo.Core, otherwise the connector type.
        /// </summary>
        public string Source { get; set; } = "hugo";

        /// <summary>
        /// Short description of what the template is for.
        /// </summary>
        public string Description { get; set; } = String.Empty;
    }
}
