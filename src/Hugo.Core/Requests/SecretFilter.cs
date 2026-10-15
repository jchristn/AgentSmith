namespace Hugo.Core.Requests
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Filters for listing secrets. Null fields do not filter.
    /// </summary>
    public class SecretFilter
    {
        /// <summary>
        /// Secret kind, for example Password or PrivateKey.
        /// </summary>
        public SecretKindEnum? Kind { get; set; } = null;
    }
}
