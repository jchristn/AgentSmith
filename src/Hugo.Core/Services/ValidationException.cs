namespace Hugo.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Raised when input fails validation; mapped to 400 with field errors.
    /// </summary>
    public class ValidationException : Exception
    {
        /// <summary>
        /// Field errors keyed by field path.
        /// </summary>
        public Dictionary<string, string> Fields { get; } = new Dictionary<string, string>();

        /// <summary>
        /// Stable machine codes keyed by the same field paths as <see cref="Fields"/> (for example OutOfRange), so a
        /// client can show a translated message. Fields without an entry are reported with the code Invalid.
        /// </summary>
        public Dictionary<string, string> Codes { get; } = new Dictionary<string, string>();

        /// <summary>
        /// Stable error code for the whole response. Default ValidationFailed; manifest spec errors use ManifestInvalid.
        /// Never null or empty.
        /// </summary>
        public string Code
        {
            get
            {
                return _Code;
            }
            set
            {
                _Code = String.IsNullOrWhiteSpace(value) ? "ValidationFailed" : value;
            }
        }

        private string _Code = "ValidationFailed";

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">Message.</param>
        public ValidationException(string message) : base(message)
        {
        }

        /// <summary>
        /// Instantiate with field errors.
        /// </summary>
        /// <param name="message">Message.</param>
        /// <param name="fields">Field errors.</param>
        public ValidationException(string message, Dictionary<string, string> fields) : base(message)
        {
            if (fields != null) Fields = fields;
        }

        /// <summary>
        /// Instantiate with field errors and their machine codes.
        /// </summary>
        /// <param name="message">Message.</param>
        /// <param name="fields">Field errors.</param>
        /// <param name="codes">Machine codes keyed by field path; null for none.</param>
        public ValidationException(string message, Dictionary<string, string> fields, Dictionary<string, string>? codes) : base(message)
        {
            if (fields != null) Fields = fields;
            if (codes != null) Codes = codes;
        }

        /// <summary>
        /// The machine code for a field: the explicit code, or Invalid.
        /// </summary>
        /// <param name="path">Field path.</param>
        /// <returns>Code.</returns>
        public string CodeFor(string path)
        {
            if (path != null && Codes.TryGetValue(path, out string? code) && !String.IsNullOrEmpty(code)) return code;
            return "Invalid";
        }

        /// <summary>
        /// The message followed by each field error, for protocols that carry only one line of text (MCP, A2A), for
        /// example "One or more run parameters are not allowed. parameters.model: Deployment echo does not allow
        /// overriding model."
        /// </summary>
        /// <returns>Text.</returns>
        public string Describe()
        {
            if (Fields.Count == 0) return Message;
            return Message + " " + String.Join(" ", Fields.Select(f => f.Key + ": " + f.Value));
        }
    }
}
