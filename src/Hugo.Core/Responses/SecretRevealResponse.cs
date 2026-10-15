namespace Hugo.Core.Responses
{
    using System;

    /// <summary>
    /// Plaintext values of a revealed secret. Never captured in request history.
    /// </summary>
    public class SecretRevealResponse
    {
        /// <summary>
        /// Secret identifier.
        /// </summary>
        public string SecretId { get; set; } = String.Empty;

        /// <summary>
        /// Secret name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Secret kind.
        /// </summary>
        public Hugo.Connectors.Abstractions.SecretKindEnum Kind { get; set; } = Hugo.Connectors.Abstractions.SecretKindEnum.Generic;

        /// <summary>
        /// Plaintext values by field name.
        /// </summary>
        public System.Collections.Generic.Dictionary<string, string> Values { get; set; } = new System.Collections.Generic.Dictionary<string, string>();

        /// <summary>
        /// When the values were revealed (UTC).
        /// </summary>
        public DateTime RevealedUtc { get; set; } = DateTime.UtcNow;
    }
}
