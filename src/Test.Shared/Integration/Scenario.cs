namespace Test.Shared.Integration
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// One resource of the joel golden scenario.
    /// </summary>
    public class Scenario
    {
        /// <summary>
        /// Resource name and alias.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Connector type.
        /// </summary>
        public string Type { get; }

        /// <summary>
        /// Environment tag.
        /// </summary>
        public string Environment { get; }

        /// <summary>
        /// Endpoint summary.
        /// </summary>
        public string Endpoint { get; }

        /// <summary>
        /// Access mode.
        /// </summary>
        public AccessModeEnum Mode { get; }

        /// <summary>
        /// Operator notes, or null.
        /// </summary>
        public string? Notes { get; }

        /// <summary>
        /// Root knowledge (database scope), or null.
        /// </summary>
        public string? Knowledge { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <param name="type">Connector type.</param>
        /// <param name="environment">Environment.</param>
        /// <param name="endpoint">Endpoint.</param>
        /// <param name="mode">Access mode.</param>
        /// <param name="notes">Notes.</param>
        /// <param name="knowledge">Root knowledge.</param>
        public Scenario(string name, string type, string environment, string endpoint, AccessModeEnum mode, string? notes, string? knowledge)
        {
            Name = name;
            Type = type;
            Environment = environment;
            Endpoint = endpoint;
            Mode = mode;
            Notes = notes;
            Knowledge = knowledge;
        }
    }
}
