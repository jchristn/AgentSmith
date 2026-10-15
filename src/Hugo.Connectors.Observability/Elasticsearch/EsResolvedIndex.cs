namespace Hugo.Connectors.Observability
{
    using System;

    /// <summary>
    /// One concrete index in a <c>_resolve/index</c> response.
    /// </summary>
    public class EsResolvedIndex
    {
        /// <summary>
        /// Index name.
        /// </summary>
        public string Name { get; set; } = String.Empty;
    }
}
