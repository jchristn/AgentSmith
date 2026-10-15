namespace Hugo.Core.Requests
{
    using System.Collections.Generic;

    /// <summary>
    /// Replace some or all field values of a secret. Supplied fields are replaced, omitted fields are kept, a value equal
    /// to the mask placeholder (********) keeps the stored value, and fields listed in <see cref="Remove"/> are deleted.
    /// </summary>
    public class SecretValueRequest
    {
        /// <summary>
        /// Field values by field name.
        /// </summary>
        public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Field names to delete, or null.
        /// </summary>
        public List<string>? Remove { get; set; } = null;
    }
}
