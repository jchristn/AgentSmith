namespace Hugo.Connectors.Abstractions
{
    using System;

    /// <summary>
    /// One field-level validation error.
    /// </summary>
    public class ConnectorValidationError
    {
        /// <summary>
        /// Setting key or secret slot name, or empty for resource-level errors.
        /// </summary>
        public string Field { get; set; } = String.Empty;

        /// <summary>
        /// Stable error code (for example <c>required</c>, <c>out_of_range</c>, <c>invalid_format</c>).
        /// </summary>
        public string Code { get; set; } = String.Empty;

        /// <summary>
        /// English message.
        /// </summary>
        public string Message { get; set; } = String.Empty;

        /// <summary>
        /// Instantiate an empty error.
        /// </summary>
        public ConnectorValidationError()
        {
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="field">Field.</param>
        /// <param name="code">Code.</param>
        /// <param name="message">Message.</param>
        public ConnectorValidationError(string field, string code, string message)
        {
            Field = field ?? String.Empty;
            Code = code ?? String.Empty;
            Message = message ?? String.Empty;
        }
    }
}
