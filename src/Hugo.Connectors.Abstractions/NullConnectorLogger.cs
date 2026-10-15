namespace Hugo.Connectors.Abstractions
{
    /// <summary>
    /// Logger that discards every message. Thread-safe.
    /// </summary>
    public class NullConnectorLogger : IConnectorLogger
    {
        /// <summary>
        /// Shared instance.
        /// </summary>
        public static NullConnectorLogger Instance { get; } = new NullConnectorLogger();

        /// <inheritdoc />
        public void Debug(string message)
        {
        }

        /// <inheritdoc />
        public void Info(string message)
        {
        }

        /// <inheritdoc />
        public void Warn(string message)
        {
        }

        /// <inheritdoc />
        public void Error(string message)
        {
        }
    }
}
