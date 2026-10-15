namespace Hugo.Connectors.Abstractions
{
    /// <summary>
    /// Classification of a specific tool call by its effect on the target system.
    /// Ordered by severity: a higher value is more severe.
    /// </summary>
    public enum EffectEnum
    {
        /// <summary>
        /// Observes state without changing it.
        /// </summary>
        Read = 0,

        /// <summary>
        /// Changes state in a recoverable way (create, update, restart, scale).
        /// </summary>
        Write = 1,

        /// <summary>
        /// Removes data, stops service, or otherwise changes state in a way that may not be recoverable.
        /// </summary>
        Destructive = 2
    }
}
