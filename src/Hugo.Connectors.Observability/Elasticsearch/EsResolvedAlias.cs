namespace Hugo.Connectors.Observability
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One alias in a <c>_resolve/index</c> response.
    /// </summary>
    public class EsResolvedAlias
    {
        /// <summary>
        /// Alias name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Indexes or data streams the alias points to.
        /// </summary>
        public List<string> Indices { get; set; } = new List<string>();
    }
}
