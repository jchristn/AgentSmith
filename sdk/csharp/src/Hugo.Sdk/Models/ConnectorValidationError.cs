namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>
    /// A field-level validation error from a connector.
    /// </summary>
    public class ConnectorValidationError
    {
        #region Public-Members

        /// <summary>
        /// Field.
        /// </summary>
        public string Field { get; set; } = String.Empty;

        /// <summary>
        /// Code.
        /// </summary>
        public string Code { get; set; } = String.Empty;

        /// <summary>
        /// Message.
        /// </summary>
        public string Message { get; set; } = String.Empty;

        #endregion
    }
}
