namespace Hugo.Connectors.Abstractions
{
    /// <summary>
    /// Logger handed to connectors. Messages pass through the server's secret scrubber. Implementations are thread-safe.
    /// </summary>
    public interface IConnectorLogger
    {
        /// <summary>
        /// Log a debug message.
        /// </summary>
        /// <param name="message">Message.</param>
        void Debug(string message);

        /// <summary>
        /// Log an informational message.
        /// </summary>
        /// <param name="message">Message.</param>
        void Info(string message);

        /// <summary>
        /// Log a warning.
        /// </summary>
        /// <param name="message">Message.</param>
        void Warn(string message);

        /// <summary>
        /// Log an error.
        /// </summary>
        /// <param name="message">Message.</param>
        void Error(string message);
    }
}
