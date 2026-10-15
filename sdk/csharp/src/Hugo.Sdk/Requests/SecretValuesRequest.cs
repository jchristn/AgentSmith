namespace Hugo.Sdk.Requests
{
    using System.Collections.Generic;

    /// <summary>
    /// Replace some field values of a secret. Omitted fields keep their stored values.
    /// </summary>
    public class SecretValuesRequest
    {
        #region Public-Members

        /// <summary>
        /// New field values by field name.
        /// </summary>
        public Dictionary<string, string>? Values { get; set; } = null;

        /// <summary>
        /// Field names to remove.
        /// </summary>
        public List<string>? Remove { get; set; } = null;

        #endregion
    }
}
