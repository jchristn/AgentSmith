namespace Hugo.Connectors.Redis
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// INFO of one node, parsed into sections of fields. Not thread-safe for mutation.
    /// </summary>
    public class RedisInfoNode
    {
        /// <summary>
        /// Node endpoint, <c>host:port</c>.
        /// </summary>
        public string Node { get; set; } = String.Empty;

        /// <summary>
        /// Sections (lower case, for example <c>memory</c>) of field to value.
        /// </summary>
        public Dictionary<string, Dictionary<string, string>> Sections { get; set; } = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
    }
}
