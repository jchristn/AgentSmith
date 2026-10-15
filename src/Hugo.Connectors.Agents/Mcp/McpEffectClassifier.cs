namespace Hugo.Connectors.Agents.Mcp
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Derives the effect of a call to an external MCP tool from the tool's annotations (plan section 8.3):
    /// <c>destructiveHint = true</c> is Destructive, otherwise <c>readOnlyHint = true</c> is Read, otherwise Write.
    /// A tool whose annotations are unknown (not yet discovered, or no longer listed) is Destructive, failing closed.
    /// Annotations are hints written by the remote server, so the operator should still bind untrusted servers ReadOnly
    /// or with approvals. Thread-safe.
    /// </summary>
    public static class McpEffectClassifier
    {
        #region Public-Methods

        /// <summary>
        /// Effect from annotation hints. A tool that claims both read-only and destructive takes the more severe effect.
        /// </summary>
        /// <param name="readOnlyHint">readOnlyHint, or null when absent.</param>
        /// <param name="destructiveHint">destructiveHint, or null when absent.</param>
        /// <returns>Effect.</returns>
        public static EffectEnum FromHints(bool? readOnlyHint, bool? destructiveHint)
        {
            if (destructiveHint == true) return EffectEnum.Destructive;
            if (readOnlyHint == true) return EffectEnum.Read;
            return EffectEnum.Write;
        }

        /// <summary>
        /// Reason text for a discovered tool.
        /// </summary>
        /// <param name="tool">Tool.</param>
        /// <returns>Reason.</returns>
        /// <exception cref="ArgumentNullException">Thrown when tool is null.</exception>
        public static string Reason(McpRemoteTool tool)
        {
            ArgumentNullException.ThrowIfNull(tool);
            if (tool.DestructiveHint == true) return "remote tool " + tool.Name + " has destructiveHint=true";
            if (tool.ReadOnlyHint == true) return "remote tool " + tool.Name + " has readOnlyHint=true";
            if (tool.ReadOnlyHint == null && tool.DestructiveHint == null) return "remote tool " + tool.Name + " has no readOnlyHint or destructiveHint, so it is Write";
            return "remote tool " + tool.Name + " is not read-only and not marked destructive, so it is Write";
        }

        /// <summary>
        /// Short effect letter (<c>R</c>, <c>W</c>, <c>D</c>).
        /// </summary>
        /// <param name="effect">Effect.</param>
        /// <returns>Letter.</returns>
        public static string Letter(EffectEnum effect)
        {
            if (effect == EffectEnum.Read) return "R";
            if (effect == EffectEnum.Write) return "W";
            return "D";
        }

        #endregion
    }
}
