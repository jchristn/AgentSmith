namespace Hugo.Connectors.Abstractions
{
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Result of validating resource settings and secret slots.
    /// </summary>
    public class ConnectorValidationResult
    {
        /// <summary>
        /// Errors. Empty when valid. Never null.
        /// </summary>
        public List<ConnectorValidationError> Errors { get; set; } = new List<ConnectorValidationError>();

        /// <summary>
        /// Whether no errors were found.
        /// </summary>
        public bool IsValid
        {
            get
            {
                return Errors == null || !Errors.Any();
            }
        }

        /// <summary>
        /// Add an error and return this result.
        /// </summary>
        /// <param name="field">Field.</param>
        /// <param name="code">Code.</param>
        /// <param name="message">Message.</param>
        /// <returns>This result.</returns>
        public ConnectorValidationResult Add(string field, string code, string message)
        {
            Errors.Add(new ConnectorValidationError(field, code, message));
            return this;
        }

        /// <summary>
        /// A valid result.
        /// </summary>
        /// <returns>Result.</returns>
        public static ConnectorValidationResult Valid()
        {
            return new ConnectorValidationResult();
        }
    }
}
