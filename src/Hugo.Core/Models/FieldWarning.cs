namespace Hugo.Core.Models
{
    using System;

    /// <summary>
    /// A warning about one field of a request that was accepted but not stored as given (I-08), for example a number
    /// outside its documented range that was clamped to the nearest allowed value. Returned in the warnings array of
    /// REST and MCP create and update responses and on manifest apply items; never stored on the resource itself.
    /// </summary>
    public class FieldWarning
    {
        #region Public-Members

        /// <summary>
        /// Field name as the REST API spells it, for example maxAttempts. Never null.
        /// </summary>
        public string Field
        {
            get
            {
                return _Field;
            }
            set
            {
                _Field = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Stable machine code. ValueClamped: the value was outside the allowed range and the nearest allowed value was
        /// stored. Default ValueClamped. Never null.
        /// </summary>
        public string Code
        {
            get
            {
                return _Code;
            }
            set
            {
                _Code = String.IsNullOrWhiteSpace(value) ? "ValueClamped" : value;
            }
        }

        /// <summary>
        /// The value the caller sent, as text, or null when there was none.
        /// </summary>
        public string? Given { get; set; } = null;

        /// <summary>
        /// The value stored instead, as text, or null when nothing was stored.
        /// </summary>
        public string? Stored { get; set; } = null;

        /// <summary>
        /// Human-readable explanation naming the field, the given value, the allowed range, and the stored value. Never
        /// null.
        /// </summary>
        public string Message
        {
            get
            {
                return _Message;
            }
            set
            {
                _Message = value ?? String.Empty;
            }
        }

        #endregion

        #region Private-Members

        private string _Field = String.Empty;
        private string _Code = "ValueClamped";
        private string _Message = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty warning.
        /// </summary>
        public FieldWarning()
        {
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="field">Field name.</param>
        /// <param name="code">Machine code.</param>
        /// <param name="given">Given value as text.</param>
        /// <param name="stored">Stored value as text.</param>
        /// <param name="message">Message.</param>
        public FieldWarning(string field, string code, string? given, string? stored, string message)
        {
            Field = field;
            Code = code;
            Given = given;
            Stored = stored;
            Message = message;
        }

        #endregion
    }
}
