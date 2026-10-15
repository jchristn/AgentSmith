namespace Hugo.Connectors.Observability
{
    using System.Collections.Generic;

    /// <summary>
    /// A field mapping (or the root mapping): its type, nested properties for object and nested fields, and
    /// multi-fields.
    /// </summary>
    public class EsFieldMapping
    {
        /// <summary>
        /// Field type (for example <c>keyword</c>, <c>text</c>, <c>date</c>, <c>nested</c>). Null for plain objects.
        /// </summary>
        public string? Type { get; set; } = null;

        /// <summary>
        /// Child fields of an object or nested field.
        /// </summary>
        public Dictionary<string, EsFieldMapping>? Properties { get; set; } = null;

        /// <summary>
        /// Multi-fields (for example <c>name.keyword</c>).
        /// </summary>
        public Dictionary<string, EsFieldMapping>? Fields { get; set; } = null;
    }
}
