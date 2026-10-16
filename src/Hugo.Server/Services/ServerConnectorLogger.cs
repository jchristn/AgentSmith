namespace Hugo.Server.Services
{
    using System;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Security;
    using Hugo.Core.Telemetry;
    using SyslogLogging;

    /// <summary>
    /// Connector logger over the server log: every line is prefixed with the connector type and resource name and passes
    /// through the secret scrubber (with the invocation's known secret values). Thread-safe.
    /// </summary>
    public class ServerConnectorLogger : IConnectorLogger
    {
        #region Private-Members

        private readonly HugoLog _Logging;
        private readonly string _Prefix;
        private readonly SecretScrubber _Scrubber;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Server log.</param>
        /// <param name="connectorType">Connector type.</param>
        /// <param name="resourceName">Resource name.</param>
        /// <param name="scrubber">Scrubber holding the invocation's secret values.</param>
        /// <exception cref="ArgumentNullException">Thrown when logging or scrubber is null.</exception>
        public ServerConnectorLogger(HugoLog logging, string connectorType, string resourceName, SecretScrubber scrubber)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Scrubber = scrubber ?? throw new ArgumentNullException(nameof(scrubber));
            _Prefix = "[Connector " + connectorType + " " + resourceName + "] ";
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public void Debug(string message)
        {
            _Logging.Debug(_Prefix + _Scrubber.Scrub(message));
        }

        /// <inheritdoc />
        public void Info(string message)
        {
            _Logging.Info(_Prefix + _Scrubber.Scrub(message));
        }

        /// <inheritdoc />
        public void Warn(string message)
        {
            _Logging.Warn(_Prefix + _Scrubber.Scrub(message));
        }

        /// <inheritdoc />
        public void Error(string message)
        {
            _Logging.Error(_Prefix + _Scrubber.Scrub(message));
        }

        #endregion
    }
}
