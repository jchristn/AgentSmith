namespace Test.Shared.Connectors.Sql
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// One rule of <see cref="ScriptedConnectorModel"/>. Immutable and thread-safe.
    /// </summary>
    public class ScriptedRule
    {
        /// <summary>
        /// Purpose to match.
        /// </summary>
        public string Purpose { get; }

        /// <summary>
        /// Text the user prompt must contain, or null for any.
        /// </summary>
        public string? Contains { get; }

        /// <summary>
        /// Response factory.
        /// </summary>
        public Func<ConnectorModelRequest, string> Respond { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="purpose">Purpose.</param>
        /// <param name="contains">Prompt text, or null.</param>
        /// <param name="respond">Response factory.</param>
        /// <exception cref="ArgumentNullException">Thrown when purpose or respond is null.</exception>
        public ScriptedRule(string purpose, string? contains, Func<ConnectorModelRequest, string> respond)
        {
            Purpose = purpose ?? throw new ArgumentNullException(nameof(purpose));
            Contains = contains;
            Respond = respond ?? throw new ArgumentNullException(nameof(respond));
        }
    }
}
